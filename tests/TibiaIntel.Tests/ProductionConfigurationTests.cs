using Microsoft.Extensions.Configuration;
using TibiaIntel.DataRefresh;

namespace TibiaIntel.Tests;

public sealed class ProductionConfigurationTests
{
    [Fact]
    public void SharedRefreshConfigurationPreservesProductionStoragePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TibiaIntel.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(directory.FullName, "TibiaIntel"))
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Production.json")
            .AddJsonFile("DataRefresh/refresh-sources.json")
            .Build();

        Assert.Equal("/var/lib/tibiaintel/refresh", configuration["DataRefresh:CacheDirectory"]);
        Assert.Equal("/var/lib/tibiaintel/keys", configuration["DataProtection:KeysDirectory"]);
        Assert.Equal("App_Data/refresh", new RefreshOptions().CacheDirectory);
    }
}
