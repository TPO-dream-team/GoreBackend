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
    /// Retrieves all active boards that have not expired and are not flagged as spam.
    /// </summary>
    /// <returns>A list of active tour boards.</returns>
    /// <response code="200">Returns the list of boards.</response>
    /// <response code="500">Internal server error.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BoardListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBoards()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var boards = await (from b in _context.Boards
                                join u in _context.Users on b.UserId equals u.Id
                                join m in _context.Messages on b.MessageId equals m.Id
                                where b.ExpiryDate >= today && m.IsSpam != true
                                orderby b.ExpiryDate
                                select new BoardListDto(
                                    b.Id,
                                    b.ExpiryDate,
                                    u.Username,
                                    u.Id,
                                    b.MountainId,
                                    m.Content,
                                    b.TourTime,
                                    b.Difficulty
                                )).ToListAsync();

            return Ok(boards);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active boards.");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Creates a new tour board. Includes automated spam detection on the description.
    /// </summary>
    /// <param name="request">The board details including description and tour date.</param>
    /// <returns>A status indicating success or failure.</returns>
    /// <response code="201">Board created successfully.</response>
    /// <response code="400">Invalid request data or description flagged as spam.</response>
    /// <response code="401">User is not authorized.</response>
    /// <response code="404">Specified mountain not found.</response>
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
                return Unauthorized("User ID not found in token.");

            if (string.IsNullOrWhiteSpace(request.Description) || request.TourTime <= 0 || request.MountainId == Guid.Empty)
                return BadRequest(new { message = "Please fill the whole form." });

            if (request.Difficulty < 1 || request.Difficulty > 5)
                return BadRequest(new { message = "Select appropriate difficulty." });

            if (request.ExpiryDate < DateOnly.FromDateTime(DateTime.Now))
                return BadRequest(new { message = "You chose invalid day of the tour." });

            var mountainExists = await _context.Mountains.AnyAsync(m => m.Id == request.MountainId);
            if (!mountainExists)
                return NotFound(new { message = "Select mountain from the list." });

            bool? isSpam = null;
            ModelOutput output = _modelManager.Predict(request.Description);
            double confidence = (double)(output.ConfidencePercentage);

            if (_config.GetValue<bool>("useSpamFilter", false))
            {
                isSpam = output.IsSpam;
            }

            var newMessage = new Message
            {
                Content = request.Description,
                IsSpam = isSpam,
                IsSpamConf = confidence,
            };

            _context.Messages.Add(newMessage);
            await _context.SaveChangesAsync();


            Board b = new Board()
            {
                ExpiryDate = request.ExpiryDate,
                UserId = userId,
                MountainId = request.MountainId,
                TourTime = request.TourTime,
                Difficulty = request.Difficulty,
                MessageId = newMessage.Id
            };

            _context.Boards.Add(b);
            await _context.SaveChangesAsync();

            if (output.IsSpam)
            {
                return BadRequest("The description includes inappropriate context.");
            }

            return Created();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating board.");
            return StatusCode(500, "An error occurred while creating the board.");
        }
    }

    /// <summary>
    /// Gets specific board details by its unique identifier.
    /// </summary>
    /// <param name="id">The GUID of the board.</param>
    /// <response code="200">Returns the board details.</response>
    /// <response code="404">Board not found.</response>
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
                b.Message.Content,
                b.TourTime,
                b.Difficulty
            ))
            .FirstOrDefaultAsync();

        if (board is null) return NotFound($"Board with ID {id} not found.");
        return Ok(board);
    }

    /// <summary>
    /// Retrieves all chat messages for a specific board, excluding those flagged as spam.
    /// </summary>
    /// <param name="id">The board GUID.</param>
    /// <response code="200">Returns the chat messages.</response>
    /// <response code="404">Board not found.</response>
    [HttpGet("{id:guid}/chats")]
    [ProducesResponseType(typeof(IEnumerable<BoardChatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoardChats(Guid id)
    {
        var boardExists = await _context.Boards.AnyAsync(b => b.Id == id);
        if (!boardExists) return NotFound($"Board with ID {id} not found.");

        var chats = await (from c in _context.BoardChats
                           join p in _context.Users on c.UserId equals p.Id
                           join m in _context.Messages on c.MessageId equals m.Id
                           where c.BoardId == id && m.IsSpam != true
                           orderby c.Timestamp
                           select new BoardChatDto(
                               c.Id,
                               c.BoardId,
                               c.UserId,
                               p.Username,
                               m.Content,
                               c.Timestamp
                           )).ToListAsync();

        return Ok(chats);
    }

    /// <summary>
    /// Posts a new chat message to a board. Content is analyzed by the spam filter.
    /// </summary>
    /// <param name="id">The board GUID.</param>
    /// <param name="request">The chat message content.</param>
    /// <response code="201">Message posted successfully.</response>
    /// <response code="400">Message empty or flagged as spam.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="404">Board not found.</response>
    [Authorize]
    [HttpPost("{id:guid}/chats")]
    public async Task<IActionResult> CreateBoardChat(Guid id, [FromBody] CreateBoardChatRequest request)
    {
        var boardExists = await _context.Boards.AnyAsync(b => b.Id == id);
        if (!boardExists) return NotFound($"Board with ID {id} not found.");

        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdValue, out var userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { message = "Write the comment first." });

        bool? isSpam = null;
        ModelOutput output = _modelManager.Predict(request.Message);
        double confidence = (double)(output.ConfidencePercentage);

        if (_config.GetValue<bool>("useSpamFilter", false))
        {
            isSpam = output.IsSpam;
        }

        var newMessage = new Message
        {
            Content = request.Message.Trim(),
            IsSpam = isSpam,
            IsSpamConf = confidence,
        };

        _context.Messages.Add(newMessage);
        await _context.SaveChangesAsync();

        var chat = new BoardChat
        {
            BoardId = id,
            UserId = userId,
            MessageId = newMessage.Id,
        };

        _context.BoardChats.Add(chat);
        await _context.SaveChangesAsync();

        if (output.IsSpam)
        {
            return BadRequest("The comment includes inappropriate context.");
        }

        return CreatedAtAction(nameof(GetBoardChats), new { id = id }, new BoardChatCreatedResponse(chat.Id, "Message posted."));
    }

    public record BoardDTO(DateOnly ExpiryDate, int Difficulty, int TourTime, string Description, Guid MountainId);
    public record CreateBoardChatRequest(string Message);
    public record BoardChatCreatedResponse(int Id, string Message);
    public record BoardListDto(Guid BoardId, DateOnly ExpiryDate, string Username, Guid UserId, Guid MountainId, string Description, int TourTime, int Difficulty);
    public record BoardDetailDto(Guid Id, DateOnly ExpiryDate, Guid UserId, Guid MountainId, string Description, int TourTime, int Difficulty);
    public record BoardChatDto(int Id, Guid BoardId, Guid UserId, string Username, string Msg, DateTime Timestamp);
}