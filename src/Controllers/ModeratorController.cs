using Microsoft.AspNetCore.Mvc;
using src.Models;
using System.ComponentModel.DataAnnotations;
using src.AI;

namespace src.Controllers;

[ApiController]
[Route("moderator")]
public class ModeratorController : ControllerBase
{
    private readonly ILogger<BoardController> _logger;
    private readonly GoreDBContext _context;
    private readonly IConfiguration _config;
    private readonly IModelManager _modelManager;

    public ModeratorController(ILogger<BoardController> logger, IConfiguration config, GoreDBContext context, IModelManager modelManager)
    {
        _logger = logger;
        _config = config;
        _modelManager = modelManager;
        _context = context;
    }

    /// <summary>
    /// Submits a manual classification for a message to be used as AI training data.
    /// </summary>
    /// <param name="messageId">The unique ID of the message.</param>
    /// <param name="isSpam">True if the message is spam, false otherwise.</param>
    /// <response code="200">Label submitted successfully.</response>
    /// <response code="404">Message ID not found.</response>
    [HttpPost("train")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitTrainingData([FromQuery] int messageId, [FromQuery] bool isSpam)
    {
        // TODO: Update your _context with the manual label
        // var msg = await _context.Messages.FindAsync(messageId);
        // if (msg == null) return NotFound();

        return Ok(new { Message = $"Message {messageId} marked as {(isSpam ? "Spam" : "Ham")} for training." });
    }

    /// <summary>
    /// Retrieves messages where the AI confidence is low and requires human moderation.
    /// </summary>
    /// <returns>A list of messages with low-confidence scores.</returns>
    /// <response code="200">Returns the list of ambiguous messages.</response>
    [HttpGet("ambiguous-messages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAmbiguousMessages()
    {
        // Placeholder for logic that fetches messages where (0.4 < AI_Score < 0.6)
        var hardMessages = new[]
        {
            new { Id = 101, Content = "Is this a promotional link or just a reference?" },
            new { Id = 102, Content = "Click here to see the results of the study." }
        };

        return Ok(hardMessages);
    }

    /// <summary>
    /// Returns the current performance metrics (F1, Recall, Precision) for the spam filter.
    /// </summary>
    /// <returns>Metrics for both Spam and Non-Spam classifications.</returns>
    /// <response code="200">Returns the classification report.</response>
    [HttpGet("metrics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetModelMetrics()
    {
        var metrics = _modelManager.GetMetrics();        

        return Ok(metrics);
    }
}