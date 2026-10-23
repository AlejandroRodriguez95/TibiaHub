using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TibiaIntel.Identity;

namespace TibiaIntel.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/admin")]
public sealed class AdminController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<AdminUserResponse>>> Users(CancellationToken cancellationToken)
    {
        return Ok(await userManager.Users.AsNoTracking()
            .OrderBy(user => user.Email).ThenBy(user => user.Id)
            .Select(user => new AdminUserResponse(user.Id, user.Email))
            .ToListAsync(cancellationToken));
    }
}

// Explicit projection keeps Identity's internal and sensitive fields out of the API.
public sealed record AdminUserResponse(string Id, string? Email);
