using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using src.AI;
using src.Controllers;
using src.Models;
using System.Security.Claims;
using Xunit;
using static src.Controllers.BoardController;

namespace tests
{
    public class BoardControllerTests : IDisposable
    {
        private readonly GoreDBContext _context;
        private readonly Mock<ILogger<BoardController>> _loggerMock;
        private readonly Mock<IConfiguration> _configMock;
        private readonly Mock<IModelManager> _modelManagerMock;
        private readonly BoardController _controller;

        public BoardControllerTests()
        {
            var options = new DbContextOptionsBuilder<GoreDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new GoreDBContext(options);
            _loggerMock = new Mock<ILogger<BoardController>>();
            _configMock = new Mock<IConfiguration>();
            _modelManagerMock = new Mock<IModelManager>();

            // Setup default Predict behavior to avoid null refs in logic
            _modelManagerMock.Setup(m => m.Predict(It.IsAny<string>()))
                .Returns(new ModelOutput { IsSpam = false, ConfidencePercentage = 10f });

            _controller = new BoardController(
                _loggerMock.Object,
                _configMock.Object,
                _context,
                _modelManagerMock.Object);
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        private void SetAuthenticatedUser(string userId)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId) };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
        }

        [Fact]
        public async Task GetBoards_ReturnsOk_WithActiveBoards()
        {
            // Arrange
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var user = new User { Id = Guid.NewGuid(), Username = "testuser", PasswordHash = "p", Role = "User" };

            // CRITICAL: Controller joins with Messages table. Must create Message first.
            var message = new Message { Content = "Test Description", IsSpam = false };
            _context.Messages.Add(message);
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var board1 = new Board
            {
                Id = Guid.NewGuid(),
                ExpiryDate = today.AddDays(1),
                UserId = user.Id,
                MountainId = Guid.NewGuid(),
                MessageId = message.Id, // Link the message
                TourTime = 2,
                Difficulty = 3
            };
            _context.Boards.Add(board1);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetBoards();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var boards = Assert.IsAssignableFrom<IEnumerable<BoardListDto>>(okResult.Value);
            Assert.Single(boards);
        }

        [Fact]
        public async Task MakeBoard_WithValidData_ReturnsCreated()
        {
            // Arrange
            var userId = Guid.NewGuid();
            SetAuthenticatedUser(userId.ToString());
            var mountainId = Guid.NewGuid();
            _context.Mountains.Add(new Mountain { Id = mountainId, Name = "Test Mountain" });
            await _context.SaveChangesAsync();

            // Setup config for GetValue<bool>
            var sectionMock = new Mock<IConfigurationSection>();
            sectionMock.Setup(s => s.Value).Returns("false");
            _configMock.Setup(c => c.GetSection("useSpamFilter")).Returns(sectionMock.Object);

            var request = new BoardDTO(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), 3, 4, "Let's climb!", mountainId);

            // Act
            var result = await _controller.MakeBoard(request);

            // Assert
            Assert.IsType<CreatedResult>(result);
            Assert.True(await _context.Boards.AnyAsync(b => b.TourTime == 4));
        }

        [Fact]
        public async Task MakeBoard_WhenSpamDetected_ReturnsBadRequest()
        {
            // Arrange
            SetAuthenticatedUser(Guid.NewGuid().ToString());

            var sectionMock = new Mock<IConfigurationSection>();
            sectionMock.Setup(s => s.Value).Returns("true");
            _configMock.Setup(c => c.GetSection("useSpamFilter")).Returns(sectionMock.Object);

            _modelManagerMock.Setup(m => m.Predict(It.IsAny<string>()))
                .Returns(new ModelOutput { IsSpam = true, ConfidencePercentage = 99f });

            var mountainId = Guid.NewGuid();
            _context.Mountains.Add(new Mountain { Id = mountainId, Name = "Mtn" });
            await _context.SaveChangesAsync();

            var request = new BoardDTO(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), 3, 4, "Spammy text", mountainId);

            // Act
            var result = await _controller.MakeBoard(request);

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("The description includes inappropriate context.", badRequest.Value);
        }
    }
}