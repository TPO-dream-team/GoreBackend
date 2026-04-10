#if DEBUG || STAGING
using Microsoft.AspNetCore.Mvc;
using src.AI;

namespace YourApp.Controllers;

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
        _modelManager.Refit(seedData);
        return Ok("Test model reset successfully.");
    }
}
#endif