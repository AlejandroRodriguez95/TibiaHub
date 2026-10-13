using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TibiaHub.Identity;
using TibiaHub.Routes;

namespace TibiaHub.Controllers;

[ApiController]
[Route(ApiRoutes.Account)]
public sealed class AccountController(UserManager<ApplicationUser> userManager) : ControllerBase
{
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
