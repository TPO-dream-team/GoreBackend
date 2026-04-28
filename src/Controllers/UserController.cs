using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using src.Models;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Xml.Linq;
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
    /// Creates a new user.
    /// </summary>
    /// <param name="reg">User registration data (username, password, repeatpassword).</param>
    /// <response code="201">User Created.</response>
    /// <response code="400">Username already exists or missing elements.</response>
    [HttpPost("register")]
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
    /// Authenticates a user and returns a JWT token.
    /// </summary>
    /// <param name="log">Login data (username and password).</param>
    /// <response code="200">User logged in.</response>
    /// <response code="400">Missing username or password.</response>
    /// <response code="401">Invalid username or password.</response>
    [HttpPost("login")]
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
    /// Creates a new JWT token from old one.
    /// </summary>
    /// <param>Expired JWT token.</param>
    /// <response code="200">New token created.</response>
    /// <response code="400">Invalid token.</response>
    /// <response code="401">Token cannot be refreshed.</response>
    [HttpPost("refresh")]
    [Authorize]
    public IActionResult Refresh()
    {
        var authHeader = Request.Headers["Authorization"].ToString();

        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return BadRequest("Bearer token not found in Authorization header.");
        }

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
                ValidateLifetime = false // Keep false to allow processing expired tokens
            };

            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);

            if (validatedToken is not JwtSecurityToken jwtSecurityToken ||
                !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                return BadRequest("Invalid token format.");
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = principal.FindFirst(ClaimTypes.Name)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username))
            {
                return BadRequest("Token missing required claims.");
            }

            var user = new User
            {
                Id = Guid.Parse(userId),
                Username = username,
                Role = role ?? "user"
            };

            var newToken = GenerateJwtToken(user);

            return Ok(new { token = newToken });
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

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwt["Key"])
        );

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
    /// Returns username of user with id "id".
    /// </summary>
    /// <param name="id">user id</param>
    /// <response code="200">Returns the username.</response>
    /// <response code="404">User with given id doesn't exist.</response>
    [HttpGet("{id:guid}/username")]
    public IActionResult Username(Guid id)
    {
        var user = _context.Users.Find(id);

        if (user == null)
        {
            return NotFound();
        }

        return Ok(user.Username);
    }

    /// <summary>
    /// Returns a list of [id, username] pairs.
    /// </summary>
    /// <param name="id">List of user ids.</param>
    /// <response code="200">Returns the list.</response>
    [HttpGet("username")]
    public IActionResult UsernameList([FromQuery] Guid[] id)
    {
        if (id == null || id.Length == 0)
        {
            return Ok(new List<object>());
        }

        var idList = id.ToList();

        var users = _context.Users
        .Where(u => idList.Contains(u.Id))
        .Select(u => new[] { u.Id.ToString(), u.Username })
        .ToList();

        return Ok(users);
    }

    /// <summary>
    /// Retrieves a history of all successful mountain scans for a specific user.
    /// </summary>
    /// <param name="id">The unique GUID of the user.</param>
    /// <returns>A list of scan records including mountain and user identifiers</returns>
    /// <response code="200">Returns the collection of user scans.</response>
    /// <response code="404">If the user ID does not exist in the system.</response>
    [HttpGet("{id:guid}/scans")]
    [ProducesResponseType(typeof(IEnumerable<UserScanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<UserScanDto>>> ScansFromUser(Guid id)
    {
        // Validate user existence to justify the 404
        var userExists = await _context.Users.AnyAsync(u => u.Id == id);
        if (!userExists)
        {
            return NotFound($"User with ID {id} not found.");
        }

        var ret = await _context.Scans
            .AsNoTracking()
            .Where(s => s.UserId == id)
            .Select(s => new UserScanDto(s.Id, s.UserId, s.MountainId, s.Timestamp))
            .ToListAsync();

        return Ok(ret);
    }

    /// <summary>
    /// Retrieves the total number of mountain summits recorded by a specific user.
    /// </summary>
    /// <param name="id">The unique GUID of the user.</param>
    /// <returns>An integer representing the total scan count.</returns>
    /// <response code="200">Returns the count of successful scans for the user.</response>
    /// <response code="404">If the user ID does not exist in the system.</response>
    [HttpGet("{id:guid}/scans/count")]
    public async Task<ActionResult<int>> CountScans(Guid id)
    {
        var userExists = await _context.Users.AnyAsync(u => u.Id == id);
        if (!userExists)
        {
            return NotFound($"User with ID {id} does not exist.");
        }

        int count = await _context.Scans
            .Where(s => s.UserId == id)
            .CountAsync();

        return Ok(count);
    }


    /// <summary>
    /// Records a new mountain scan via an NFC tag identifier.
    /// </summary>
    /// <param name="request">The request containing the NFC tag string, latitude, and longitude.</param>
    /// <returns>A success message and the generated Scan ID.</returns>
    /// <remarks>
    /// This endpoint performs the following validations:
    /// 1. Verifies the NFC tag exists in the database.
    /// 2. **Location Check:** Validates that the provided coordinates are within range of the mountain's peak.
    /// 3. **Anti-Spam:** Ensures the user hasn't successfully scanned this specific mountain within the last 24 hours.
    /// </remarks>
    /// <response code="200">Scan recorded successfully.</response>
    /// <response code="400">If the location is out of range, a scan exists within 24h, or data is malformed.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="404">If the NFC tag does not match any mountain in the database.</response>
    [Authorize]
    [HttpPost("/scans")]
    [ProducesResponseType(typeof(ScanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScanResponse>> NewScan([FromBody] ScanRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var mountain = await _context.Mountains
            .FirstOrDefaultAsync(m => m.Nfc == request.NFC);
        _logger.LogInformation(request.NFC);
        if (mountain == null)
            return NotFound("NFC tag not recognised.");

        double distance = calculateDistance(request.Lat, request.Lon, (double)mountain.Lat, (double)mountain.Lon);
        if (distance > double.Parse(_config.GetSection("ScanKmLimit").Value))
        {
            _logger.LogInformation(distance.ToString());
            return BadRequest("You are not on a known mountain."); 
        }
        var last24Hours = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-24), DateTimeKind.Unspecified);
        var alreadyScanned = await _context.Scans
            .AnyAsync(s => s.UserId == userId &&
                           s.MountainId == mountain.Id &&
                           s.Timestamp >= last24Hours);

        if (alreadyScanned)
        {
            return BadRequest("You have already scanned this mountain.");
        }

        var newScan = new Scan
        {
            UserId = userId,
            MountainId = mountain.Id
        };

        _context.Scans.Add(newScan);
        await _context.SaveChangesAsync();

        return Ok(new ScanResponse("NFC tag was scanned successfully.", newScan.Id));
    }

    private double calculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0; // Radius of the Earth in kilometers

        // Convert degrees to radians
        double dLat = (lat2 - lat1) * (Math.PI / 180.0);
        double dLon = (lon2 - lon1) * (Math.PI / 180.0);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * (Math.PI / 180.0)) * Math.Cos(lat2 * (Math.PI / 180.0)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c; // Returns distance in kilometers
    }


    /// <summary>
    /// Retrieves all active boards created by a specific user.
    /// </summary>
    /// <param name="id">The unique GUID of the user.</param>
    /// <returns>A list of board records including mountain and user identifiers.</returns>
    /// <response code="200">Returns the collection of user boards.</response>
    /// <response code="404">If the user ID does not exist in the system.</response>
    [HttpGet("{id:guid}/boards")]
    [ProducesResponseType(typeof(IEnumerable<BoardListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<BoardListDto>>> BoardsFromUser(Guid id)
    {
        var userExists = await _context.Users.AnyAsync(u => u.Id == id);
        if (!userExists)
        {
            return NotFound($"User with ID {id} not found.");
        }

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
                                        b.Description,
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
    /// Retrieves full profile information for a specific user.
    /// </summary>
    /// <param name="id">The unique GUID of the user.</param>
    /// <response code="200">Returns the full user profile.</response>
    /// <response code="404">If the user ID does not exist.</response>
    [HttpGet("{id:guid}/profile")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileDto>> GetProfile(Guid id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new { u.Id, u.Username })
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return NotFound(new { message = $"User with ID {id} not found." });
        }

        var scans = await _context.Scans
            .AsNoTracking()
            .Where(s => s.UserId == id)
            .Select(s => new ScansDTO(s.Id, s.UserId, s.MountainId, s.Timestamp, s.Mountain.Name))
            .ToListAsync();

        var boards = await _context.Boards
            .AsNoTracking()
            .Where(b => b.UserId == id)
            .Select(b => new BoardDTO(b.Id, b.UserId, b.MountainId, b.Description, b.TourTime, b.Difficulty, b.Mountain.Name))
            .ToListAsync();

        var profile = new UserProfileDto
        (
            new UserInfo(user.Id, user.Username),
            scans,
            boards
           );
      
        return Ok(profile);
    }

    public record UserInfo(Guid Id, string Username);
    public record ScansDTO(int Id, Guid UserId, Guid MountainId, DateTime Timestamp, string MountainName);
    public record BoardDTO(Guid Id, Guid UserId, Guid MountainId, string Description, int TourTime, int Difficulty, string MountainName);
    public record UserProfileDto(
        UserInfo User,
        IEnumerable<ScansDTO> Scans,
        IEnumerable<BoardDTO> Boards
    );

    public record LoginUser(string Username, string Password);
    public record ScanRequest(string NFC, double Lon, double Lat);
    public record RegisterUser(string Username, string Password, string RepeatPassword);
    public record ScanResponse(string Message, int ScanId);
    public record UserScanDto(int Id, Guid UserId, Guid MountainId, DateTime Timestamp);
}