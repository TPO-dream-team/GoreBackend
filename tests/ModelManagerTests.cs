using Moq;
using src.AI;
using Xunit;

namespace tests
{
    public class ModelManagerTests : IDisposable
    {
        private readonly string _tempModelPath;
        private readonly Mock<IModelMetricsStore> _metricsStoreMock;
        private readonly Mock<IPredictionService> _predictionServiceMock;
        private readonly int _requiredRows = 5;

        public ModelManagerTests()
        {
            _tempModelPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
            _metricsStoreMock = new Mock<IModelMetricsStore>();
            _predictionServiceMock = new Mock<IPredictionService>();

            _metricsStoreMock.Setup(m => m.Get()).Returns(new ModelMetricsSnapshot());
        }

        public void Dispose()
        {
            if (File.Exists(_tempModelPath))
            {
                File.Delete(_tempModelPath);
            }
        }

        [Fact]
        public void Predict_WhenFileDoesNotExist_ReturnsDefaultOutput()
        {
            // Arrange
            if (File.Exists(_tempModelPath)) File.Delete(_tempModelPath);
            var manager = new ModelManager(_predictionServiceMock.Object, _metricsStoreMock.Object, _tempModelPath, _requiredRows);

            // Act
            var result = manager.Predict("some text");

            // Assert
            Assert.False(result.IsSpam);
            Assert.Equal(0, result.ConfidencePercentage);
            _predictionServiceMock.Verify(x => x.Predict(It.IsAny<ModelInput>()), Times.Never);
        }

        [Fact]
        public void Predict_WhenFileExists_CallsServiceAndCalculatesConfidence()
        {
            // Arrange
            File.WriteAllText(_tempModelPath, "dummy model content"); // Create dummy file
            var manager = new ModelManager(_predictionServiceMock.Object, _metricsStoreMock.Object, _tempModelPath, _requiredRows);

            var expectedOutput = new ModelOutput { IsSpam = true, Probability = 0.7f };
            _predictionServiceMock.Setup(x => x.Predict(It.IsAny<ModelInput>())).Returns(expectedOutput);

            // Act
            var result = manager.Predict("spammy message");

            // Assert
            Assert.Equal(0.7f, result.ConfidencePercentage);
            _predictionServiceMock.Verify(x => x.Predict(It.Is<ModelInput>(i => i.Message == "spammy message")), Times.Once);
        }

        [Theory]
        [InlineData(true, 0.9f, 0.9f)]
        [InlineData(false, 0.1f, 0.9f)]
        [InlineData(true, 0.5f, 0.5f)] 
        public void CalculateConfidence_ReturnsCorrectValue(bool isSpam, float probability, float expected)
        {
            // Arrange
            var manager = new ModelManager(_predictionServiceMock.Object, _metricsStoreMock.Object, _tempModelPath, _requiredRows);

            // Act
            var actual = manager.CalculateConfidence(isSpam, probability);

            // Assert
            Assert.Equal(expected, actual, precision: 3);
        }

        [Fact]
        public void Refit_BuffersData_UntilThresholdReached()
        {
            // Arrange
            var manager = new ModelManager(_predictionServiceMock.Object, _metricsStoreMock.Object, _tempModelPath, _requiredRows);
            var input = new List<ModelInput> { new ModelInput { Message = "Valid Message" } };

            // Act
            var result = manager.Refit(input);

            // Assert
            Assert.Contains("Total: 1", result);

            _metricsStoreMock.Verify(x => x.Save(It.IsAny<ModelMetricsSnapshot>()), Times.Never);
        }

        [Fact]
        public void PrepareData_SplitsTenPercentToTest()
        {
            // Arrange
            var manager = new ModelManager(_predictionServiceMock.Object, _metricsStoreMock.Object, _tempModelPath, _requiredRows);
            var data = Enumerable.Range(0, 100).Select(i => new ModelInput { Message = $"Msg {i}" }).ToList();

            // Act
            var (train, test) = manager.PrepareData(data);

            // Assert
            Assert.Equal(90, train.Count);
            Assert.Equal(10, test.Count);
        }

        [Fact]
        public void GetMetrics_ReturnsFromStore()
        {
            // Arrange
            var expected = new ModelMetricsSnapshot { F1Score = 0.88, TrainingRun = 5 };
            _metricsStoreMock.Setup(s => s.Get()).Returns(expected);
            var manager = new ModelManager(_predictionServiceMock.Object, _metricsStoreMock.Object, _tempModelPath, _requiredRows);

            // Act
            var result = manager.GetMetrics();

            // Assert
            Assert.Equal(0.88, result.F1Score);
            Assert.Equal(5, result.TrainingRun);
        }

        [Fact]
        public void Refit_WithNull_ReturnsCurrentCount()
        {
            // Arrange
            var manager = new ModelManager(_predictionServiceMock.Object, _metricsStoreMock.Object, _tempModelPath, _requiredRows);

            // Act
            var result = manager.Refit(null);

            // Assert
            Assert.Equal("Batch size: 0", result);
        }
    }
}