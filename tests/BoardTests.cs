using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using src.AI; // Added for IModelManager
using src.Controllers;
using src.Models;
using System.Security.Claims;
using System.Text.Json;
using Xunit;
using static src.Controllers.BoardController;

namespace tests
{
    public class BoardControllerTests : IDisposable
    {
        private readonly GoreDBContext _context;
        private readonly Mock<ILogger<BoardController>> _loggerMock;
        private readonly Mock<IConfiguration> _configMock;
        private readonly Mock<IModelManager> _modelManagerMock; // Added Mock
        private readonly BoardController _controller;

        public BoardControllerTests()
        {
            var options = new DbContextOptionsBuilder<GoreDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new GoreDBContext(options);
            _loggerMock = new Mock<ILogger<BoardController>>();
            _configMock = new Mock<IConfiguration>();
            _configMock.Setup(c => c.GetSection("useSpamFilter")).Returns(new Mock<IConfigurationSection>().Object);
            _modelManagerMock = new Mock<IModelManager>(); // Initialize Mock

            // Pass the 4th argument: _modelManagerMock.Object
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
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId)
            };
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
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = "testuser",
                PasswordHash = "hashedpassword123",
                Role = "User"
            };
            _context.Users.Add(user);

            var board1 = new Board
            {
                Id = Guid.NewGuid(),
                ExpiryDate = today.AddDays(1),
                UserId = user.Id,
                MountainId = Guid.NewGuid(),
                Description = "Test1",
                TourTime = 2,
                Difficulty = 3
            };
            _context.Boards.Add(board1);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetBoards();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var boards = Assert.IsType<List<BoardListDto>>(okResult.Value);
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

            // Mock Spam Filter Config to false by default
            _configMock.Setup(c => c.GetSection("useSpamFilter").Value).Returns("false");

            var request = new BoardDTO(
                ExpiryDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
                Difficulty: 3,
                TourTime: 4,
                Description: "Let's climb!",
                MountainId: mountainId
            );

            // Act
            var result = await _controller.MakeBoard(request);

            // Assert
            Assert.IsType<CreatedResult>(result); // matches return Created();
            var board = await _context.Boards.FirstOrDefaultAsync(b => b.Description == "Let's climb!");
            Assert.NotNull(board);
        }

        [Fact]
        public async Task MakeBoard_WhenSpamDetected_ReturnsBadRequest()
        {
            // Arrange
            SetAuthenticatedUser(Guid.NewGuid().ToString());
            _configMock.Setup(m => m.GetSection("useSpamFilter").Value).Returns("true");

            // Setup the model manager to return IsSpam = true
            _modelManagerMock.Setup(m => m.Predict(It.IsAny<string>()))
                .Returns(new ModelOutput { IsSpam = true });

            _context.Mountains.Add(new Mountain { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Mtn" });
            await _context.SaveChangesAsync();

            var request = new BoardDTO(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), 3, 4, "Bad words", Guid.Parse("00000000-0000-0000-0000-000000000001"));

            // Act
            var result = await _controller.MakeBoard(request);

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("The description you wrote includes inappropriate context.", badRequest.Value);
        }

        [Fact]
        public async Task GetBoardById_WithExistingId_ReturnsBoard()
        {
            var boardId = Guid.NewGuid();
            var user = new User { Id = Guid.NewGuid(), Username = "u", PasswordHash = "p", Role = "r" };
            var board = new Board { Id = boardId, UserId = user.Id, ExpiryDate = DateOnly.MaxValue, Description = "Test" };
            _context.Users.Add(user);
            _context.Boards.Add(board);
            await _context.SaveChangesAsync();

            var result = await _controller.GetBoardById(boardId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<BoardDetailDto>(okResult.Value);
            Assert.Equal(boardId, dto.Id);
        }

        [Fact]
        public async Task CreateBoardChat_WithValidData_ReturnsCreatedAtAction()
        {
            var boardId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            SetAuthenticatedUser(userId.ToString());
            _context.Boards.Add(new Board { Id = boardId, UserId = userId, ExpiryDate = DateOnly.MaxValue });
            await _context.SaveChangesAsync();

            var request = new CreateBoardChatRequest("Hello");

            var result = await _controller.CreateBoardChat(boardId, request);

            var createdResult = Assert.IsType<CreatedAtActionResult>(result);
            Assert.Equal(nameof(BoardController.GetBoardChats), createdResult.ActionName);
        }

        private static string ExtractMessage(object? value)
        {
            if (value is null) return string.Empty;
            if (value is string s) return s;
            var json = JsonSerializer.Serialize(value);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var msg)) return msg.GetString() ?? "";
            return value.ToString() ?? "";
        }
    }
}