using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using src.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Claims;
using System.Text;
using static src.Controllers.UserController;

namespace src.Controllers;

[Authorize]
[ApiController]
[Route("post")]
public class PostController : ControllerBase
{
    private readonly ILogger<PostController> _logger;
    private readonly IConfiguration _config;
    private GoreDBContext _context;

    public PostController(ILogger<PostController> logger, IConfiguration config, GoreDBContext context)
    {
        _logger = logger;
        _config = config;
        _context = context;
    }


    [HttpGet]
    public IActionResult AllPosts([FromQuery] int offset = 0, [FromQuery] int limit = 100)
    {
   
        if (offset < 0 || limit <= 0)
            return BadRequest("Invalid pagination parameters.");

        try
        {
            var posts = _context.Posts
                .OrderByDescending(p => p.Timestamp)
                .Skip(offset)
                .Take(limit)
                .Select(p => new {
                    p.Id,
                    p.CreatedBy,
                    p.Timestamp,
                    p.Tagline,
                    p.StartMsg,
                    p.MountainId,
                    p.CreatedByNavigation.Username, 
                    p.Mountain.Name,
                    CommentCount = p.PostComments.Count
                })
                .ToList();

            return Ok(posts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching posts");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{id:int}/comments")]
    public IActionResult AllComments(int id)
    {
        try
        {
            var postExists = _context.Posts.Any(p => p.Id == id);
            if (!postExists)
            {
                return NotFound($"Post with ID {id} not found.");
            }

            var comments = _context.PostComments
                .Where(c => c.PostId == id)
                .OrderBy(c => c.Timestamp) 
                .Select(c => new {
                    c.Id,
                    c.CreatedBy,
                    c.Message,
                    c.Timestamp,
                    c.CreatedByNavigation.Username 
                })
                .ToList();

            return Ok(comments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching comments for post {PostId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [Authorize]
    [HttpPost("new")]
    public IActionResult CreatePost([FromBody] CreatePostRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Tagline) || string.IsNullOrWhiteSpace(request.StartMsg))
            return BadRequest("Tagline and message are required.");

        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized("Invalid user identity in token.");

            if (request.MountainId != null)
            {
                var mountainExists = _context.Mountains.Any(m => m.Id == request.MountainId);
                if (!mountainExists)
                    return NotFound("The specified mountain was not found in our database.");
            }

            var newPost = new Post
            {
                CreatedBy = userId,
                Tagline = request.Tagline,
                StartMsg = request.StartMsg,
                MountainId = request.MountainId
            };

            _context.Posts.Add(newPost);
            _context.SaveChanges();

            return CreatedAtAction(nameof(AllPosts), new { id = newPost.Id }, new
            {
                Message = "Summit post created!",
                PostId = newPost.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating post for user {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
            return StatusCode(500, "An error occurred while saving your post.");
        }
    }

    [HttpPost("{id}/comments/new")]
    public IActionResult AddComment(int id, [FromBody] CreateCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message cannot be empty.");

        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString))
                return Unauthorized("User ID not found in token.");

            Guid userId = Guid.Parse(userIdString);

            var postExists = _context.Posts.Any(p => p.Id == id);
            if (!postExists)
                return NotFound("The post you are trying to comment on does not exist.");

            var newComment = new PostComment
            {
                PostId = id,
                CreatedBy = userId,
                Message = request.Message
            };

            _context.PostComments.Add(newComment);
            _context.SaveChanges();

            return Created();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding comment to post {PostId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    public record CreateCommentRequest(string Message);
    public record CreatePostRequest(string Tagline, string StartMsg, Guid? MountainId);
}