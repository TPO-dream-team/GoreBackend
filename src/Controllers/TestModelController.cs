#if DEBUG || STAGING
using Microsoft.AspNetCore.Mvc;
using src.AI;
using System.Diagnostics.CodeAnalysis;

namespace YourApp.Controllers;

[ExcludeFromCodeCoverage]
[ApiController]
[Route("api/test/model")]
public class TestModelController : ControllerBase
{
    private readonly IModelManager _modelManager;

    public TestModelController(IModelManager modelManager)
    {
        _modelManager = modelManager;
    }

    [HttpGet("predict")]
    public ActionResult<ModelOutput> Predict([FromQuery] string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return BadRequest("Message parameter is required.");
        }

        var prediction = _modelManager.Predict(message);
        return Ok(prediction);
    }

    [HttpPost("reset-and-train")]
    public IActionResult ResetModel([FromBody] List<ModelInput> seedData)
    {
        var status = _modelManager.Refit(seedData);
        return Ok(new { Message = status });
    }
    [HttpGet("metrics")]
    public ActionResult<ModelMetricsSnapshot> GetMetrics()
    {
        var metrics = _modelManager.GetMetrics();
        return Ok(metrics);
    }
}
#endif