using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TibiaHub.Identity;
using TibiaHub.Routes;

namespace TibiaHub.Controllers;

[ApiController]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route(ApiRoutes.Account)]
public sealed class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : ControllerBase
{
    [HttpGet(ApiRoutes.AccountRoutes.Csrf)]
    public IResult Csrf(IAntiforgery antiforgery) =>
        Results.Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [Authorize]
    [HttpGet(ApiRoutes.AccountRoutes.MyAccount)]
    public async Task<IResult> MyAccount()
    {
        var user = await userManager.GetUserAsync(User);
        return user is null ? Results.Unauthorized() : Results.Ok(new { user.Id, user.Email });
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
            ? Results.Ok(new { user.Id, user.Email })
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
