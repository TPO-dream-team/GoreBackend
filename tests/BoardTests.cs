using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using src.Controllers;
using src.Models;
using System.Linq.Expressions;
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
        private readonly BoardController _controller;

        public BoardControllerTests()
        {
            // In-memory database options
            var options = new DbContextOptionsBuilder<GoreDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new GoreDBContext(options);
            _loggerMock = new Mock<ILogger<BoardController>>();
            _configMock = new Mock<IConfiguration>();

            _controller = new BoardController(_loggerMock.Object, _configMock.Object, _context);
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        // Helper to set authenticated user
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

        // ---------- GetBoards ----------
        [Fact]
        public async Task GetBoards_ReturnsOk_WithActiveBoards()
        {
            // Arrange
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = "testuser",
                PasswordHash = "hashedpassword123", // Required property
                Role = "User" // Required property
            };
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
            var board2 = new Board
            {
                Id = Guid.NewGuid(),
                ExpiryDate = today.AddDays(2),
                UserId = user.Id,
                MountainId = Guid.NewGuid(),
                Description = "Test2",
                TourTime = 4,
                Difficulty = 5
            };
            var expiredBoard = new Board
            {
                Id = Guid.NewGuid(),
                ExpiryDate = today.AddDays(-1),
                UserId = user.Id,
                MountainId = Guid.NewGuid(),
                Description = "Expired",
                TourTime = 1,
                Difficulty = 1
            };

            _context.Users.Add(user);
            _context.Boards.AddRange(board1, board2, expiredBoard);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetBoards();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var boards = Assert.IsType<List<BoardListDto>>(okResult.Value);
            Assert.Equal(2, boards.Count);
            Assert.Equal(board1.Id, boards[0].BoardId);
            Assert.Equal(board2.Id, boards[1].BoardId);
        }

        // ---------- MakeBoard ----------
        [Fact]
        public async Task MakeBoard_WithValidData_ReturnsCreated()
        {
            // Arrange
            var userId = Guid.NewGuid();
            SetAuthenticatedUser(userId.ToString());

            var mountainId = Guid.NewGuid();
            _context.Mountains.Add(new Mountain { Id = mountainId, Name = "Test Mountain" });
            await _context.SaveChangesAsync();

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
            Assert.IsType<CreatedResult>(result);
            var board = await _context.Boards.FirstOrDefaultAsync();
            Assert.NotNull(board);
            Assert.Equal(userId, board.UserId);
            Assert.Equal(request.ExpiryDate, board.ExpiryDate);
        }

        [Fact]
        public async Task MakeBoard_WhenUserNotAuthenticated_ReturnsUnauthorized()
        {
            // Arrange: set an empty/unauthenticated user
            var identity = new ClaimsIdentity(); // Empty identity - not authenticated
            var principal = new ClaimsPrincipal(identity);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };

            var request = new BoardController.BoardDTO(DateOnly.MaxValue, 1, 1, "", Guid.NewGuid());

            // Act
            var result = await _controller.MakeBoard(request);

            // Assert
            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Equal("User ID not found in token.", unauthorized.Value);
        }

        [Fact]
        public async Task MakeBoard_WhenMountainNotFound_ReturnsNotFound()
        {
            // Arrange
            SetAuthenticatedUser(Guid.NewGuid().ToString());
            var request = new BoardDTO(DateOnly.MaxValue, 1, 1, "", Guid.NewGuid());

            // Act
            var result = await _controller.MakeBoard(request);

            // Assert
            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("The specified mountain was not found in our database.", notFound.Value);
        }

        
        // ---------- GetBoardById ----------
        [Fact]
        public async Task GetBoardById_WithExistingId_ReturnsBoard()
        {
            // Arrange
            var boardId = Guid.NewGuid();
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = "user",
                PasswordHash = "hashedpassword123", // Add required property
                Role = "User" // Add required property
            };
            var board = new Board
            {
                Id = boardId,
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow),
                UserId = user.Id,
                MountainId = Guid.NewGuid(),
                Description = "Test",
                TourTime = 3,
                Difficulty = 2
            };
            _context.Users.Add(user);
            _context.Boards.Add(board);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetBoardById(boardId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<BoardDetailDto>(okResult.Value);
            Assert.Equal(boardId, dto.Id);
            Assert.Equal(board.Description, dto.Description);
        }

        [Fact]
        public async Task GetBoardById_WithNonExistingId_ReturnsNotFound()
        {
            // Act
            var result = await _controller.GetBoardById(Guid.NewGuid());

            // Assert
            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Contains("not found", notFound.Value.ToString());
        }

        // ---------- GetBoardChats ----------
        [Fact]
        public async Task GetBoardChats_WithExistingBoard_ReturnsChats()
        {
            // Arrange
            var boardId = Guid.NewGuid();
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = "chatty",
                PasswordHash = "hashedpassword123",
                Role = "User"
            };
            var board = new Board { Id = boardId, UserId = user.Id, ExpiryDate = DateOnly.MaxValue };
            var chat1 = new BoardChat
            {
                Id = 1,
                BoardId = boardId,
                UserId = user.Id,
                Msg = "Hello",
                Timestamp = DateTime.UtcNow.AddMinutes(-5)
            };
            var chat2 = new BoardChat
            {
                Id = 2,
                BoardId = boardId,
                UserId = user.Id,
                Msg = "World",
                Timestamp = DateTime.UtcNow
            };
            _context.Users.Add(user);
            _context.Boards.Add(board);
            _context.BoardChats.AddRange(chat1, chat2);
            await _context.SaveChangesAsync();

            // Act
            var result = await _controller.GetBoardChats(boardId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var chats = Assert.IsType<List<BoardChatDto>>(okResult.Value);
            Assert.Equal(2, chats.Count);
            Assert.Equal("Hello", chats[0].Msg);
            Assert.Equal("World", chats[1].Msg);
        }

        [Fact]
        public async Task GetBoardChats_WithNonExistingBoard_ReturnsNotFound()
        {
            // Act
            var result = await _controller.GetBoardChats(Guid.NewGuid());

            // Assert
            Assert.IsType<NotFoundObjectResult>(result);
        }

        // ---------- CreateBoardChat ----------
        [Fact]
        public async Task CreateBoardChat_WithValidData_ReturnsCreated()
        {
            // Arrange
            var boardId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            SetAuthenticatedUser(userId.ToString());

            var board = new Board { Id = boardId, UserId = userId, ExpiryDate = DateOnly.MaxValue };
            _context.Boards.Add(board);
            await _context.SaveChangesAsync();

            var request = new CreateBoardChatRequest("  Hello world!  ");

            // Act
            var result = await _controller.CreateBoardChat(boardId, request);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result);
            Assert.Equal(nameof(BoardController.GetBoardChats), createdResult.ActionName);
            Assert.Equal(boardId, createdResult.RouteValues["id"]);
            var response = Assert.IsType<BoardChatCreatedResponse>(createdResult.Value);
            Assert.Equal("Message posted.", response.Message);

            var chat = await _context.BoardChats.FirstOrDefaultAsync();
            Assert.NotNull(chat);
            Assert.Equal(boardId, chat.BoardId);
            Assert.Equal(userId, chat.UserId);
            Assert.Equal("Hello world!", chat.Msg); // trimmed
        }

        [Fact]
        public async Task CreateBoardChat_WhenBoardNotFound_ReturnsNotFound()
        {
            // Arrange
            SetAuthenticatedUser(Guid.NewGuid().ToString());
            var request = new CreateBoardChatRequest("Hello");

            // Act
            var result = await _controller.CreateBoardChat(Guid.NewGuid(), request);

            // Assert
            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task CreateBoardChat_WhenUserNotAuthenticated_ReturnsUnauthorized()
        {
            // Arrange
            var boardId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            var board = new Board
            {
                Id = boardId,
                UserId = userId,
                ExpiryDate = DateOnly.MaxValue
            };
            _context.Boards.Add(board);
            await _context.SaveChangesAsync();

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity())
                }
            };

            var request = new BoardController.CreateBoardChatRequest("Hello");

            // Act
            var result = await _controller.CreateBoardChat(boardId, request);

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task CreateBoardChat_WithEmptyMessage_ReturnsBadRequest()
        {
            // Arrange
            SetAuthenticatedUser(Guid.NewGuid().ToString());
            var boardId = Guid.NewGuid();
            _context.Boards.Add(new Board { Id = boardId, UserId = Guid.NewGuid(), ExpiryDate = DateOnly.MaxValue });
            await _context.SaveChangesAsync();

            var request = new CreateBoardChatRequest("   ");

            // Act
            var result = await _controller.CreateBoardChat(boardId, request);

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Message is required.", badRequest.Value);
        }
    }
}