using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TibiaIntel.Vip;

namespace TibiaIntel.Controllers;

[ApiController]
[Authorize]
[Route("api/tools/vip")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class VipController(VipService vip) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<VipSnapshot> Get(CancellationToken token) => await vip.ReadAsync(UserId, token);

    [HttpPost("characters")]
    public Task<IActionResult> Add(VipNameRequest request, CancellationToken token) =>
        Write(() => vip.AddAsync(UserId, request.Name, token));

    [HttpDelete("characters/{id:long}")]
    public Task<IActionResult> Remove(long id, CancellationToken token) =>
        Write(() => vip.RemoveAsync(UserId, id, token));

    [HttpPost("groups")]
    public Task<IActionResult> CreateGroup(VipNameRequest request, CancellationToken token) =>
        Write(() => vip.SaveGroupAsync(UserId, null, request.Name, token));

    [HttpPut("groups/{id:long}")]
    public Task<IActionResult> RenameGroup(long id, VipNameRequest request, CancellationToken token) =>
        Write(() => vip.SaveGroupAsync(UserId, id, request.Name, token));

    [HttpDelete("groups/{id:long}")]
    public Task<IActionResult> DeleteGroup(long id, CancellationToken token) =>
        Write(() => vip.DeleteGroupAsync(UserId, id, token));

    [HttpPut("characters/{id:long}/groups")]
    public Task<IActionResult> SetGroups(long id, VipGroupsRequest request, CancellationToken token) =>
        Write(() => vip.SetGroupsAsync(UserId, id, request.GroupIds, token));

    private async Task<IActionResult> Write(Func<Task> action)
    {
        try { await action(); return NoContent(); }
        catch (VipException e) { return StatusCode(e.StatusCode, new { message = e.Message }); }
    }
}

public sealed record VipNameRequest([Required, MaxLength(100)] string Name);
public sealed record VipGroupsRequest([Required, MaxLength(10000)] long[] GroupIds);
