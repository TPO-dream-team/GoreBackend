using Microsoft.Extensions.ML;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.Extensions.Options;

namespace src.AI;

public class ModelInput
{
    [ColumnName("Label")]
    public bool IsSpam { get; set; }

    [LoadColumn(0)]
    public string Message { get; set; }
}

public class ModelOutput
{
    [ColumnName("PredictedLabel")]
    public bool IsSpam { get; set; }

    // This is the raw score (log-odds)
    public float Score { get; set; }

    // This is the calibrated probability (0.0 to 1.0)
    public float Probability { get; set; }

    // Kako je confident v svoj prediction (to se bo shranilo v db)
    public float ConfidencePercentage { get; set; }
}

public class MetricsRow
{
    [ColumnName("Label")]
    public bool Label { get; set; }

    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }
}

public interface IModelManager
{
    ModelOutput Predict(string input);
    string Refit(IEnumerable<ModelInput> newData);
    ModelMetricsSnapshot GetMetrics();
}

public class ModelMetricsSnapshot
{
    public int TrainingRun { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double F1Score { get; set; }
    public DateTime TrainedAtUtc { get; set; }
    public int TrainingExamples { get; set; }
    public int TestExamples { get; set; }
}

public class ModelManager : IModelManager
{
    private const int RequiredTotalRows = 100;

    private readonly MLContext _mlContext;
    private readonly PredictionEnginePool<ModelInput, ModelOutput> _modelPool;
    private readonly IModelMetricsStore _metricsStore;
    private readonly string _modelPath;
    private readonly object _sync = new();

    private ITransformer _model;
    private readonly List<ModelInput> _pendingBuffer = new();

    public ModelManager(
        PredictionEnginePool<ModelInput, ModelOutput> modelPool,
        IModelMetricsStore metricsStore,
        IOptions<ModelStorageOptions> storageOptions,
        IHostEnvironment hostEnvironment)
    {
        _mlContext = new MLContext(seed: 0);
        _modelPool = modelPool;
        _metricsStore = metricsStore;

        var configuredModelPath = storageOptions.Value.ModelPath;
        _modelPath = Path.IsPathRooted(configuredModelPath)
            ? configuredModelPath
            : Path.Combine(hostEnvironment.ContentRootPath, configuredModelPath);

        var modelDirectory = Path.GetDirectoryName(_modelPath);
        if (!string.IsNullOrWhiteSpace(modelDirectory))
        {
            Directory.CreateDirectory(modelDirectory);
        }

        if (File.Exists(_modelPath))
            _model = _mlContext.Model.Load(_modelPath, out _);
    }

    public ModelOutput Predict(string input)
    {
        var result = _modelPool.Predict(modelName: "ClassifierModel", new ModelInput { Message = input });

        float actualConfidence = result.IsSpam
        ? result.Probability
        : (1.0f - result.Probability);

        result.ConfidencePercentage = actualConfidence;

        return result;
    }

    public string Refit(IEnumerable<ModelInput> newData)
    {
        if (newData is null)
        {
            return "data added to retrain batch 0";
        }

        var cleanedBatch = newData
            .Where(x => x is not null && !string.IsNullOrWhiteSpace(x.Message))
            .ToList();

        if (cleanedBatch.Count == 0)
        {
            return $"data added to retrain batch {_pendingBuffer.Count}";
        }

        lock (_sync)
        {
            _pendingBuffer.AddRange(cleanedBatch);

            bool shouldRetrain = _pendingBuffer.Count >= RequiredTotalRows;

            if (!shouldRetrain)
            {
                return $"Data added to retrain batch. #data: {_pendingBuffer.Count}";
            }

            TrainAndSaveModel();
            _pendingBuffer.Clear();
            return "Model successfully retrained.";
        }
    }

    public ModelMetricsSnapshot GetMetrics()
    {
        return _metricsStore.Get();
    }

    private void TrainAndSaveModel()
    {
        var previousMetrics = GetMetrics();

        var allRows = _pendingBuffer
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        var positives = allRows.Where(x => x.IsSpam).OrderBy(_ => Random.Shared.Next()).ToList();
        var negatives = allRows.Where(x => !x.IsSpam).OrderBy(_ => Random.Shared.Next()).ToList();

        int positiveTestCount = positives.Count > 0
            ? Math.Clamp((int)Math.Round(positives.Count * 0.1, MidpointRounding.AwayFromZero), 1, positives.Count)
            : 0;

        int negativeTestCount = negatives.Count > 0
            ? Math.Clamp((int)Math.Round(negatives.Count * 0.1, MidpointRounding.AwayFromZero), 1, negatives.Count)
            : 0;

        var testRows = positives.Take(positiveTestCount)
            .Concat(negatives.Take(negativeTestCount))
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        var trainRows = positives.Skip(positiveTestCount)
            .Concat(negatives.Skip(negativeTestCount))
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        if (trainRows.Count == 0 || testRows.Count == 0)
        {
            return;
        }

        IDataView trainDataView = _mlContext.Data.LoadFromEnumerable(trainRows);

        var pipeline = _mlContext.Transforms.Text.FeaturizeText("Features", nameof(ModelInput.Message))
            .Append(_mlContext.BinaryClassification.Trainers.SdcaLogisticRegression());

        _model = pipeline.Fit(trainDataView);
        _mlContext.Model.Save(_model, trainDataView.Schema, _modelPath);

        IDataView testDataView = _mlContext.Data.LoadFromEnumerable(testRows);
        var predictions = _model.Transform(testDataView);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions);

        var snapshot = new ModelMetricsSnapshot
        {
            TrainingRun = previousMetrics.TrainingRun + 1,
            Precision = metrics.PositivePrecision,
            Recall = metrics.PositiveRecall,
            F1Score = metrics.F1Score,
            TrainedAtUtc = DateTime.UtcNow,
            TrainingExamples = trainRows.Count,
            TestExamples = testRows.Count
        };

        _metricsStore.Save(snapshot);
    }
}