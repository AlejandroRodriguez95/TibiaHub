using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TibiaHub.CharacterScanning;
using TibiaHub.Data;

namespace TibiaHub.Controllers;

[ApiController]
[Authorize]
[Route("tools/character-scan")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CharacterScanController(ApplicationDbContext db, CharacterScanService scans, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Scan([FromQuery, Required, MaxLength(100)] string name, CancellationToken token)
    {
        var normalized = SessionRecorder.Normalize(name);
        var cutoff = clock.GetUtcNow().AddDays(-14);
        // Character names are globally unique in-game. Following a transfer, use
        // the world where the character was most recently observed starting a session.
        var world = await db.OnlineSessions.Where(s => s.Character.NormalizedName == normalized
                && (s.LogoutAt == null || s.LogoutAt >= cutoff))
            .OrderByDescending(s => s.LoginAt).Select(s => s.Character.World).FirstOrDefaultAsync(token);
        if (world is null) return NotFound(new { message = "No retained online history was found for this character. Monitoring must observe them online before a scan is available." });
        return Ok(await scans.ScanAsync(world, name, token));
    }
}
