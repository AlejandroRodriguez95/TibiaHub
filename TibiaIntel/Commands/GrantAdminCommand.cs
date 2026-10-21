using Microsoft.AspNetCore.Identity;
using TibiaIntel.Identity;

namespace TibiaIntel.Commands;

public sealed class GrantAdminCommand(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles)
{
    public async Task ExecuteAsync(string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
            throw new ArgumentException("Usage: --grant-admin <existing-user-email>");

        var user = await users.FindByEmailAsync(args[1])
            ?? throw new InvalidOperationException("Register this account before granting admin access.");

        if (!await roles.RoleExistsAsync(ApplicationRoles.Admin))
            EnsureSuccess(await roles.CreateAsync(new IdentityRole(ApplicationRoles.Admin)));

        EnsureSuccess(await users.AddToRoleAsync(user, ApplicationRoles.Admin));
        Console.WriteLine("Admin access granted. Log out and log in again to refresh permissions.");
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
