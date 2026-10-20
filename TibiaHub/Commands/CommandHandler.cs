namespace TibiaHub.Commands;

public static class CommandHandler
{
    // True means a command ran and the web server should not start.
    public static async Task<bool> HandleAsync(IServiceProvider services, string[] args)
    {
        if (args.Length == 0)
            return false;

        switch (args[0])
        {
            case "--grant-admin":
                using (var scope = services.CreateScope())
                {
                    await scope.ServiceProvider.GetRequiredService<GrantAdminCommand>().ExecuteAsync(args);
                }
                return true;
            default:
                // Other arguments may be standard ASP.NET Core configuration options.
                return false;
        }
    }
}
