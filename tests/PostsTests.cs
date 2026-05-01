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

namespace tests
{
    public class PostControllerTests : IDisposable
    {
        private readonly GoreDBContext _context;
        private readonly Mock<ILogger<PostController>> _loggerMock;
        private readonly Mock<IModelManager> _modelManagerMock;
        private readonly Mock<IConfiguration> _configMock;
        private readonly PostController _controller;

        public PostControllerTests()
        {
            var options = new DbContextOptionsBuilder<GoreDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new GoreDBContext(options);
            _loggerMock = new Mock<ILogger<PostController>>();
            _modelManagerMock = new Mock<IModelManager>();
            _configMock = new Mock<IConfiguration>();

            // Default AI: Not Spam
            _modelManagerMock.Setup(m => m.Predict(It.IsAny<string>()))
                .Returns(new ModelOutput { IsSpam = false, ConfidencePercentage = 10f });

            _controller = new PostController(
                _loggerMock.Object,
                _configMock.Object,
                _context,
                _modelManagerMock.Object);
        }

        public void Dispose()
        {
            _context?.Database.EnsureDeleted();
            _context?.Dispose();
        }

        private void SetupUser(Guid userId, string username = "testuser")
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username)
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
        }

        [Fact]
        public void AllPosts_WithValidOffsetAndLimit_ReturnsOkWithPosts()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var mountainId = Guid.NewGuid();
            var user = new User { Id = userId, Username = "user1", PasswordHash = "hash", Role = "user" };
            var mountain = new Mountain { Id = mountainId, Name = "Everest" };

            // Controller selects p.Message.Content, so Message objects are mandatory
            var msg1 = new Message { Content = "Hello", IsSpam = false };
            var msg2 = new Message { Content = "World", IsSpam = false };

            _context.Users.Add(user);
            _context.Mountains.Add(mountain);
            _context.Messages.AddRange(msg1, msg2);
            _context.SaveChanges();

            _context.Posts.AddRange(
                new Post { Id = 1, CreatedBy = userId, Tagline = "First", MessageId = msg1.Id, Timestamp = DateTime.UtcNow, MountainId = mountainId },
                new Post { Id = 2, CreatedBy = userId, Tagline = "Second", MessageId = msg2.Id, Timestamp = DateTime.UtcNow.AddMinutes(-1), MountainId = mountainId }
            );
            _context.SaveChanges();

            // Act
            var result = _controller.AllPosts(offset: 0, limit: 10);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var posts = Assert.IsAssignableFrom<IEnumerable<PostController.PostListDto>>(okResult.Value);
            Assert.Equal(2, posts.Count());
        }

        [Fact]
        public void CreatePost_WithValidRequestAndMountain_ReturnsCreatedAtAction()
        {
            // Arrange
            var userId = Guid.NewGuid();
            SetupUser(userId);
            var mountainId = Guid.NewGuid();
            _context.Mountains.Add(new Mountain { Id = mountainId, Name = "Mountain" });
            _context.SaveChanges();

            // Setup Config for GetValue<bool>
            var mockSection = new Mock<IConfigurationSection>();
            mockSection.Setup(s => s.Value).Returns("false");
            _configMock.Setup(c => c.GetSection("useSpamFilter")).Returns(mockSection.Object);

            var request = new PostController.CreatePostRequest("Tagline", "Message", mountainId);

            // Act
            var result = _controller.CreatePost(request);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result);
            Assert.Equal(nameof(PostController.GetPostById), createdResult.ActionName);
            Assert.True(_context.Posts.Any(p => p.Tagline == "Tagline"));
        }

        [Fact]
        public void CreatePost_WithSpamContent_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(Guid.NewGuid());

            var mockSection = new Mock<IConfigurationSection>();
            mockSection.Setup(s => s.Value).Returns("true");
            _configMock.Setup(c => c.GetSection("useSpamFilter")).Returns(mockSection.Object);

            _modelManagerMock.Setup(m => m.Predict(It.IsAny<string>()))
                .Returns(new ModelOutput { IsSpam = true, ConfidencePercentage = 95f });

            var request = new PostController.CreatePostRequest("Spam", "Bad Content", null);

            // Act
            var result = _controller.CreatePost(request);

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("The comment includes inappropriate context.", badRequest.Value);
        }

        [Fact]
        public void AddComment_WithValidRequest_AddsAndSavesNewComment()
        {
            // Arrange
            var userId = Guid.NewGuid();
            SetupUser(userId);

            var mockSection = new Mock<IConfigurationSection>();
            mockSection.Setup(s => s.Value).Returns("false");
            _configMock.Setup(c => c.GetSection("useSpamFilter")).Returns(mockSection.Object);

            _context.Users.Add(new User { Id = userId, Username = "testuser", PasswordHash = "hash", Role = "user" });
            var postId = 1;
            _context.Posts.Add(new Post { Id = postId, CreatedBy = userId, Tagline = "Test", MessageId = 99, Timestamp = DateTime.UtcNow });
            _context.SaveChanges();

            var request = new PostController.CreateCommentRequest("Awesome comment!");

            // Act
            var result = _controller.AddComment(postId, request);

            // Assert
            Assert.IsType<CreatedResult>(result);
            Assert.True(_context.PostComments.Any(c => c.PostId == postId));
        }

        [Fact]
        public void AllComments_WithExistingPostAndComments_ReturnsOrderedComments()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var user = new User { Id = userId, Username = "commenter", PasswordHash = "hash", Role = "user" };
            _context.Users.Add(user);

            var postId = 42;
            _context.Posts.Add(new Post { Id = postId, CreatedBy = userId, Tagline = "Test", Timestamp = DateTime.UtcNow });

            var m1 = new Message { Content = "First", IsSpam = false };
            var m2 = new Message { Content = "Second", IsSpam = false };
            _context.Messages.AddRange(m1, m2);
            _context.SaveChanges();

            _context.PostComments.AddRange(
                new PostComment { PostId = postId, CreatedBy = userId, MessageId = m1.Id, Timestamp = DateTime.UtcNow.AddHours(-2) },
                new PostComment { PostId = postId, CreatedBy = userId, MessageId = m2.Id, Timestamp = DateTime.UtcNow.AddHours(-1) }
            );
            _context.SaveChanges();

            // Act
            var result = _controller.AllComments(postId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var comments = Assert.IsType<List<PostController.CommentDto>>(okResult.Value);
            Assert.Equal(2, comments.Count);
            Assert.Equal("First", comments[0].Message);
            Assert.Equal("commenter", comments[0].Username);
        }
    }
}