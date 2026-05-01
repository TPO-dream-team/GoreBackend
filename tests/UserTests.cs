using BCrypt.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using src.Controllers;
using src.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;

namespace tests
{
    public class UserControllerTests : IDisposable
    {
        private readonly GoreDBContext _context;
        private readonly Mock<ILogger<UserController>> _loggerMock;
        private readonly IConfiguration _configuration;
        private readonly UserController _controller;

        public UserControllerTests()
        {
            // In-memory database
            var options = new DbContextOptionsBuilder<GoreDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _context = new GoreDBContext(options);

            _loggerMock = new Mock<ILogger<UserController>>();

            // Setup JWT configuration
            var jwtSettings = new Dictionary<string, string>
            {
                ["Jwt:Key"] = "a-very-long-secret-key-that-is-at-least-32-bytes-long!",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:ExpiresInDays"] = "7"
            };
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(jwtSettings)
                .Build();

            _controller = new UserController(_loggerMock.Object, _configuration, _context);
            // Set default ControllerContext with HttpContext to avoid null reference
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }

        // ---------------------- Helper Methods ----------------------
        private void SetAuthenticatedUser(Guid userId)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, "testuser")
            };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);
            _controller.ControllerContext.HttpContext.User = principal;
        }

        private void SetAuthorizationHeader(string token)
        {
            _controller.ControllerContext.HttpContext.Request.Headers["Authorization"] = $"Bearer {token}";
        }

        private User CreateTestUser(string username = "testuser", string password = "password123")
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = "user"
            };
            _context.Users.Add(user);
            _context.SaveChanges();
            return user;
        }

        private Mountain CreateTestMountain(Guid? id = null, string name = "Test Mountain", string nfc = "test-nfc")
        {
            var mountain = new Mountain
            {
                Id = id ?? Guid.NewGuid(),
                Name = name,
                Height = 1000,
                RegionId = 1,
                Lat = 45.0m,
                Lon = 10.0m,
                Nfc = nfc
            };
            _context.Mountains.Add(mountain);
            _context.SaveChanges();
            return mountain;
        }

        private Scan CreateTestScan(int scanId, Guid userId, Guid mountainId)
        {
            var scan = new Scan
            {
                Id = scanId,
                UserId = userId,
                MountainId = mountainId
            };
            _context.Scans.Add(scan);
            _context.SaveChanges();
            return scan;
        }

        private string GenerateJwtToken(User user)
        {
            var jwt = _configuration.GetSection("Jwt");
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, "user")
            };
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddDays(int.Parse(jwt["ExpiresInDays"])),
                signingCredentials: creds
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // ---------------------- Register Tests ----------------------
        [Fact]
        public void Register_ValidUser_ReturnsCreated()
        {
            // Arrange
            var request = new UserController.RegisterUser("newuser", "password123", "password123");

            // Act
            var result = _controller.Register(request);

            // Assert
            Assert.IsType<CreatedResult>(result);
            var user = _context.Users.SingleOrDefault(u => u.Username == "newuser");
            Assert.NotNull(user);
            Assert.True(BCrypt.Net.BCrypt.Verify("password123", user.PasswordHash));
            Assert.Equal("user", user.Role);
        }

        [Fact]
        public void Register_UsernameMissing_ReturnsBadRequest()
        {
            var request = new UserController.RegisterUser("", "pass", "pass");
            var result = _controller.Register(request);
            AssertBadRequest(result, "Please fill out the whole form.");
        }

        [Fact]
        public void Register_PasswordMissing_ReturnsBadRequest()
        {
            var request = new UserController.RegisterUser("user", "", "pass");
            var result = _controller.Register(request);
            AssertBadRequest(result, "Please fill out the whole form.");
        }

        [Fact]
        public void Register_RepeatPasswordMissing_ReturnsBadRequest()
        {
            var request = new UserController.RegisterUser("user", "pass", "");
            var result = _controller.Register(request);
            AssertBadRequest(result, "Please fill out the whole form.");
        }

        [Fact]
        public void Register_UsernameAlreadyExists_ReturnsBadRequest()
        {
            // Arrange
            var existingUser = CreateTestUser("existing");

            var request = new UserController.RegisterUser("existing", "pass", "pass");

            // Act
            var result = _controller.Register(request);

            // Assert
            AssertBadRequest(result, "This username already exists.");
        }

        [Fact]
        public void Register_PasswordsDoNotMatch_ReturnsBadRequest()
        {
            var request = new UserController.RegisterUser("user", "pass1", "pass2");
            var result = _controller.Register(request);
            AssertBadRequest(result, "Passwords do not match.");
        }

        // ---------------------- Login Tests ----------------------
        [Fact]
        public void Login_ValidCredentials_ReturnsOkWithToken()
        {
            // Arrange
            var user = CreateTestUser("testuser", "password123");
            var request = new UserController.LoginUser("testuser", "password123");

            // Act
            var result = _controller.Login(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            // Serialize the anonymous object to JSON and parse to get token
            var json = JsonSerializer.Serialize(okResult.Value);
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
            string token = jsonElement.GetProperty("token").GetString();
            Assert.NotNull(token);
            // Verify token can be validated
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            Assert.Equal(user.Id.ToString(), jwtToken.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
            Assert.Equal("testuser", jwtToken.Claims.First(c => c.Type == ClaimTypes.Name).Value);
        }

        [Fact]
        public void Login_MissingUsername_ReturnsBadRequest()
        {
            var request = new UserController.LoginUser("", "pass");
            var result = _controller.Login(request);
            AssertBadRequest(result, "Please fill out the whole form.");
        }

        [Fact]
        public void Login_MissingPassword_ReturnsBadRequest()
        {
            var request = new UserController.LoginUser("user", "");
            var result = _controller.Login(request);
            AssertBadRequest(result, "Please fill out the whole form.");
        }

        [Fact]
        public void Login_UserNotFound_ReturnsUnauthorized()
        {
            var request = new UserController.LoginUser("unknown", "pass");
            var result = _controller.Login(request);
            AssertUnauthorized(result, "Invalid username or password.");
        }

        [Fact]
        public void Login_WrongPassword_ReturnsUnauthorized()
        {
            var user = CreateTestUser("testuser", "correct");
            var request = new UserController.LoginUser("testuser", "wrong");
            var result = _controller.Login(request);
            AssertUnauthorized(result, "Invalid username or password.");
        }

        // ---------------------- Refresh Tests ----------------------
        [Fact]
        public void Refresh_ValidToken_ReturnsNewToken()
        {
            // Arrange
            var user = CreateTestUser("testuser", "password");
            var oldToken = GenerateJwtToken(user);
            SetAuthorizationHeader(oldToken);

            // Počaka 1s da je token zagotovo nov
            System.Threading.Thread.Sleep(1001);

            // Act
            var result = _controller.Refresh();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(okResult.Value);
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
            string newToken = jsonElement.GetProperty("token").GetString();
            Assert.NotNull(newToken);
            Assert.NotEqual(oldToken, newToken);
            // Verify new token contains same user info
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(newToken);
            Assert.Equal(user.Id.ToString(), jwtToken.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
            Assert.Equal(user.Username, jwtToken.Claims.First(c => c.Type == ClaimTypes.Name).Value);
        }

        [Fact]
        public void Refresh_InvalidTokenFormat_ReturnsUnauthorized()
        {
            SetAuthorizationHeader("invalid-token");
            var result = _controller.Refresh();
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.StartsWith("Could not refresh token:", unauthorizedResult.Value.ToString());
        }

        [Fact]
        public void Refresh_TokenMissingClaims_ReturnsBadRequest()
        {
            // Generate token without NameIdentifier and Name claims
            var jwt = _configuration.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: new[] { new Claim("some", "claim") },
                expires: DateTime.UtcNow.AddDays(1),
                signingCredentials: creds
            );
            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            SetAuthorizationHeader(tokenString);

            var result = _controller.Refresh();
            AssertBadRequest(result, "Token missing required claims.");
        }

        [Fact]
        public void Refresh_TokenWithWrongKey_ReturnsUnauthorized()
        {
            var wrongKey = "wrong-key-that-is-not-the-same-as-the-one-in-config";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(wrongKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) },
                expires: DateTime.UtcNow.AddDays(1),
                signingCredentials: creds
            );
            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            SetAuthorizationHeader(tokenString);

            var result = _controller.Refresh();
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.StartsWith("Could not refresh token:", unauthorizedResult.Value.ToString());
        }

        // ---------------------- Username by ID ----------------------
        [Fact]
        public void Username_ExistingUser_ReturnsOkWithUsername()
        {
            var user = CreateTestUser("testuser");
            var result = _controller.Username(user.Id);
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("testuser", okResult.Value);
        }

        [Fact]
        public void Username_NonExistingUser_ReturnsNotFound()
        {
            var result = _controller.Username(Guid.NewGuid());
            Assert.IsType<NotFoundResult>(result);
        }

        // ---------------------- UsernameList ----------------------
        [Fact]
        public void UsernameList_WithIds_ReturnsListOfIdUsernamePairs()
        {
            var user1 = CreateTestUser("user1");
            var user2 = CreateTestUser("user2");
            var ids = new[] { user1.Id, user2.Id };

            var result = _controller.UsernameList(ids);
            var okResult = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsType<List<string[]>>(okResult.Value);
            Assert.Equal(2, list.Count);
            Assert.Contains(list, item => item[0] == user1.Id.ToString() && item[1] == "user1");
            Assert.Contains(list, item => item[0] == user2.Id.ToString() && item[1] == "user2");
        }

        [Fact]
        public void UsernameList_EmptyIds_ReturnsEmptyList()
        {
            var result = _controller.UsernameList(Array.Empty<Guid>());
            var okResult = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsType<List<object>>(okResult.Value);
            Assert.Empty(list);
        }

        [Fact]
        public void UsernameList_NullIds_ReturnsEmptyList()
        {
            var result = _controller.UsernameList(null);
            var okResult = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsType<List<object>>(okResult.Value);
            Assert.Empty(list);
        }

        [Fact]
        public void UsernameList_OnlySomeIdsExist_ReturnsOnlyExisting()
        {
            var user1 = CreateTestUser("user1");
            var nonExisting = Guid.NewGuid();
            var ids = new[] { user1.Id, nonExisting };
            var result = _controller.UsernameList(ids);
            var okResult = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsType<List<string[]>>(okResult.Value);
            Assert.Single(list);
            Assert.Equal(user1.Id.ToString(), list[0][0]);
            Assert.Equal("user1", list[0][1]);
        }

        // ---------------------- ScansFromUser ----------------------
        [Fact]
        public async Task ScansFromUser_ExistingUserWithScans_ReturnsScans()
        {
            var user = CreateTestUser();
            var mountain1 = CreateTestMountain();
            var mountain2 = CreateTestMountain();
            var scan1 = CreateTestScan(1, user.Id, mountain1.Id);
            var scan2 = CreateTestScan(2, user.Id, mountain2.Id);

            var result = await _controller.ScansFromUser(user.Id);
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var scans = Assert.IsType<List<UserController.UserScanDto>>(okResult.Value);
            Assert.Equal(2, scans.Count);
            Assert.Contains(scans, s => s.Id == 1 && s.MountainId == mountain1.Id);
            Assert.Contains(scans, s => s.Id == 2 && s.MountainId == mountain2.Id);
        }

        [Fact]
        public async Task ScansFromUser_ExistingUserNoScans_ReturnsEmptyList()
        {
            var user = CreateTestUser();
            var result = await _controller.ScansFromUser(user.Id);
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var scans = Assert.IsType<List<UserController.UserScanDto>>(okResult.Value);
            Assert.Empty(scans);
        }

        [Fact]
        public async Task ScansFromUser_NonExistingUser_ReturnsNotFound()
        {
            var result = await _controller.ScansFromUser(Guid.NewGuid());
            var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
            Assert.Contains("not found", notFound.Value.ToString());
        }

        // ---------------------- CountScans ----------------------
        [Fact]
        public async Task CountScans_ExistingUser_ReturnsCount()
        {
            var user = CreateTestUser();
            var mountain = CreateTestMountain();
            CreateTestScan(1, user.Id, mountain.Id);
            CreateTestScan(2, user.Id, mountain.Id);

            var result = await _controller.CountScans(user.Id);
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(2, okResult.Value);
        }

        [Fact]
        public async Task CountScans_ExistingUserNoScans_ReturnsZero()
        {
            var user = CreateTestUser();
            var result = await _controller.CountScans(user.Id);
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(0, okResult.Value);
        }

        [Fact]
        public async Task CountScans_NonExistingUser_ReturnsNotFound()
        {
            var result = await _controller.CountScans(Guid.NewGuid());
            var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
            Assert.Contains("does not exist", notFound.Value.ToString());
        }

        // ---------------------- NewScan Tests ----------------------
        [Fact]
        public async Task NewScan_UserNotAuthenticated_ReturnsUnauthorized()
        {
            // Arrange: No authentication set (DefaultHttpContext has no claims)
            var request = new UserController.ScanRequest("some-nfc", 0, 0);

            // Act
            var result = await _controller.NewScan(request);

            // Assert
            Assert.IsType<UnauthorizedResult>(result.Result);
        }

        [Fact]
        public async Task NewScan_InvalidUserIdClaim_ReturnsUnauthorized()
        {
            // Arrange
            var identity = new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "not-a-guid")
            }, "TestAuth");
            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);

            var request = new UserController.ScanRequest("nfc", 0, 0);

            // Act
            var result = await _controller.NewScan(request);

            // Assert
            Assert.IsType<UnauthorizedResult>(result.Result);
        }

        [Fact]
        public async Task NewScan_NfcNotFound_ReturnsNotFound()
        {
            // Arrange
            var userId = Guid.NewGuid(); // Simplified user simulation
            var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);

            var request = new UserController.ScanRequest("nonexistent-nfc", 0, 0);

            // Act
            var result = await _controller.NewScan(request);

            // Assert
            var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
            // Matching the actual string returned by the controller
            Assert.Equal("NFC tag not recognised.", notFound.Value);
        }

        // ---------------------- Helper Assertions ----------------------
        private static string ExtractMessage(object? value)
        {
            if (value == null) return string.Empty;
            if (value is string s) return s;
            // Extract message from object
            var json = JsonSerializer.Serialize(value);
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message",out var message))
            {
                return message.GetString() ?? string.Empty;
            }
            return value.ToString() ?? string.Empty;
        }


        private void AssertBadRequest(IActionResult result, string expectedMessage)
        {
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(expectedMessage, ExtractMessage(badRequest.Value));
        }

        private void AssertUnauthorized(IActionResult result, string expectedMessage)
        {
            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Equal(expectedMessage, ExtractMessage(unauthorized.Value));
        }
    }
}