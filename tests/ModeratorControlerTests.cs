using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using src.AI;
using src.Controllers;
using src.Models;
using Xunit;

namespace tests
{
    public class ModeratorControllerTests : IDisposable
    {
        private readonly GoreDBContext _context;
        private readonly Mock<IModelManager> _modelManagerMock;
        private readonly Mock<ILogger<BoardController>> _loggerMock;
        private readonly ModeratorController _controller;

        public ModeratorControllerTests()
        {
            var options = new DbContextOptionsBuilder<GoreDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new GoreDBContext(options);
            _modelManagerMock = new Mock<IModelManager>();
            _loggerMock = new Mock<ILogger<BoardController>>();

            var configMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();

            _controller = new ModeratorController(
                _loggerMock.Object,
                configMock.Object,
                _context,
                _modelManagerMock.Object);
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        [Fact]
        public async Task SubmitTrainingData_MessageExists_UpdatesDbAndCallsRefit()
        {
            // Arrange
            var msg = new Message { Id = 1, Content = "Bad link here", IsSpam = false, IsSpamConf = 0.6 };
            _context.Messages.Add(msg);
            await _context.SaveChangesAsync();

            _modelManagerMock.Setup(m => m.Refit(It.IsAny<IEnumerable<ModelInput>>()))
                .Returns("Model successfully retrained.");

            // Act
            var result = await _controller.SubmitTrainingData(1, true);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);

            // Verify Database Update
            var updatedMsg = await _context.Messages.FindAsync(1);
            Assert.True(updatedMsg.IsSpam);
            Assert.Equal(1.0, updatedMsg.IsSpamConf);

            // Verify AI Manager Call
            _modelManagerMock.Verify(m => m.Refit(It.Is<IEnumerable<ModelInput>>(list =>
                list.First().Message == "Bad link here" && list.First().IsSpam == true)), Times.Once);
        }

        [Fact]
        public async Task SubmitTrainingData_MessageNotFound_ReturnsNotFound()
        {
            // Act
            var result = await _controller.SubmitTrainingData(999, true);

            // Assert
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetAmbiguousMessage_ReturnsMessageWithLowestConfidence()
        {
            // Arrange
            _context.Messages.AddRange(
                new Message { Id = 1, Content = "Very Confident", IsSpamConf = 0.99 },
                new Message { Id = 2, Content = "Somewhat Ambiguous", IsSpamConf = 0.75 },
                new Message { Id = 3, Content = "Most Ambiguous", IsSpamConf = 0.55 }
            );
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetAmbiguousMessage();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<ModeratorController.AmbiguousMessageDto>(okResult.Value);

            // Should be ID 3 because 0.55 is the lowest score < 0.95
            Assert.Equal(3, dto.Id);
            Assert.Equal(0.55, dto.Confidence);
        }

        [Fact]
        public async Task GetAmbiguousMessage_NoLowConfidenceMessages_ReturnsNotFound()
        {
            // Arrange
            _context.Messages.Add(new Message { Id = 1, Content = "Safe", IsSpamConf = 0.98 });
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetAmbiguousMessage();

            // Assert
            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("No messages found with active spam filter scores.", notFoundResult.Value);
        }

        [Fact]
        public void GetModelMetrics_ReturnsDataFromManager()
        {
            // Arrange
            var expectedMetrics = new ModelMetricsSnapshot { F1Score = 0.92, TrainingRun = 10 };
            _modelManagerMock.Setup(m => m.GetMetrics()).Returns(expectedMetrics);

            // Act
            var result = _controller.GetModelMetrics();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var actualMetrics = Assert.IsType<ModelMetricsSnapshot>(okResult.Value);
            Assert.Equal(0.92, actualMetrics.F1Score);
            _modelManagerMock.Verify(m => m.GetMetrics(), Times.Once);
        }
    }
}