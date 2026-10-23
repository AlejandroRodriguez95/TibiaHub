# ASP.NET Core Program.cs And Services Study Notes

This file is a study map for the main things that appear in `Program.cs`, especially around `builder.Services`.

`builder.Services` is the dependency injection container setup area. Most calls here register services that controllers, middleware, Identity, EF Core, HTTP clients, and other app code can use later.

## Current TibiaIntel Setup

### `builder.Services.AddDbContext<ApplicationDbContext>()`

Registers the EF Core database context.

In this project it configures `ApplicationDbContext` to use PostgreSQL through Npgsql:

```csharp
options.UseNpgsql(connectionString);
```

This is what lets services like Identity and your own repositories/use cases access the database through EF Core.

### `builder.Configuration.GetConnectionString("DefaultConnection")`

Reads a connection string from configuration.

Typical sources include:

- `appsettings.json`
- `appsettings.Development.json`
- environment variables
- user secrets
- command-line args

### `AddIdentityCore<ApplicationUser>()`

Registers ASP.NET Core Identity's core user-management services.

This includes things like:

- user validation
- password hashing
- password validation
- `UserManager<ApplicationUser>`
- token-related services when configured

In this project, the app uses `ApplicationUser` as the user entity.

### `options.User.RequireUniqueEmail = true`

Configures Identity so users must have unique email addresses.

This is an Identity behavior rule, not a controller rule.

### `.AddEntityFrameworkStores<ApplicationDbContext>()`

Connects Identity to EF Core storage.

This tells Identity to store users, roles, claims, logins, and tokens using `ApplicationDbContext`.

Conceptually:

```text
UserManager<ApplicationUser>
  -> IUserStore<ApplicationUser>
  -> EF Core user store
  -> ApplicationDbContext
  -> PostgreSQL
```

### `.AddSignInManager()`

Registers `SignInManager<ApplicationUser>`.

This handles sign-in workflows such as password sign-in, external sign-in, checking lockout status, and creating authentication cookies.

### `.AddDefaultTokenProviders()`

Registers default token providers for Identity.

These are used for flows like:

- email confirmation
- password reset
- changing email
- two-factor authentication tokens

### `AddAuthentication(IdentityConstants.ApplicationScheme)`

Registers authentication services and sets the default authentication scheme.

In this app, the default scheme is the Identity application cookie scheme.

Authentication answers: "Who is this user?"

### `.AddIdentityCookies()`

Registers the cookie handlers used by ASP.NET Core Identity.

These cookies are used to remember that a user is signed in across HTTP requests.

### `AddAuthorization()`

Registers authorization services.

Authorization answers: "Is this authenticated user allowed to do this?"

Used with attributes like:

```csharp
[Authorize]
[Authorize(Roles = "Admin")]
```

### `AddCors()`

Registers Cross-Origin Resource Sharing policies.

This project has an `AngularDevelopment` policy that allows requests from:

```text
http://localhost:4200
```

This is useful because the Angular dev server and ASP.NET API run on different origins during development.

### `Configure<TibiaDataOptions>()`

Registers strongly typed options from configuration.

This maps configuration values into a class:

```csharp
builder.Configuration.GetSection(TibiaDataOptions.SectionName)
```

Then other services can inject:

```csharp
IOptions<TibiaDataOptions>
```

### `AddHttpClient<TibiaDataClient>()`

Registers a typed HTTP client.

This project uses it for the TibiaData API client.

Typed clients are useful because they centralize:

- base URL
- headers
- timeouts
- retry policies
- service-specific HTTP behavior

### `builder.Build()`

Builds the configured application.

After this point, service registration is mostly finished and the middleware/request pipeline is configured with `app.Use...` and `app.Map...`.

### `app.UseCors()`

Adds CORS middleware to the request pipeline.

In this project it is only enabled during development.

### `app.MapGet("/", ...)`

Maps a minimal API endpoint.

This specific endpoint returns:

```text
Hello World!
```

### `app.UseAuthentication()`

Adds authentication middleware to the request pipeline.

This must run before authorization.

### `app.UseAuthorization()`

Adds authorization middleware to the request pipeline.

This checks access policies for endpoints that require authorization.

## Common `builder.Services` Topics To Study Later

### Dependency Injection Lifetimes

### Custom Services

### Options Pattern

Used to bind configuration to typed classes.

Common variants:

- `IOptions<T>`: basic options.
- `IOptionsSnapshot<T>`: scoped options, useful in web apps.
- `IOptionsMonitor<T>`: supports change notifications.

### Entity Framework Core

Common registrations:

```csharp
AddDbContext<TContext>()
AddDbContextFactory<TContext>()
AddPooledDbContextFactory<TContext>()
```

Most normal web apps start with `AddDbContext`.

### Identity

Main areas:

- users
- roles
- claims
- passwords
- sign-in
- cookies
- tokens
- external login providers

Common services:

```csharp
UserManager<ApplicationUser>
SignInManager<ApplicationUser>
RoleManager<IdentityRole>
```

### Authentication

Authentication identifies the user.

Common schemes:

- cookies
- JWT bearer tokens
- OpenID Connect
- OAuth providers

### Authorization

Authorization controls access.

Common patterns:

- `[Authorize]`
- roles
- claims
- policies
- custom authorization handlers

### CORS

Controls which browser origins may call the API.

Important for frontend apps running on a different port or domain.

Avoid broad production rules like:

```csharp
AllowAnyOrigin()
```

unless the API is intentionally public and does not use credentials.

### HTTP Clients

Use `AddHttpClient` instead of manually creating `HttpClient`.

Common patterns:

- named clients
- typed clients
- generated clients
- retry policies
- timeout configuration

### Logging

ASP.NET Core has built-in logging.

Services can inject:

```csharp
ILogger<MyService>
```

Logging configuration usually lives in `appsettings.json`.

### Configuration

Configuration can come from multiple places:

- JSON files
- environment variables
- user secrets
- command-line arguments
- external providers

Later sources can override earlier ones.

### Validation

Validation can happen through:

- data annotations
- FluentValidation
- manual service validation
- endpoint filters
- model binding validation

Controllers with `[ApiController]` automatically handle some model validation behavior.

### Swagger / OpenAPI

Common for documenting APIs.

Typical registrations:

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
```

Then middleware:

```csharp
app.UseSwagger();
app.UseSwaggerUI();
```

### Middleware

Middleware runs during each HTTP request.

Common examples:

- exception handling
- HTTPS redirection
- static files
- routing
- CORS
- authentication
- authorization

Middleware order matters.

### Exception Handling

Common middleware:

```csharp
app.UseExceptionHandler();
app.UseDeveloperExceptionPage();
```

Development and production usually handle errors differently.

### Health Checks

Used to expose app/database readiness.

Common registration:

```csharp
builder.Services.AddHealthChecks();
```

Then:

```csharp
app.MapHealthChecks("/health");
```

### Background Services

Used for long-running work.

Common registration:

```csharp
builder.Services.AddHostedService<MyWorker>();
```

Useful for scheduled jobs, queue consumers, polling, and cleanup tasks.

### Caching

Common options:

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddStackExchangeRedisCache(...);
```

Use memory cache for simple single-server caching. Use distributed cache when multiple app instances need shared cache state.

### Sessions

Server-side session state can be added with:

```csharp
builder.Services.AddSession();
```

Then:

```csharp
app.UseSession();
```

Many modern APIs avoid session state unless they specifically need it.

### Rate Limiting

ASP.NET Core supports rate limiting middleware.

Useful for protecting login endpoints, public APIs, and expensive operations.

### Response Compression

Compresses HTTP responses.

Common registration:

```csharp
builder.Services.AddResponseCompression();
```

### Static Files

Used when serving files like images, CSS, JavaScript, or built frontend assets.

Middleware:

```csharp
app.UseStaticFiles();
```

### Minimal APIs

Endpoints can be mapped directly in `Program.cs`:

```csharp
app.MapGet("/api/status", () => Results.Ok());
```

Good for small endpoints. Controllers are often better for larger APIs.

### Endpoint Filters

Can run logic before or after minimal API endpoints.

Useful for validation, logging, or request checks in minimal APIs.

### Model Binding

ASP.NET Core can bind route values, query strings, headers, and JSON bodies into C# parameters or models.

Controllers and minimal APIs both use model binding.

### Hosted Environment

Available through:

```csharp
app.Environment
builder.Environment
```

Common values:

- Development
- Staging
- Production

### Secrets

Do not commit secrets.

Common places for secrets:

- environment variables
- .NET user secrets
- deployment secret stores

The `.gitignore` should ignore `.env` files but keep `.env.example`.

## Mental Model

Think of `Program.cs` in two halves:

### Service Registration

Before `builder.Build()`:

```text
What services does my app know how to create?
```

Examples:

- controllers
- DbContext
- Identity
- auth
- options
- HTTP clients
- app services

### Request Pipeline

After `builder.Build()`:

```text
What happens to each HTTP request?
```

Examples:

- CORS
- authentication
- authorization
- routing
- controllers
- endpoints

## Useful Commands

Create a migration:

```powershell
dotnet ef migrations add MigrationName --project TibiaIntel
```

Apply migrations to the database:

```powershell
dotnet ef database update --project TibiaIntel
```

List migrations:

```powershell
dotnet ef migrations list --project TibiaIntel
```

Remove the last migration if it has not been applied:

```powershell
dotnet ef migrations remove --project TibiaIntel
```

Run the app:

```powershell
dotnet run --project TibiaIntel
```

## Current Cookie And CSRF Configuration

The Angular frontend uses Identity cookies. `AddControllersWithViews()` supplies
MVC's antiforgery validation filter services, used by
the global `AutoValidateAntiforgeryTokenAttribute` filter in `Program.cs`; this does not require
adding HTML views. `AddAntiforgery` configures the `X-CSRF-TOKEN` request header.
`GET /account/csrf` returns a token and sets its companion antiforgery cookie.
The filter validates unsafe controller requests before executing their actions, including account POST requests.

`ConfigureApplicationCookie` sets HttpOnly, SameSite=Lax, and secure transport
outside Development, and returns API status codes instead of login redirects.
Development CORS allows credentials only from the explicit Angular origin.
Angular must also send `withCredentials: true` to receive and send cookies.

`[Authorize]` on `/account/myaccount` and `/account/logout` requires an authenticated
identity. `/myaccount` resolves that identity with `UserManager.GetUserAsync(User)`;
logout calls `SignInManager.SignOutAsync()` to clear the browser cookie.

See [AUTHENTICATION.md](AUTHENTICATION.md) for the full flow and deployment notes.
