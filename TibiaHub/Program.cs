using TibiaHub.External.TibiaData;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDevelopment", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

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

if (app.Environment.IsDevelopment())
{
    app.UseCors("AngularDevelopment");
}

app.MapGet("/", () => "Hello World!");
app.MapControllers();

app.Run();
