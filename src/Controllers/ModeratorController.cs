using Microsoft.AspNetCore.Mvc;
using src.Models;
using System.ComponentModel.DataAnnotations;
using src.AI;
using Microsoft.EntityFrameworkCore;

namespace src.Controllers;

[ApiController]
[Route("moderator")]
public class ModeratorController : ControllerBase
{
    private readonly ILogger<BoardController> _logger;
    private readonly GoreDBContext _context;
    private readonly IModelManager _modelManager;

    public ModeratorController(ILogger<BoardController> logger, IConfiguration config, GoreDBContext context, IModelManager modelManager)
    {
        _logger = logger;
        _modelManager = modelManager;
        _context = context;
    }

    /// <summary>
    /// Manually labels a message, sets confidence to 1.0, and adds it to the AI retraining buffer.
    /// </summary>
    /// <param name="messageId">The database ID of the message to label.</param>
    /// <param name="isSpam">The manual classification (true for Spam, false for Ham).</param>
    /// <response code="200">Message updated and sent to the model retraining pipeline.</response>
    /// <response code="404">Message not found in the database.</response>
    [HttpPost("train")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitTrainingData([FromQuery] int messageId, [FromQuery] bool isSpam)
    {
        var msg = await _context.Messages.FindAsync(messageId);

        if (msg == null)
        {
            return NotFound(new { Message = $"Message with ID {messageId} not found." });
        }

        msg.IsSpam = isSpam;
        msg.IsSpamConf = 1.0;

        await _context.SaveChangesAsync();

        var trainingItem = new ModelInput
        {
            Message = msg.Content,
            IsSpam = isSpam
        };

        string refitStatus = _modelManager.Refit(new List<ModelInput> { trainingItem });

        return Ok(new
        {
            Message = $"Message {messageId} marked as {(isSpam ? "Spam" : "Ham")}.",
            RefitStatus = refitStatus
        });
    }

    /// <summary>
    /// Retrieves the message with the lowest AI confidence score for manual review.
    /// </summary>
    /// <remarks>
    /// Targets messages where the confidence score is furthest from absolute certainty (0 or 1).
    /// </remarks>
    /// <returns>The single most ambiguous message currently in the system.</returns>
    /// <response code="200">Returns the ambiguous message data.</response>
    /// <response code="404">No messages with active filter scores found.</response>
    [HttpGet("ambiguous")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAmbiguousMessage()
    {
        try
        {
            AmbiguousMessageDto? ambiguousMessage = await _context.Messages
                .Where(m => (double)m.IsSpamConf < 0.95)
                .OrderBy(m => (double)m.IsSpamConf)
                .Select(m => new AmbiguousMessageDto(
                    m.Id,
                    m.Content,
                    m.IsSpamConf,
                    m.IsSpam
                ))
                .FirstOrDefaultAsync();

            if (ambiguousMessage == null)
                return NotFound("No messages found with active spam filter scores.");

            return Ok(ambiguousMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching the most ambiguous message.");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Retrieves current AI model performance statistics.
    /// </summary>
    /// <returns>A report containing Accuracy, F1-Score, and other relevant metrics.</returns>
    /// <response code="200">Returns the model performance report.</response>
    [HttpGet("metrics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetModelMetrics()
    {
        var metrics = _modelManager.GetMetrics();

        return Ok(metrics);
    }

    public record AmbiguousMessageDto(int Id, string Content, double Confidence, bool? IsSpam);
}