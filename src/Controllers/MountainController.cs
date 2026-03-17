using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using src.Models;
using System.Collections.Immutable;

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

    [HttpGet]
    public ActionResult<IEnumerable<Mountain>> GetMountains()
    {
        IEnumerable<PublicMountain> ret = from m in _context.Mountains select new PublicMountain(m.Id, m.Name, m.Height, m.RegionId, m.Lat, m.Lon );
        return  Ok(ret.ToList());
    }
}

record PublicMountain(Guid Id, string Name, int Height, int RegionId, decimal Lat, decimal Lon);