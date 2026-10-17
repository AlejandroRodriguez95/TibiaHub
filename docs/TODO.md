# TibiaHub TODO

## Current State

- PostgreSQL runs locally through Docker Compose on `127.0.0.1:5433`.
- EF Core and ASP.NET Core Identity are configured; the initial migration is applied.
- Registration, cookie login, authenticated current-user lookup, and logout are implemented.
- Angular includes login and registration forms, restores the session through `/account/me`, and supports logout.
- Account writes require an antiforgery token; development CORS allows credentials from `http://localhost:4200`.
- Integration tests cover registration, login, session isolation, logout, and CSRF validation.

## Authentication Decision

Use Identity cookies for the Angular browser frontend. JWT issuance and refresh tokens are outside the current scope. Future bots can use a separate authentication mechanism if needed; public character lookup does not require login.

See [AUTHENTICATION.md](AUTHENTICATION.md) for the request flow and security settings.

## Next Steps

- Manually exercise registration, login, page refresh, and logout in Angular.
- Add protected frontend routes when a feature needs them.
- Move local database credentials to .NET user secrets.
- Add email confirmation and password recovery before public account registration.
- Configure deployment origins, HTTPS, and persistent Data Protection keys before hosting.

## Learning Focus

- How `UserManager<ApplicationUser>` creates users and hashes passwords.
- How `SignInManager<ApplicationUser>` issues and clears authentication cookies.
- How authentication populates `User` and `[Authorize]` protects endpoints.
- How antiforgery tokens protect cookie-authenticated writes.
