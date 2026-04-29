using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using src.Models;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using static src.Controllers.BoardController;

namespace src.Controllers;

[ApiController]
[Route("user")]
public class UserController : ControllerBase
{
    private readonly ILogger<UserController> _logger;
    private readonly IConfiguration _config;
    private GoreDBContext _context;

    public UserController(ILogger<UserController> logger, IConfiguration config, GoreDBContext context)
    {
        _logger = logger;
        _config = config;
        _context = context;
    }

    /// <summary>
    /// Registers a new user account with a hashed password.
    /// </summary>
    /// <param name="reg">User registration details.</param>
    /// <response code="201">User created successfully.</response>
    /// <response code="400">Username exists or form validation failed.</response>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Register([FromBody] RegisterUser reg)
    {
        if (string.IsNullOrWhiteSpace(reg.Username) || string.IsNullOrWhiteSpace(reg.Password) || string.IsNullOrWhiteSpace(reg.RepeatPassword))
            return BadRequest(new { message = "Please fill out the whole form." });

        if (_context.Users.Any(u => u.Username == reg.Username))
            return BadRequest(new { message = "This username already exists." });

        if (reg.Password != reg.RepeatPassword)
            return BadRequest(new { message = "Passwords do not match." });

        var hash = BCrypt.Net.BCrypt.HashPassword(reg.Password);

        var user = new User
        {
            Username = reg.Username,
            PasswordHash = hash,
            Role = "user"
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        return Created();
    }

    /// <summary>
    /// Authenticates a user and generates a JWT token for session management.
    /// </summary>
    /// <param name="log">Login credentials.</param>
    /// <response code="200">Successful login, returns JWT.</response>
    /// <response code="401">Invalid username or password.</response>
    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Login([FromBody] LoginUser log)
    {
        if (string.IsNullOrWhiteSpace(log.Username) || string.IsNullOrWhiteSpace(log.Password))
            return BadRequest(new { message = "Please fill out the whole form." });

        var user = _context.Users.SingleOrDefault(u => u.Username == log.Username);

        if (user == null || !BCrypt.Net.BCrypt.Verify(log.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid username or password." });

        var token = GenerateJwtToken(user);

        return Ok(new { token });
    }

    /// <summary>
    /// Exchanges a valid (or recently expired) JWT for a new one.
    /// </summary>
    /// <response code="200">Returns new token.</response>
    /// <response code="401">Unauthorized if validation fails.</response>
    [HttpPost("refresh")]
    [Authorize]
    public IActionResult Refresh()
    {
        var authHeader = Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            return BadRequest("Bearer token not found.");

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var jwtSettings = _config.GetSection("Jwt");
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]);

        try
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidateAudience = true,
                ValidAudience = jwtSettings["Audience"],
                ValidateLifetime = false
            };

            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = principal.FindFirst(ClaimTypes.Name)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username))
                return BadRequest("Token missing required claims.");

            var user = new User { Id = Guid.Parse(userId), Username = username, Role = role ?? "user" };
            return Ok(new { token = GenerateJwtToken(user) });
        }
        catch (Exception ex)
        {
            return Unauthorized("Could not refresh token: " + ex.Message);
        }
    }

    private string GenerateJwtToken(User user)
    {
        var jwt = _config.GetSection("Jwt");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: DateTime.Now.AddDays(int.Parse(jwt["ExpiresInDays"])),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Gets the username for a specific user ID.
    /// </summary>
    [HttpGet("{id:guid}/username")]
    public IActionResult Username(Guid id)
    {
        var user = _context.Users.Find(id);
        return user == null ? NotFound() : Ok(user.Username);
    }

    /// <summary>
    /// Returns a list of username/ID pairs for multiple users.
    /// </summary>
    [HttpGet("username")]
    public IActionResult UsernameList([FromQuery] Guid[] id)
    {
        if (id == null || id.Length == 0) return Ok(new List<object>());
        var users = _context.Users
            .Where(u => id.Contains(u.Id))
            .Select(u => new[] { u.Id.ToString(), u.Username })
            .ToList();
        return Ok(users);
    }

    /// <summary>
    /// Retrieves a history of mountain scans for a user.
    /// </summary>
    [HttpGet("{id:guid}/scans")]
    public async Task<ActionResult<IEnumerable<UserScanDto>>> ScansFromUser(Guid id)
    {
        if (!await _context.Users.AnyAsync(u => u.Id == id))
            return NotFound($"User with ID {id} not found.");

        var ret = await _context.Scans
            .AsNoTracking()
            .Where(s => s.UserId == id)
            .Select(s => new UserScanDto(s.Id, s.UserId, s.MountainId, s.Timestamp))
            .ToListAsync();

        return Ok(ret);
    }

    /// <summary>
    /// Gets the total count of unique scans for a specific user.
    /// </summary>
    [HttpGet("{id:guid}/scans/count")]
    public async Task<ActionResult<int>> CountScans(Guid id)
    {
        if (!await _context.Users.AnyAsync(u => u.Id == id))
            return NotFound($"User with ID {id} does not exist.");

        return Ok(await _context.Scans.CountAsync(s => s.UserId == id));
    }

    /// <summary>
    /// Submits a new mountain scan. Validates NFC ID and GPS distance against the mountain location.
    /// </summary>
    /// <response code="200">Scan recorded successfully.</response>
    /// <response code="400">Distance limit exceeded or duplicate scan within 24h.</response>
    [Authorize]
    [HttpPost("/scans")]
    public async Task<ActionResult<ScanResponse>> NewScan([FromBody] ScanRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) return Unauthorized();

        var mountain = await _context.Mountains.FirstOrDefaultAsync(m => m.Nfc == request.NFC);
        if (mountain == null) return NotFound("NFC tag not recognised.");

        double distance = calculateDistance(request.Lat, request.Lon, (double)mountain.Lat, (double)mountain.Lon);
        if (distance > double.Parse(_config["ScanKmLimit"])) return BadRequest("You are not on a known mountain.");

        var last24Hours = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-24), DateTimeKind.Unspecified);
        if (await _context.Scans.AnyAsync(s => s.UserId == userId && s.MountainId == mountain.Id && s.Timestamp >= last24Hours))
            return BadRequest("You have already scanned this mountain.");

        var newScan = new Scan { UserId = userId, MountainId = mountain.Id };
        _context.Scans.Add(newScan);
        await _context.SaveChangesAsync();

        return Ok(new ScanResponse("NFC tag was scanned successfully.", newScan.Id));
    }

    /// <summary>
    /// Retrieves all active tour boards created by the specified user.
    /// </summary>
    [HttpGet("{id:guid}/boards")]
    public async Task<ActionResult<IEnumerable<BoardListDto>>> BoardsFromUser(Guid id)
    {
        if (!await _context.Users.AnyAsync(u => u.Id == id))
            return NotFound($"User with ID {id} not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            var userBoards = await (from b in _context.Boards
                                    join u in _context.Users on b.UserId equals u.Id
                                    where u.Id == id && b.ExpiryDate >= today
                                    orderby b.ExpiryDate
                                    select new BoardListDto(
                                        b.Id,
                                        b.ExpiryDate,
                                        u.Username,
                                        u.Id,
                                        b.MountainId,
                                        b.Message.Content,
                                        b.TourTime,
                                        b.Difficulty
                                    )).ToListAsync();

            return Ok(userBoards);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving boards for user {UserId}.", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Retrieves a complete user profile including detailed scan history and boards.
    /// </summary>
    [HttpGet("{id:guid}/profile")]
    public async Task<ActionResult<UserProfileDto>> GetProfile(Guid id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new { u.Id, u.Username })
            .FirstOrDefaultAsync();

        if (user == null) return NotFound(new { message = "User not found." });

        var scans = await _context.Scans
            .AsNoTracking()
            .Where(s => s.UserId == id)
            .Select(s => new ScansDTO(s.Id, s.UserId, s.MountainId, s.Timestamp, s.Mountain.Name))
            .ToListAsync();

        var boards = await _context.Boards
            .AsNoTracking()
            .Where(b => b.UserId == id)
            .Select(b => new BoardDTO(
                b.Id,
                b.UserId,
                b.MountainId,
                b.Message.Content,
                b.TourTime,
                b.Difficulty,
                b.Mountain.Name))
            .ToListAsync();

        return Ok(new UserProfileDto(new UserInfo(user.Id, user.Username), scans, boards));
    }

    private double calculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * (Math.PI / 180.0);
        double dLon = (lon2 - lon1) * (Math.PI / 180.0);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * (Math.PI / 180.0)) * Math.Cos(lat2 * (Math.PI / 180.0)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public record UserInfo(Guid Id, string Username);
    public record ScansDTO(int Id, Guid UserId, Guid MountainId, DateTime Timestamp, string MountainName);
    public record BoardDTO(Guid Id, Guid UserId, Guid MountainId, string Description, int TourTime, int Difficulty, string MountainName);
    public record UserProfileDto(UserInfo User, IEnumerable<ScansDTO> Scans, IEnumerable<BoardDTO> Boards);
    public record LoginUser(string Username, string Password);
    public record ScanRequest(string NFC, double Lon, double Lat);
    public record RegisterUser(string Username, string Password, string RepeatPassword);
    public record ScanResponse(string Message, int ScanId);
    public record UserScanDto(int Id, Guid UserId, Guid MountainId, DateTime Timestamp);
}