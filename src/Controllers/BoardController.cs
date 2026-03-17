using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using src.Controllers;
using src.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;


namespace BoardAPI.Controllers
{
    [ApiController]
    [Route("boards")]
    public class BoardController : ControllerBase
    {
        private readonly ILogger<BoardController> _logger;
        private readonly GoreDBContext _context;
        private readonly IConfiguration _config;
        public BoardController(ILogger<BoardController> logger, IConfiguration config, GoreDBContext context)
        {
            _logger = logger;
            _config = config;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetBoards()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var boards = from b in _context.Boards
                         join u in _context.Users on b.UserId equals u.Id
                         where b.ExpiryDate >= today
                         orderby b.ExpiryDate
                         select new
                         {
                             BoardId = b.Id,
                             b.ExpiryDate,
                             u.Username,
                             UserId = u.Id,
                             b.MountainId,
                             b.TourTime,
                             b.Difficulty
                         };

            return Ok(boards);
        }

        [HttpPost]
        [Authorize]
        //TODO check if mountan exists
        public async Task<IActionResult> MakeBoard(BoardDTO request)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return Unauthorized("User ID not found in token.");
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
                return BadRequest($"Error creating board: {ex.Message}");
            }
        }


        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetBoardById(Guid id)
        {
            var board = await _context.Boards
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id);

            if (board is null)
                return NotFound();

            return Ok(board);
        }

        [HttpGet("{id:guid}/chats")]
        public async Task<IActionResult> GetBoardChats(Guid id)
        {
            var boardExists = await _context.Boards
                .AsNoTracking()
                .AnyAsync(b => b.Id == id);

            if (!boardExists)
                return NotFound();

            var chats = from c in _context.BoardChats
                        join p in _context.Users on c.UserId equals p.Id
                        where c.BoardId == id
                        orderby c.Timestamp
                        select new { c.Id, c.BoardId, c.UserId, p.Username, c.Msg};

            return Ok(chats.ToList());
        }

        [Authorize]
        [HttpPost("{id:guid}/chats")]
        public async Task<IActionResult> CreateBoardChat(Guid id, [FromBody] CreateBoardChatRequest request)
        {
            var boardExists = await _context.Boards.AnyAsync(b => b.Id == id);
            if (!boardExists)
                return NotFound();

            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!Guid.TryParse(userIdValue, out var userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Message is required.");

            var chat = new BoardChat
            {
                BoardId = id,
                UserId = userId,
                Msg = request.Message.Trim()
            };

            _context.BoardChats.Add(chat);
            await _context.SaveChangesAsync();

            return Created($"/boards/{id}/chats", chat);
        }

        public record BoardDTO(DateOnly ExpiryDate, int Difficulty, int TourTime, string Description, Guid MountainId);
        public record CreateBoardChatRequest(string Message);
    }
}
