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

public interface IModelMetricsStore
{
    ModelMetricsSnapshot Get();
    void Save(ModelMetricsSnapshot snapshot);
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
    private readonly int _requiredTotalRows;

    private readonly MLContext _mlContext;
    private readonly PredictionEnginePool<ModelInput, ModelOutput> _modelPool;
    private readonly IModelMetricsStore _metricsStore;
    private readonly string _modelPath;
    private readonly object _sync = new();

    private readonly List<ModelInput> _pendingBuffer = new();

    public ModelManager(
        PredictionEnginePool<ModelInput, ModelOutput> modelPool,
        IModelMetricsStore metricsStore,
        string modelPath,
        int requiredTotalRows)
    {
        _mlContext = new MLContext(seed: 0);
        _modelPool = modelPool;
        _metricsStore = metricsStore;
        _modelPath = modelPath;
        _requiredTotalRows = requiredTotalRows;
    }

    public ModelOutput Predict(string input)
    {
        // Guard against missing model file
        if (!File.Exists(_modelPath))
        {
            return new ModelOutput { IsSpam = false, ConfidencePercentage = 0 };
        }

        var result = _modelPool.Predict(modelName: "ClassifierModel", new ModelInput { Message = input });

        // Logic extracted for clarity and testing
        result.ConfidencePercentage = CalculateConfidence(result.IsSpam, result.Probability);

        return result;
    }

    public float CalculateConfidence(bool isSpam, float probability)
        => isSpam ? probability : (1.0f - probability);

    public string Refit(IEnumerable<ModelInput> newData)
    {
        if (newData == null) return $"Batch size: {_pendingBuffer.Count}";

        var cleanedBatch = newData
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Message))
            .ToList();

        List<ModelInput>? dataToTrain = null;

        lock (_sync)
        {
            _pendingBuffer.AddRange(cleanedBatch);

            if (_pendingBuffer.Count >= _requiredTotalRows)
            {
                dataToTrain = new List<ModelInput>(_pendingBuffer);
                _pendingBuffer.Clear();
            }
        }

        if (dataToTrain != null)
        {
            if (TrainAndSaveModel(dataToTrain))
                return "Model successfully retrained.";
            else
                return "Model didn't retrain successfully.";
        }

        return $"Data added to batch. Total: {_pendingBuffer.Count}";
    }


    private bool TrainAndSaveModel(List<ModelInput> allRows) 
    {
        try
        {
            var (trainRows, testRows) = PrepareData(allRows);

            if (!trainRows.Any()) return false;

            IDataView trainDataView = _mlContext.Data.LoadFromEnumerable(trainRows);

            var pipeline = _mlContext.Transforms.Text.FeaturizeText("Features", nameof(ModelInput.Message))
                .Append(_mlContext.BinaryClassification.Trainers.SdcaLogisticRegression());

            ITransformer trainedModel = pipeline.Fit(trainDataView);

            var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            _mlContext.Model.Save(trainedModel, trainDataView.Schema, tempPath);
            File.Move(tempPath, _modelPath, overwrite: true);

            bool hasPositiveTest = testRows.Any(x => x.IsSpam);
            bool hasNegativeTest = testRows.Any(x => !x.IsSpam);

            if (hasPositiveTest && hasNegativeTest)
            {
                var testDataView = _mlContext.Data.LoadFromEnumerable(testRows);
                var predictions = trainedModel.Transform(testDataView);
                var metrics = _mlContext.BinaryClassification.Evaluate(predictions);

                _metricsStore.Save(new ModelMetricsSnapshot
                {
                    TrainingRun = _metricsStore.Get().TrainingRun + 1,
                    Precision = metrics.PositivePrecision,
                    Recall = metrics.PositiveRecall,
                    F1Score = metrics.F1Score,
                    TrainedAtUtc = DateTime.UtcNow,
                    TrainingExamples = trainRows.Count,
                    TestExamples = testRows.Count
                });
            }
            else
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public (List<ModelInput> train, List<ModelInput> test) PrepareData(List<ModelInput> data)
    {
        var shuffled = data.OrderBy(_ => Random.Shared.Next()).ToList();
        int testCount = (int)(shuffled.Count * 0.1);

        return (shuffled.Skip(testCount).ToList(), shuffled.Take(testCount).ToList());
    }

    public ModelMetricsSnapshot GetMetrics() => _metricsStore.Get();
}
