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

            // FIX: Setup default config behavior to prevent NullReferenceException on GetValue calls
            var mockSection = new Mock<IConfigurationSection>();
            mockSection.Setup(s => s.Value).Returns("false");
            _configMock.Setup(c => c.GetSection(It.IsAny<string>())).Returns(mockSection.Object);

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
                MessageId = message.Id,
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

            // FIX: Explicitly set spam filter to true for this test
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

        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        public async Task MakeBoard_InvalidDifficulty_ReturnsBadRequest(int difficulty)
        {
            SetAuthenticatedUser(Guid.NewGuid().ToString());
            var request = new BoardDTO(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), difficulty, 100, "Description", Guid.NewGuid());

            var result = await _controller.MakeBoard(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task MakeBoard_PastExpiryDate_ReturnsBadRequest()
        {
            SetAuthenticatedUser(Guid.NewGuid().ToString());
            var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var request = new BoardDTO(pastDate, 3, 100, "Description", Guid.NewGuid());

            var result = await _controller.MakeBoard(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetBoardById_ReturnsOk_WhenBoardExists()
        {
            var boardId = Guid.NewGuid();
            var message = new Message { Content = "Climbing trip", IsSpam = false };
            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            var board = new Board
            {
                Id = boardId,
                MessageId = message.Id,
                ExpiryDate = DateOnly.FromDateTime(DateTime.Now),
                UserId = Guid.NewGuid(),
                MountainId = Guid.NewGuid()
            };
            _context.Boards.Add(board);
            await _context.SaveChangesAsync();

            var result = await _controller.GetBoardById(boardId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<BoardDetailDto>(okResult.Value);
            Assert.Equal("Climbing trip", dto.Description);
        }

        [Fact]
        public async Task GetBoardById_ReturnsNotFound_WhenBoardDoesNotExist()
        {
            var result = await _controller.GetBoardById(Guid.NewGuid());
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task GetBoardChats_FiltersSpam_AndReturnsOnlyCleanMessages()
        {
            var boardId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            _context.Boards.Add(new Board { Id = boardId, UserId = userId, ExpiryDate = DateOnly.MaxValue });
            _context.Users.Add(new User { Id = userId, Username = "MountainGuide", PasswordHash = "x", Role = "User" });

            var msg1 = new Message { Content = "Clean message", IsSpam = false };
            var msg2 = new Message { Content = "Spam bot", IsSpam = true };
            _context.Messages.AddRange(msg1, msg2);
            await _context.SaveChangesAsync();

            _context.BoardChats.AddRange(
                new BoardChat { BoardId = boardId, UserId = userId, MessageId = msg1.Id, Timestamp = DateTime.UtcNow },
                new BoardChat { BoardId = boardId, UserId = userId, MessageId = msg2.Id, Timestamp = DateTime.UtcNow }
            );
            await _context.SaveChangesAsync();

            var result = await _controller.GetBoardChats(boardId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var chats = Assert.IsAssignableFrom<IEnumerable<BoardChatDto>>(okResult.Value);
            Assert.Single(chats);
            Assert.Equal("Clean message", chats.First().Msg);
        }

        [Fact]
        public async Task CreateBoardChat_ValidComment_ReturnsCreated()
        {
            // Arrange
            var boardId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            _context.Boards.Add(new Board { Id = boardId, UserId = userId, ExpiryDate = DateOnly.MaxValue });
            await _context.SaveChangesAsync();

            SetAuthenticatedUser(userId.ToString());

            // FIX: Explicitly set spam filter for this specific test call
            var sectionMock = new Mock<IConfigurationSection>();
            sectionMock.Setup(s => s.Value).Returns("true");
            _configMock.Setup(c => c.GetSection("useSpamFilter")).Returns(sectionMock.Object);

            var request = new CreateBoardChatRequest("I'm coming too!");
            _modelManagerMock.Setup(m => m.Predict(request.Message))
                .Returns(new ModelOutput { IsSpam = false, ConfidencePercentage = 95f });

            // Act
            var result = await _controller.CreateBoardChat(boardId, request);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result);
            Assert.Equal(nameof(_controller.GetBoardChats), createdResult.ActionName);
            Assert.True(await _context.BoardChats.AnyAsync(c => c.BoardId == boardId));
        }

        [Fact]
        public async Task CreateBoardChat_WhenDetectedAsSpam_ReturnsBadRequest()
        {
            // Arrange
            var boardId = Guid.NewGuid();
            var board = new Board
            {
                Id = boardId,
                UserId = Guid.NewGuid(),
                ExpiryDate = DateOnly.MaxValue
            };
            _context.Boards.Add(board);
            await _context.SaveChangesAsync();

            SetAuthenticatedUser(Guid.NewGuid().ToString());

            // Setup config to return true for the filter
            var sectionMock = new Mock<IConfigurationSection>();
            sectionMock.Setup(s => s.Value).Returns("True"); // Use "True" for boolean parsing safety
            _configMock.Setup(c => c.GetSection("useSpamFilter")).Returns(sectionMock.Object);

            // Mock AI to detect spam
            var request = new CreateBoardChatRequest("SPAM_CONTENT_HERE");
            _modelManagerMock.Setup(m => m.Predict(It.IsAny<string>()))
                .Returns(new ModelOutput { IsSpam = true, ConfidencePercentage = 99.9f });

            // Act
            var result = await _controller.CreateBoardChat(boardId, request);

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);

            // In your controller, you returned a raw string, not a new { message = "..." }
            Assert.Equal("The comment includes inappropriate context.", badRequest.Value);
        }
    }
}