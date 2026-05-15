using Moq;
using src.AI;
using src.Models;
using Microsoft.ML;
using Xunit;

public class ModelManagerTrainingTests : IDisposable
{
    private readonly Mock<IPredictionService> _mockPredictionService;
    private readonly Mock<IModelMetricsStore> _mockMetricsStore;
    private readonly string _testModelPath;

    public ModelManagerTrainingTests()
    {
        _mockPredictionService = new Mock<IPredictionService>();
        _mockMetricsStore = new Mock<IModelMetricsStore>();

        // Setup a unique path for the model zip file
        _testModelPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");

        // Mock the metrics store to return a default snapshot
        _mockMetricsStore.Setup(m => m.Get()).Returns(new ModelMetricsSnapshot { TrainingRun = 0 });
    }

    // Cleanup the physical file after each test
    public void Dispose()
    {
        if (File.Exists(_testModelPath)) File.Delete(_testModelPath);
    }

    [Fact]
    public void Refit_WhenDataIsNull_ReturnsCurrentBufferSize()
    {
        // Arrange: Start with a required row count of 10
        var manager = new ModelManager(_mockPredictionService.Object, _mockMetricsStore.Object, _testModelPath, 10, 0.1);

        // Act
        var result = manager.Refit(null!);

        // Assert
        Assert.Equal("Batch size: 0", result);
    }

    [Fact]
    public void Refit_WhenThresholdNotMet_BuffersData()
    {
        // Arrange
        var manager = new ModelManager(_mockPredictionService.Object, _mockMetricsStore.Object, _testModelPath, 10, 0.1);
        var newData = new List<ModelInput> { new ModelInput { Message = "Test", IsSpam = true } };

        // Act
        var result = manager.Refit(newData);

        // Assert
        Assert.Equal("Data added to batch. Total: 1", result);
        Assert.False(File.Exists(_testModelPath)); // Model should not be saved yet
    }

    [Fact]
    public void Refit_WhenTestDataIsImbalanced_ReturnsFailure()
    {

        var manager = new ModelManager(_mockPredictionService.Object, _mockMetricsStore.Object, _testModelPath, 2, 0.1);
        var unbalancedData = new List<ModelInput>
        {
            new ModelInput { Message = "Spam 1", IsSpam = true },
            new ModelInput { Message = "Spam 2", IsSpam = true },
            new ModelInput { Message = "Spam 3", IsSpam = true }
        };

        // Act
        var result = manager.Refit(unbalancedData);

        // Assert
        Assert.Equal("Model didn't retrain successfully.", result);
    }

    [Fact]
    public void Refit_ThreadSafety_HandlesConcurrentUpdates()
    {
        // Arrange
        var manager = new ModelManager(_mockPredictionService.Object, _mockMetricsStore.Object, _testModelPath, 100, 0.1);
        var tasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() => manager.Refit(new List<ModelInput> { new ModelInput { Message = "msg", IsSpam = false } })));
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        var finalStatus = manager.Refit(null!);
        Assert.Equal("Batch size: 50", finalStatus);
    }
}