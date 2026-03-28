using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using src.Controllers;
using src.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Xunit;

namespace tests
{
    public class PostControllerTests : IDisposable
    {
        private GoreDBContext? _context;
        private readonly Mock<ILogger<PostController>> _loggerMock;
        private readonly IConfiguration _config;
        private readonly PostController _controller;

        public PostControllerTests()
        {
            // Use a unique database name per test run to avoid collisions
            var options = new DbContextOptionsBuilder<GoreDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new GoreDBContext(options);
            _loggerMock = new Mock<ILogger<PostController>>();
            _config = new Mock<IConfiguration>().Object;

            _controller = new PostController(_loggerMock.Object, _config, _context);
        }

        public void Dispose()
        {
            if (_context != null)
            {
                _context.Dispose();
                _context = null;
            }
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

        // -------------------- AllPosts Tests --------------------

        [Fact]
        public void AllPosts_WithValidOffsetAndLimit_ReturnsOkWithPosts()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var mountainId = Guid.NewGuid();
            var user = new User { Id = userId, Username = "user1", PasswordHash = "hash", Role = "user" };
            _context!.Users.Add(user);
            _context.Mountains.Add(new Mountain { Id = mountainId, Name = "Everest" });
            _context.Posts.AddRange(
                new Post
                {
                    Id = 1,
                    CreatedBy = userId,
                    Tagline = "First",
                    StartMsg = "Hello",
                    Timestamp = DateTime.UtcNow,
                    MountainId = mountainId,
                    CreatedByNavigation = user
                },
                new Post
                {
                    Id = 2,
                    CreatedBy = userId,
                    Tagline = "Second",
                    StartMsg = "World",
                    Timestamp = DateTime.UtcNow.AddMinutes(-1),
                    MountainId = mountainId,
                    CreatedByNavigation = user
                }
            );
            _context.SaveChanges();

            // Act
            var result = _controller.AllPosts(offset: 0, limit: 10);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var posts = Assert.IsAssignableFrom<IEnumerable>(okResult.Value);
            Assert.Equal(2, posts.Cast<object>().Count());
        }

        [Fact]
        public void AllPosts_WithNegativeOffset_ReturnsBadRequest()
        {
            // Act
            var result = _controller.AllPosts(offset: -1, limit: 10);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid pagination parameters.", badRequestResult.Value);
        }

        [Fact]
        public void AllPosts_WithZeroOrNegativeLimit_ReturnsBadRequest()
        {
            // Act (limit = 0)
            var result1 = _controller.AllPosts(offset: 0, limit: 0);
            // Assert
            var badRequestResult1 = Assert.IsType<BadRequestObjectResult>(result1);
            Assert.Equal("Invalid pagination parameters.", badRequestResult1.Value);

            // Act (limit = -5)
            var result2 = _controller.AllPosts(offset: 0, limit: -5);
            // Assert
            var badRequestResult2 = Assert.IsType<BadRequestObjectResult>(result2);
            Assert.Equal("Invalid pagination parameters.", badRequestResult2.Value);
        }

        [Fact]
        public void AllPosts_WhenExceptionOccurs_ReturnsInternalServerError()
        {
            // Arrange – force exception by disposing the context
            _context!.Dispose();
            _context = null; // prevent Dispose from trying to use it again

            // Act
            var result = _controller.AllPosts(0, 10);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Equal("Internal server error", statusCodeResult.Value);

            // Verify logger was called
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }


        [Fact]
        public void AllComments_WithInvalidPostId_ReturnsNotFound()
        {
            // Arrange – no posts in DB
            var postId = 99;

            // Act
            var result = _controller.AllComments(postId);

            // Assert
            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal($"Post with ID {postId} not found.", notFoundResult.Value);
        }

        [Fact]
        public void AllComments_WhenExceptionOccurs_ReturnsInternalServerError()
        {
            // Arrange – force exception by disposing the context
            _context!.Dispose();
            _context = null;

            // Act
            var result = _controller.AllComments(1);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Equal("Internal server error", statusCodeResult.Value);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        // -------------------- CreatePost Tests --------------------
        [Fact]
        public void CreatePost_WithValidRequestAndMountain_ReturnsCreatedAtAction()
        {
            // Arrange
            var userId = Guid.NewGuid();
            SetupUser(userId);
            var mountainId = Guid.NewGuid();
            _context!.Mountains.Add(new Mountain { Id = mountainId, Name = "Mountain" });
            _context.SaveChanges();

            var request = new PostController.CreatePostRequest("Tagline", "Message", mountainId);

            // Act
            var result = _controller.CreatePost(request);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result);
            Assert.Equal(nameof(PostController.AllPosts), createdResult.ActionName);
            var savedPost = _context.Posts.First();
            Assert.Equal(mountainId, savedPost.MountainId);
        }

        [Fact]
        public void CreatePost_WithMissingTaglineOrMessage_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(Guid.NewGuid());

            // Act – missing tagline
            var request1 = new PostController.CreatePostRequest("", "Message", null);
            var result1 = _controller.CreatePost(request1);
            Assert.IsType<BadRequestObjectResult>(result1);
            Assert.Equal("Tagline and message are required.", ((BadRequestObjectResult)result1).Value);

            // Act – missing message
            var request2 = new PostController.CreatePostRequest("Tagline", "", null);
            var result2 = _controller.CreatePost(request2);
            Assert.IsType<BadRequestObjectResult>(result2);
            Assert.Equal("Tagline and message are required.", ((BadRequestObjectResult)result2).Value);
        }

        [Fact]
        public void CreatePost_WithInvalidUserId_ReturnsUnauthorized()
        {
            // Arrange – User has no NameIdentifier claim
            var claims = new List<Claim>();
            var identity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(identity);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };

            var request = new PostController.CreatePostRequest("Tagline", "Message", null);

            // Act
            var result = _controller.CreatePost(request);

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Equal("Invalid user identity in token.", unauthorizedResult.Value);
        }

        [Fact]
        public void CreatePost_WithNonExistentMountain_ReturnsNotFound()
        {
            // Arrange
            SetupUser(Guid.NewGuid());
            var nonExistentMountainId = Guid.NewGuid();
            var request = new PostController.CreatePostRequest("Tagline", "Message", nonExistentMountainId);

            // Act
            var result = _controller.CreatePost(request);

            // Assert
            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("The specified mountain was not found in our database.", notFoundResult.Value);
            Assert.Empty(_context!.Posts); // No post saved
        }

        [Fact]
        public void CreatePost_WhenExceptionOccurs_ReturnsInternalServerError()
        {
            // Arrange
            SetupUser(Guid.NewGuid());
            var request = new PostController.CreatePostRequest("Tagline", "Message", null);

            // Force exception by disposing the context
            _context!.Dispose();
            _context = null;

            // Act
            var result = _controller.CreatePost(request);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Equal("An error occurred while saving your post.", statusCodeResult.Value);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        // -------------------- AddComment Tests --------------------
        [Fact]
        public void AddComment_WithEmptyMessage_ReturnsBadRequest()
        {
            // Arrange
            SetupUser(Guid.NewGuid());
            var request = new PostController.CreateCommentRequest("");

            // Act
            var result = _controller.AddComment(1, request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Message cannot be empty.", badRequestResult.Value);
            Assert.Empty(_context!.PostComments);
        }

        [Fact]
        public void AddComment_WithInvalidPostId_ReturnsNotFound()
        {
            // Arrange
            SetupUser(Guid.NewGuid());
            var request = new PostController.CreateCommentRequest("Message");

            // Act
            var result = _controller.AddComment(99, request);

            // Assert
            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("The post you are trying to comment on does not exist.", notFoundResult.Value);
            Assert.Empty(_context!.PostComments);
        }

        [Fact]
        public void AddComment_WhenUserNotAuthenticated_ReturnsUnauthorized()
        {
            // Arrange – no User claims
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            };
            var request = new PostController.CreateCommentRequest("Message");

            // Act
            var result = _controller.AddComment(1, request);

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Equal("User ID not found in token.", unauthorizedResult.Value);
        }

        [Fact]
        public void AddComment_WhenExceptionOccurs_ReturnsInternalServerError()
        {
            // Arrange
            SetupUser(Guid.NewGuid());
            var request = new PostController.CreateCommentRequest("Message");

            // Force exception by disposing the context
            _context!.Dispose();
            _context = null;

            // Act
            var result = _controller.AddComment(1, request);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Equal("Internal server error", statusCodeResult.Value);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }
    }
}