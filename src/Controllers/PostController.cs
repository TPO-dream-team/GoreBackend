using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using src.AI;
using src.Models;
using System.Security.Claims;

namespace src.Controllers;

[Authorize]
[ApiController]
[Route("post")]
public class PostController : ControllerBase
{
    private readonly ILogger<PostController> _logger;
    private readonly IConfiguration _config;
    private GoreDBContext _context;
    private IModelManager _modelManager;

    public PostController(ILogger<PostController> logger, IConfiguration config, GoreDBContext context, IModelManager modelManager)
    {
        _logger = logger;
        _config = config;
        _context = context;
        _modelManager = modelManager;
    }

    /// <summary>
    /// Retrieves a paginated list of all posts that are not flagged as spam.
    /// </summary>
    /// <param name="offset">The number of posts to skip.</param>
    /// <param name="limit">The maximum number of posts to return.</param>
    /// <returns>A list of posts.</returns>
    /// <response code="200">Returns the list of posts.</response>
    /// <response code="400">Invalid pagination parameters.</response>
    /// <response code="500">Internal server error.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<PostListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult AllPosts([FromQuery] int offset = 0, [FromQuery] int limit = 100)
    {
        if (offset < 0 || limit <= 0)
            return BadRequest("Invalid pagination parameters.");
        try
        {
            var posts = _context.Posts
                .Where(p => p.Message.IsSpam != true)
                .OrderByDescending(p => p.Timestamp)
                .Skip(offset)
                .Take(limit)
                .Select(p => new PostListDto
                (
                    p.Id,
                    p.Tagline,
                    p.CreatedByNavigation.Username,
                    p.Mountain != null ? p.Mountain.Name : null,
                    p.PostComments.Count,
                    p.Message.Content,
                    p.Timestamp
                ))
                .ToList();

            return Ok(posts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching posts");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Retrieves a specific post by its unique ID.
    /// </summary>
    /// <param name="id">The post ID.</param>
    /// <returns>The requested post details.</returns>
    /// <response code="200">Returns the requested post.</response>
    /// <response code="404">Post not found.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PostListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetPostById(int id)
    {
        try
        {
            var post = _context.Posts
                .Where(p => p.Id == id)
                .Select(p => new PostListDto
                (
                    p.Id,
                    p.Tagline,
                    p.CreatedByNavigation.Username,
                    p.Mountain != null ? p.Mountain.Name : null,
                    p.PostComments.Count,
                    p.Message.Content,
                    p.Timestamp
                ))
                .FirstOrDefault();

            if (post == null)
                return NotFound($"Post with ID {id} not found.");

            return Ok(post);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching post {PostId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Retrieves all comments for a specific post, excluding those flagged as spam.
    /// </summary>
    /// <param name="id">The post ID.</param>
    /// <returns>A list of comments for the post.</returns>
    /// <response code="200">Returns the list of comments.</response>
    /// <response code="404">Post not found.</response>
    [HttpGet("{id:int}/comments")]
    [ProducesResponseType(typeof(IEnumerable<CommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult AllComments(int id)
    {
        try
        {
            var postExists = _context.Posts.Any(p => p.Id == id);
            if (!postExists)
                return NotFound($"Post with ID {id} not found.");

            var comments = _context.PostComments
                .Where(c => c.PostId == id && c.Message.IsSpam != true)
                .OrderBy(c => c.Timestamp)
                .Select(c => new CommentDto
                (
                    c.Id,
                    c.CreatedBy,
                    c.Message.Content,
                    c.CreatedByNavigation.Username,
                    c.Timestamp
                ))
                .ToList();

            return Ok(comments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching comments for post {PostId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Creates a new post. Content is processed by the AI spam filter.
    /// </summary>
    /// <param name="request">Post details including tagline and message.</param>
    /// <returns>The created post response.</returns>
    /// <response code="201">Post created successfully.</response>
    /// <response code="400">Missing fields or message flagged as spam.</response>
    /// <response code="404">Specified mountain not found.</response>
    [Authorize]
    [HttpPost("new")]
    [ProducesResponseType(typeof(PostCreationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult CreatePost([FromBody] CreatePostRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Tagline) || string.IsNullOrWhiteSpace(request.StartMsg))
            return BadRequest("Tagline and message are required.");

        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized("Invalid user identity in token.");

            if (request.MountainId != null && !_context.Mountains.Any(m => m.Id == request.MountainId))
                return NotFound("The specified mountain was not found.");

            bool? isSpam = null;
            ModelOutput output = _modelManager.Predict(request.StartMsg);
            double confidence = (double)(output.ConfidencePercentage);

            if (_config.GetValue<bool>("useSpamFilter", false))
            {
                isSpam = output.IsSpam;
            }

            var message = new Message
            {
                Content = request.StartMsg,
                IsSpam = isSpam,
                IsSpamConf = confidence,
            };

            _context.Messages.Add(message);
            _context.SaveChanges();

            var newPost = new Post
            {
                CreatedBy = userId,
                Tagline = request.Tagline,
                MessageId = message.Id,
                MountainId = request.MountainId
            };

            _context.Posts.Add(newPost);
            _context.SaveChanges();

            if (output.IsSpam)
            {
                return BadRequest("The comment includes inappropriate context.");
            }

            return CreatedAtAction(nameof(GetPostById), new { id = newPost.Id }, new PostCreationResponse("Summit post created!", newPost.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating post");
            return StatusCode(500, "An error occurred while saving your post.");
        }
    }

    /// <summary>
    /// Adds a comment to an existing post. Content is processed by the AI spam filter.
    /// </summary>
    /// <param name="id">The post ID.</param>
    /// <param name="request">The comment message content.</param>
    /// <response code="201">Comment added successfully.</response>
    /// <response code="400">Empty message or content flagged as spam.</response>
    /// <response code="404">Post not found.</response>
    [Authorize]
    [HttpPost("{id}/comments/")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult AddComment(int id, [FromBody] CreateCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message cannot be empty.");

        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized("User ID not found in token.");

            if (!_context.Posts.Any(p => p.Id == id))
                return NotFound("The post does not exist.");

            bool? isSpam = null;
            var output = _modelManager.Predict(request.Message);
            double confidence = (double)(output.ConfidencePercentage);

            if (_config.GetValue<bool>("useSpamFilter", false))
            {
                isSpam = output.IsSpam;
            }

            var messageEntry = new Message
            {
                Content = request.Message,
                IsSpam = isSpam,
                IsSpamConf = confidence,
            };

            _context.Messages.Add(messageEntry);
            _context.SaveChanges();

            var newComment = new PostComment
            {
                PostId = id,
                CreatedBy = userId,
                MessageId = messageEntry.Id
            };

            _context.PostComments.Add(newComment);
            _context.SaveChanges();

            if (output.IsSpam)
            {
                return BadRequest("The comment includes inappropriate context.");
            }

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
    public record PostListDto(int Id, string Tagline, string Username, string MountainName, int CommentCount, string StartMsg, DateTime TimeStamp);
    public record CommentDto(int Id, Guid CreatedBy, string Message, string Username, DateTime TimeStamp);
    public record PostCreationResponse(string Message, int PostId);
}