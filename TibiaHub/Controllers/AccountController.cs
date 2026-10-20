using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using TibiaHub.DataRefresh.Worlds;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TibiaHub.Identity;
using TibiaHub.Routes;

namespace TibiaHub.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route(ApiRoutes.Account)]
public sealed class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    WorldListService worlds) : ControllerBase
{
    [HttpGet(ApiRoutes.AccountRoutes.Csrf)]
    public IResult Csrf(IAntiforgery antiforgery) =>
        Results.Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [Authorize]
    [HttpGet(ApiRoutes.AccountRoutes.MyAccount)]
    public async Task<IResult> MyAccount(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        return user is null ? Results.Unauthorized() : Results.Ok(new
        {
            user.Id, user.Email, IsAdmin = User.IsInRole(ApplicationRoles.Admin),
            DefaultWorld = (await userManager.GetClaimsAsync(user)).FirstOrDefault(c => c.Type == DefaultWorldClaim)?.Value,
            Worlds = await worlds.GetWorldsAsync(token)
        });
    }

    public const string DefaultWorldClaim = "tibiahub:default-world";

    [Authorize]
    [HttpPost("default-world")]
    public async Task<IResult> SetDefaultWorld(DefaultWorldRequest request, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Results.Unauthorized();
        var available = await worlds.GetWorldsAsync(token);
        var world = string.IsNullOrWhiteSpace(request.World) ? null
            : available.FirstOrDefault(w => w.Equals(request.World.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(request.World) && world is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["world"] = ["Choose a world from the available list."] });
        var previous = (await userManager.GetClaimsAsync(user)).FirstOrDefault(c => c.Type == DefaultWorldClaim);
        IdentityResult result;
        if (previous is null)
            result = world is null ? IdentityResult.Success : await userManager.AddClaimAsync(user, new Claim(DefaultWorldClaim, world));
        else
            result = world is null ? await userManager.RemoveClaimAsync(user, previous)
                : await userManager.ReplaceClaimAsync(user, previous, new Claim(DefaultWorldClaim, world));
        return result.Succeeded ? Results.Ok(new { DefaultWorld = world }) : Results.BadRequest(new { errors = result.Errors });
    }

    [Authorize]
    [HttpPost(ApiRoutes.AccountRoutes.Logout)]
    public async Task<IResult> Logout()
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    [HttpPost(ApiRoutes.AccountRoutes.Login)]
    public async Task<IResult> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: false, lockoutOnFailure: true);

        return result.Succeeded
            ? Results.Ok(new { user.Id, user.Email, IsAdmin = await userManager.IsInRoleAsync(user, ApplicationRoles.Admin) })
            : Results.Unauthorized();
    }

    [HttpPost(ApiRoutes.AccountRoutes.Register)]
    public async Task<IResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email
        };

        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return Results.BadRequest(new
            {
                errors = result.Errors.Select(error => new
                {
                    error.Code,
                    error.Description
                })
            });
        }

        return Results.Created(ApiRoutes.AccountRoutes.UserLocation(user.Id), new
        {
            user.Id,
            user.Email
        });
    }
}

public sealed record RegisterRequest(
    [Required]
    [EmailAddress]
    string Email,
    [Required]
    [MinLength(6)]
    string Password);

public sealed record LoginRequest(
    [Required]
    [EmailAddress]
    string Email,
    [Required]
    string Password);

public sealed record DefaultWorldRequest([MaxLength(100)] string? World);
