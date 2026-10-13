using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TibiaHub.Data;
using TibiaHub.External.TibiaData;
using TibiaHub.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

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
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddAuthorization();

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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
