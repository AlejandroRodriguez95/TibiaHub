# TibiaHub TODO

## Current State

- Docker Compose runs PostgreSQL locally on `127.0.0.1:5433`.
- EF Core is installed and configured for PostgreSQL.
- ASP.NET Core Identity is wired into the API.
- The initial Identity migration has been created and applied.
- The database has the default Identity tables, including `AspNetUsers` and `AspNetRoles`.
- The solution builds successfully.

## Next Step

Build a small authentication API:

- `POST /auth/register`
- `POST /auth/login`
- `GET /auth/me`

## Learning Focus

- How `UserManager<ApplicationUser>` creates users and hashes passwords.
- How `SignInManager<ApplicationUser>` checks login credentials.
- How authentication identifies the current user.
- How `[Authorize]` protects API endpoints.

## Later

- Decide whether the API should use JWT or cookies for Angular authentication.
- Add Angular login and registration screens.
- Move local database connection string to .NET user secrets.
- Add tests around registration and login behavior.
