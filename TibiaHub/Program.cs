using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TibiaHub.Commands;
using TibiaHub.Data;
using TibiaHub.External.TibiaData;
using TibiaHub.Identity;
using TibiaHub.DataRefresh;
using TibiaHub.DataRefresh.NpcTrades;
using TibiaHub.DataRefresh.Market;
using TibiaHub.DataRefresh.Worlds;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("DataRefresh/refresh-sources.json", optional: false, reloadOnChange: true);
// Reapply overrides so deployment environment variables and command-line switches win.
builder.Configuration.AddEnvironmentVariables().AddCommandLine(args);
builder.Services.Configure<RefreshOptions>(builder.Configuration.GetSection("DataRefresh"));
builder.Services.Configure<NpcTradeSourceOptions>(builder.Configuration.GetSection("NpcTradeSource"));
builder.Services.Configure<MarketSourceOptions>(builder.Configuration.GetSection("MarketSource"));
builder.Services.Configure<WorldSourceOptions>(builder.Configuration.GetSection("WorldSource"));
builder.Services.AddSingleton<UpstreamRequestPolicy>();
builder.Services.AddHttpClient("world-list", client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TibiaHub/0.1 (world collector)");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<WorldListService>();
builder.Services.AddSingleton<IRefreshJob>(services => services.GetRequiredService<WorldListService>());
builder.Services.AddSingleton<MarketWorldStore>();
builder.Services.AddSingleton<WorldRequestTracker>();
builder.Services.AddHttpClient<MarketSourceClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TibiaHub/0.1 (market collector)");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IRefreshJob, MarketRefreshJob>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(typeof(JsonSnapshotStore<>));
builder.Services.AddHttpClient<NpcTradeSourceClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TibiaHub/0.1 (NPC price collector)");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IRefreshJob, NpcTradeRefreshJob>();
builder.Services.AddHostedService<RefreshCoordinator>();

// The MVC antiforgery validation filter requires the view-feature services.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
// currently only set up for dev mode
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString);
});

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddAuthorization();
builder.Services.AddScoped<GrantAdminCommand>();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDevelopment", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
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

if (await CommandHandler.HandleAsync(app.Services, args))
{
    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("AngularDevelopment");
}

app.MapGet("/", () => "Hello World!");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Expose the entry point for WebApplicationFactory integration tests.
public partial class Program;
