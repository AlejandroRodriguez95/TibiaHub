using TibiaHub.External.TibiaData;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.Configure<TibiaDataOptions>(
    builder.Configuration.GetSection(TibiaDataOptions.SectionName));

builder.Services.AddHttpClient<TibiaDataClient>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<TibiaDataOptions>>()
        .Value;

    client.BaseAddress = new Uri(options.BaseUrl);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
});

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapControllers();

app.Run();
