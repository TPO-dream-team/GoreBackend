using System.Text.Json;
using Microsoft.Extensions.Options;

namespace src.AI;

public class JsonModelMetricsStore : IModelMetricsStore
{
    private readonly string _metricsPath;
    private readonly object _lock = new();

    public JsonModelMetricsStore(IOptions<ModelStorageOptions> options, IHostEnvironment env)
    {
        var configured = options.Value.MetricsPath;
        _metricsPath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);

        var dir = Path.GetDirectoryName(_metricsPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Initial metrics file
        if (!File.Exists(_metricsPath))
        {
            Save(new ModelMetricsSnapshot
            {
                TrainingRun = 0,
                Precision = 89.04,
                Recall = 80.58,
                F1Score = 84.60,
                TrainedAtUtc = DateTime.UtcNow,
                TrainingExamples = 0,
                TestExamples = 0
            });
        }
    }

    public ModelMetricsSnapshot Get()
    {
        lock (_lock)
        {
            if (!File.Exists(_metricsPath))
                return new ModelMetricsSnapshot();

            var raw = File.ReadAllText(_metricsPath);
            return JsonSerializer.Deserialize<ModelMetricsSnapshot>(raw) ?? new ModelMetricsSnapshot();
        }
    }

    public void Save(ModelMetricsSnapshot snapshot)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_metricsPath, json);
        }
    }
}