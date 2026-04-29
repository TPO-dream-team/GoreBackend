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

namespace tests
{
    public class PostControllerTests : IDisposable
    {
        private GoreDBContext _context;
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

            // Default config: spam filter OFF
            var mockSection = new Mock<IConfigurationSection>();
            mockSection.Setup(s => s.Value).Returns("false");
            _configMock.Setup(c => c.GetSection("useSpamFilter")).Returns(mockSection.Object);

            // Default AI: Not Spam
            _modelManagerMock.Setup(m => m.Predict(It.IsAny<string>()))
                .Returns(new ModelOutput { IsSpam = false });

            _controller = new PostController(
                _loggerMock.Object,
                _configMock.Object,
                _context,
                _modelManagerMock.Object);
        }

        public void Dispose()
        {
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

            _context.Users.Add(user);
            _context.Mountains.Add(mountain);
            _context.Posts.AddRange(
                new Post { Id = 1, CreatedBy = userId, Tagline = "First", StartMsg = "Hello", Timestamp = DateTime.UtcNow, MountainId = mountainId, CreatedByNavigation = user, Mountain = mountain },
                new Post { Id = 2, CreatedBy = userId, Tagline = "Second", StartMsg = "World", Timestamp = DateTime.UtcNow.AddMinutes(-1), MountainId = mountainId, CreatedByNavigation = user, Mountain = mountain }
            );
            _context.SaveChanges();

            // Act
            var result = _controller.AllPosts(offset: 0, limit: 10);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var posts = Assert.IsType<List<PostController.PostListDto>>(okResult.Value);
            Assert.Equal(2, posts.Count);
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

            var request = new PostController.CreatePostRequest("Tagline", "Message", mountainId);

            // Act
            var result = _controller.CreatePost(request);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result);
            Assert.Equal(nameof(PostController.AllPosts), createdResult.ActionName);
            var savedPost = _context.Posts.First();
            Assert.Equal(mountainId, savedPost.MountainId);
            Assert.Equal("Message", savedPost.StartMsg);
        }

        [Fact]
        public void CreatePost_WithSpamContent_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(Guid.NewGuid());
            _configMock.Setup(c => c.GetSection("useSpamFilter").Value).Returns("true");
            _modelManagerMock.Setup(m => m.Predict(It.IsAny<string>()))
                .Returns(new ModelOutput { IsSpam = true });

            var request = new PostController.CreatePostRequest("Spam", "Bad Content", null);

            // Act
            var result = _controller.CreatePost(request);

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("The post you wrote includes inappropriate context.", badRequest.Value);
        }

        [Fact]
        public void AddComment_WithValidRequest_AddsAndSavesNewComment()
        {
            // Arrange
            var userId = Guid.NewGuid();
            SetupUser(userId);

            _context.Users.Add(new User { Id = userId, Username = "testuser", PasswordHash = "hash", Role = "user" });
            var postId = 1;
            _context.Posts.Add(new Post { Id = postId, CreatedBy = userId, Tagline = "Test", StartMsg = "Test", Timestamp = DateTime.UtcNow });
            _context.SaveChanges();

            var request = new PostController.CreateCommentRequest("Awesome comment!");

            // Act
            var result = _controller.AddComment(postId, request);

            // Assert
            Assert.IsType<CreatedResult>(result);
            var savedComment = _context.PostComments.FirstOrDefault(c => c.PostId == postId);
            Assert.NotNull(savedComment);
            Assert.Equal("Awesome comment!", savedComment.Message);
        }

        [Fact]
        public void AllComments_WithExistingPostAndComments_ReturnsOrderedComments()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var user = new User { Id = userId, Username = "commenter", PasswordHash = "hash", Role = "user" };
            _context.Users.Add(user);

            var postId = 42;
            _context.Posts.Add(new Post { Id = postId, CreatedBy = userId, Tagline = "Test", StartMsg = "Content", Timestamp = DateTime.UtcNow });

            _context.PostComments.AddRange(
                new PostComment { Id = 1, PostId = postId, CreatedBy = userId, Message = "First", Timestamp = DateTime.UtcNow.AddHours(-2), CreatedByNavigation = user },
                new PostComment { Id = 2, PostId = postId, CreatedBy = userId, Message = "Second", Timestamp = DateTime.UtcNow.AddHours(-1), CreatedByNavigation = user }
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