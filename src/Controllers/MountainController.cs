using Microsoft.AspNetCore.Mvc;
using src.Models;

namespace src.Controllers;

[ApiController]
[Route("mountain")]
public class MountainController : ControllerBase
{
    private readonly ILogger<UserController> _logger;
    private readonly IConfiguration _config;
    private GoreDBContext _context;

    public MountainController(ILogger<UserController> logger, IConfiguration config, GoreDBContext context) {
        _logger = logger;
        _config = config;
        _context = context;
    }

    /// <summary>
    /// Retrieves a collection of all mountains available in the data store.
    /// </summary>
    /// <returns>An Action containing a list of <see cref="Mountain"/> objects representing all mountains.
    /// The list will be empty if no mountains are found.</returns>
    /// <response code="200">Returns the list of mountains.</response>
    /// <response code="404">If the database contains no mountain records.</response>
    [ProducesResponseType(typeof(IEnumerable<MountainDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet]
    public ActionResult<IEnumerable<MountainDto>> GetMountains()
    {
        var mountains = _context.Mountains.ToList();
        if (!mountains.Any())
        {
            return NotFound();
        }

        var ret = mountains.Select(m => new MountainDto(m.Id, m.Name, m.Height, m.RegionId, m.Lat, m.Lon));

        return Ok(ret.ToList());
    }
}

public record MountainDto(Guid Id, string Name, int Height, int RegionId, decimal Lat, decimal Lon);