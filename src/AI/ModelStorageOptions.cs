namespace src.AI;

public class ModelStorageOptions
{
    public const string SectionName = "ModelStorage";
    public string ModelPath { get; set; } = "Assets/model.zip";
    public string MetricsPath { get; set; } = "Assets/model-metrics.json";
    public int RequiredTotalRows { get; set; } = 100;
}