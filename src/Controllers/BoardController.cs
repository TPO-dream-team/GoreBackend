using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using src.AI;
using src.Models;
using System.Security.Claims;

namespace src.Controllers;

[ApiController]
[Route("boards")]
public class BoardController : ControllerBase
{
    private readonly ILogger<BoardController> _logger;
    private readonly GoreDBContext _context;
    private readonly IConfiguration _config;
    private IModelManager _modelManager;
    public BoardController(ILogger<BoardController> logger, IConfiguration config, GoreDBContext context, IModelManager modelManager)
    {
        _logger = logger;
        _config = config;
        _context = context;
        _modelManager = modelManager;
    }

    /// <summary>
    /// Retrieves all active boards that have not yet expired.
    /// </summary>
    /// <returns>A list of active boards ordered by their expiry date.</returns>
    /// <response code="200">Returns the list of active boards.</response>
    /// <response code="500">If there is a database connection error.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BoardListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBoards()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var boards = from b in _context.Boards
                            join u in _context.Users on b.UserId equals u.Id
                            where b.ExpiryDate >= today
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
                            );

            return Ok(boards.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active boards.");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Creates a new active board posting for a mountain trip.
    /// </summary>
    /// <param name="request">The board details including expiry date, mountain, and trip info.</param>
    /// <returns>A 201 Created status if successful.</returns>
    /// <remarks>
    /// **Requirements:**
    /// * User must be authenticated via JWT.
    /// * The MountainId must reference an existing mountain in the database.
    /// </remarks>
    /// <response code="201">The board was successfully created.</response>
    /// <response code="400">If the request data is invalid or a database error occurs.</response>
    /// <response code="401">If the user is not authenticated or the ID is missing from the token.</response>
    /// <response code="404">If the specified MountainId does not exist.</response>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MakeBoard([FromBody] BoardDTO request)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized("User ID not found in token.");
            }

            if (string.IsNullOrWhiteSpace(request.Description) || request.TourTime <= 0 || request.MountainId == Guid.Empty)
            {
                return BadRequest(new { message = "Please fill the whole form." });
            }

            if (request.Difficulty < 1 || request.Difficulty > 5)
            {
                return BadRequest(new { message = "Select appropriate difficulty." });
            }

            if (request.ExpiryDate < DateOnly.FromDateTime(DateTime.Now))
            {
                return BadRequest(new { message = "You chose invalid day of the tour." });
            }
           
            var mountainExists = await _context.Mountains.AnyAsync(m => m.Id == request.MountainId);
            if (!mountainExists)
            {
                return NotFound(new { message = "Select mountain from the list." });
            }

            if (_config.GetValue<bool>("useSpamFilter", false))
            {
                var output = _modelManager.Predict(request.Description);
                if (output.IsSpam)
                {
                    return BadRequest("The description you wrote includes inappropriate context.");
                }
            }

            Board b = new Board()
            {
                ExpiryDate = request.ExpiryDate,
                UserId = userId,
                MountainId = request.MountainId,
                TourTime = request.TourTime,
                Difficulty = request.Difficulty,
                Description = request.Description
            };

            _context.Boards.Add(b);
            await _context.SaveChangesAsync();

            return Created();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating board for user {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
            return BadRequest("An error occurred while creating the board.");
        }
    }


    /// <summary>
    /// Retrieves the details of a specific board by its unique identifier.
    /// </summary>
    /// <param name="id">The GUID of the board to retrieve.</param>
    /// <returns>The requested board details.</returns>
    /// <response code="200">Returns the board details.</response>
    /// <response code="404">If no board was found with the provided ID.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BoardDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoardById(Guid id)
    {
        var board = await _context.Boards
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BoardDetailDto(
                b.Id,
                b.ExpiryDate,
                b.UserId,
                b.MountainId,
                b.Description,
                b.TourTime,
                b.Difficulty
            ))
            .FirstOrDefaultAsync();

        if (board is null)
            return NotFound($"Board with ID {id} not found.");

        return Ok(board);
    }

    /// <summary>
    /// Retrieves the full chat history for a specific board.
    /// </summary>
    /// <param name="id">The GUID of the board whose chats are being requested.</param>
    /// <returns>A list of chat messages ordered chronologically.</returns>
    /// <response code="200">Returns the collection of chat messages.</response>
    /// <response code="404">If the specified board does not exist.</response>
    [HttpGet("{id:guid}/chats")]
    [ProducesResponseType(typeof(IEnumerable<BoardChatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoardChats(Guid id)
    {
        var boardExists = await _context.Boards
            .AsNoTracking()
            .AnyAsync(b => b.Id == id);

        if (!boardExists)
            return NotFound($"Board with ID {id} not found.");

        var chats = await (from c in _context.BoardChats
                           join p in _context.Users on c.UserId equals p.Id
                           where c.BoardId == id
                           orderby c.Timestamp
                           select new BoardChatDto(
                               c.Id,
                               c.BoardId,
                               c.UserId,
                               p.Username,
                               c.Msg,
                               c.Timestamp
                           )).ToListAsync();

        return Ok(chats);
    }

    /// <summary>
    /// Posts a new message to a board's chat thread.
    /// </summary>
    /// <param name="id">The GUID of the board to post the message to.</param>
    /// <param name="request">The chat message content.</param>
    /// <returns>A 201 Created response with the new message ID.</returns>
    /// <remarks>
    /// Requires an authenticated user. The message will be trimmed of leading/trailing whitespace.
    /// </remarks>
    /// <response code="201">Message posted successfully.</response>
    /// <response code="400">If the message is empty or whitespace.</response>
    /// <response code="401">If the user is not authenticated or the ID claim is missing.</response>
    /// <response code="404">If the specified board does not exist.</response>
    [Authorize]
    [HttpPost("{id:guid}/chats")]
    [ProducesResponseType(typeof(BoardChatCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateBoardChat(Guid id, [FromBody] CreateBoardChatRequest request)
    {
        var boardExists = await _context.Boards.AnyAsync(b => b.Id == id);
        if (!boardExists)
            return NotFound($"Board with ID {id} not found.");

        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest( new { message = "Write the comment first." });

        if (_config.GetValue<bool>("useSpamFilter", false))
        {
            var output = _modelManager.Predict(request.Message);
            if (output.IsSpam)
            {
                return BadRequest("The comment you wrote includes inappropriate context.");
            }
        }

        var chat = new BoardChat
        {
            BoardId = id,
            UserId = userId,
            Msg = request.Message.Trim(),
        };

        _context.BoardChats.Add(chat);
        await _context.SaveChangesAsync();

        var response = new BoardChatCreatedResponse(chat.Id, "Message posted.");

        return CreatedAtAction(nameof(GetBoardChats), new { id = id }, response);
    }

    public record BoardDTO(DateOnly ExpiryDate, int Difficulty, int TourTime, string Description, Guid MountainId);
    public record CreateBoardChatRequest(string Message);
    public record BoardChatCreatedResponse(int Id, string Message);
    public record BoardListDto(Guid BoardId, DateOnly ExpiryDate, string Username, Guid UserId, Guid MountainId, string Description, int TourTime, int Difficulty);
    public record BoardDetailDto(Guid Id, DateOnly ExpiryDate, Guid UserId, Guid MountainId, string Description, int TourTime, int Difficulty);
    public record BoardChatDto(int Id, Guid BoardId, Guid UserId, string Username, string Msg, DateTime Timestamp);
}

