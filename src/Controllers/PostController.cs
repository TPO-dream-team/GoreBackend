using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public PostController(ILogger<PostController> logger, IConfiguration config, GoreDBContext context)
    {
        _logger = logger;
        _config = config;
        _context = context;
    }


    /// <summary>
    /// Retrieves a paginated list of all posts, ordered by the most recent.
    /// </summary>
    /// <param name="offset">The number of items to skip (default: 0).</param>
    /// <param name="limit">The maximum number of items to return (default: 100).</param>
    /// <returns>A collection of posts with author and mountain details.</returns>
    /// <response code="200">Returns the requested page of posts.</response>
    /// <response code="400">If offset is negative or limit is zero/negative.</response>
    /// <response code="500">If there is an internal server error.</response>
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
                .OrderByDescending(p => p.Timestamp)
                .Skip(offset)
                .Take(limit)
                .Select(p => new PostListDto
                (
                    p.Id,
                    p.Tagline,
                    p.CreatedByNavigation.Username,
                    p.Mountain.Name,
                    p.PostComments.Count,
                    p.StartMsg,
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
    /// Retrieves a single post by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the post.</param>
    /// <returns>Detailed information about the post.</returns>
    /// <response code="200">Returns the post details.</response>
    /// <response code="404">If the post is not found.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PostListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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
                    p.StartMsg,
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
    /// Retrieves all comments associated with a specific post.
    /// </summary>
    /// <param name="id">The unique identifier of the post.</param>
    /// <returns>A list of comments for the specified post.</returns>
    /// <response code="200">Returns the list of comments.</response>
    /// <response code="404">If a post with the specified ID does not exist.</response>
    /// <response code="500">If an unexpected error occurs on the server.</response>
    [HttpGet("{id:int}/comments")]
    [ProducesResponseType(typeof(IEnumerable<CommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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
                .Select(c => new CommentDto
                (
                    c.Id,
                    c.CreatedBy,
                    c.Message,
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
    /// Creates a new mountain summit post for the authenticated user.
    /// </summary>
    /// <param name="request">The post details including tagline, message, and optional mountain ID.</param>
    /// <returns>The ID of the newly created post.</returns>
    /// <remarks>
    /// Requires a valid JWT bearer token. 
    /// If a MountainId is provided, it must exist in the database.
    /// </remarks>
    /// <response code="201">Post created successfully.</response>
    /// <response code="400">If required fields are missing or invalid.</response>
    /// <response code="401">If the user is not authenticated or the token is invalid.</response>
    /// <response code="404">If the specified mountain does not exist.</response>
    /// <response code="500">If a database error occurs.</response>
    [Authorize]
    [HttpPost("new")]
    [ProducesResponseType(typeof(PostCreationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

            var response = new PostCreationResponse
            (
               "Summit post created!",
               newPost.Id
            );

            return CreatedAtAction(nameof(AllPosts), new { id = newPost.Id }, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating post for user {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
            return StatusCode(500, "An error occurred while saving your post.");
        }
    }

    /// <summary>
    /// Adds a new comment to a specific mountain post.
    /// </summary>
    /// <param name="id">The ID of the post being commented on.</param>
    /// <param name="request">The comment content.</param>
    /// <returns>A 201 Created status if successful.</returns>
    /// <remarks>
    /// User must be authenticated. The message field is required and cannot be empty.
    /// </remarks>
    /// <response code="201">Comment successfully added.</response>
    /// <response code="400">If the message is null or empty.</response>
    /// <response code="401">If the user is not authenticated.</response>
    /// <response code="404">If the post ID provided does not exist.</response>
    /// <response code="500">If there was a server-side error saving the comment.</response>
    [Authorize]
    [HttpPost("{id}/comments/")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult AddComment(int id, [FromBody] CreateCommentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message cannot be empty.");

        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
                return Unauthorized("User ID not found in token.");

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

    public record PostListDto(int Id, string Tagline, string Username, string MountainName, int CommentCount, string StartMsg, DateTime TimeStamp);

    public record CommentDto(int Id, Guid CreatedBy, string Message, string Username, DateTime TimeStamp);

    public record PostCreationResponse(string Message, int PostId);
}