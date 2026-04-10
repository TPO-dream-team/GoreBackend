using Microsoft.Extensions.ML;
using Microsoft.ML;
using Microsoft.ML.Data;

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

public interface IModelManager
{
    ModelOutput Predict(string input);
    void Refit(IEnumerable<ModelInput> newData);
    BinaryClassificationMetrics GetMetrics(IEnumerable<ModelInput> testData);
}

public class ModelManager : IModelManager
{
    private readonly MLContext _mlContext;
    private readonly PredictionEnginePool<ModelInput, ModelOutput> _modelPool;
    private readonly string _modelPath = "Assets/model.zip";
    private ITransformer _model;

    public ModelManager(PredictionEnginePool<ModelInput, ModelOutput> modelPool)
    {
        _mlContext = new MLContext(seed: 0);
        _modelPool = modelPool;

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

    public void Refit(IEnumerable<ModelInput> newData)
    {
        IDataView newDataView = _mlContext.Data.LoadFromEnumerable(newData);

        var pipeline = _mlContext.Transforms.Text.FeaturizeText("Features", nameof(ModelInput.Message))
            .Append(_mlContext.BinaryClassification.Trainers.SdcaLogisticRegression());

        _model = pipeline.Fit(newDataView);

        _mlContext.Model.Save(_model, newDataView.Schema, _modelPath);
    }

    public BinaryClassificationMetrics GetMetrics(IEnumerable<ModelInput> testData)
    {
        IDataView testDataView = _mlContext.Data.LoadFromEnumerable(testData);
        var predictions = _model.Transform(testDataView);

        return _mlContext.BinaryClassification.Evaluate(predictions);
    }
}