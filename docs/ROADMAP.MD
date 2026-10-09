# TibiaHub Roadmap

## Project Goal

TibiaHub is a learning and portfolio project for building a Tibia fansite with ASP.NET Core and Angular.

The long-term idea is to provide different Tibia-related features through a web frontend, while also exposing reusable API functionality that could later be consumed by Discord bots, TeamSpeak 3 bots, or other integrations.

## Initial Architecture Direction

- Frontend: Angular, kept in the same repository but separated from backend projects.
- Backend server: ASP.NET Core.
- API: ASP.NET Core, separated as its own project inside the same solution.
- Authentication: ASP.NET Core Identity with JWT-based API authentication.
- Database: PostgreSQL running through Docker for local development.
- Project style: learning-focused, but structured cleanly enough to serve as a portfolio project.

## Architecture Notes

The API should be treated as the reusable application surface. The web backend and future external integrations should depend on API contracts instead of duplicating business logic.

Separating the API from the backend server is useful because:

- Discord and TS3 bots can consume the same API later.
- Authentication and authorization rules can be centralized.
- Features can be added without tying them directly to the web frontend.
- The project can demonstrate a more realistic multi-client architecture.

## Proposed Milestones

### 1. Repository And Solution Structure

- Keep the frontend and backend in the same repository.
- Use separate projects inside the same solution instead of separate repositories or separately deployed services for now.
- Split the current single ASP.NET Core project into clearer projects when ready.
- Target a structure similar to:
  - `TibiaHub.Api`
  - `TibiaHub.Application`
  - `TibiaHub.Domain`
  - `TibiaHub.Infrastructure`
  - Angular client project under `client`, `frontend`, or similar
  - `TibiaHub.Tests`

### 2. Backend Foundation

- Set up ASP.NET Core API conventions.
- Add configuration structure for development and production.
- Add dependency injection organization.
- Add logging.
- Use PostgreSQL as the database provider.
- Add Docker Compose for local PostgreSQL.
- Add Entity Framework Core when persistence is needed.

### 3. Authentication And Authorization

- Use ASP.NET Core Identity for user management.
- Implement user registration and login.
- Add JWT issuing and validation for API requests.
- Add refresh-token strategy if needed.
- Define basic roles or claims.
- Protect selected API endpoints.

### 4. Angular Frontend Foundation

- Create the Angular app.
- Set up routing.
- Add HTTP client services for the API.
- Add login and registration screens.
- Store and attach JWT access tokens.
- Add route guards for authenticated pages.

### 5. First Tibia Features

Build character lookup as the first small end-to-end feature.

Initial data to display:

- Character name.
- Level.
- Vocation.
- World.
- Guild.
- Residence or other basic profile fields if available from the external API.

Later feature ideas:

- Guild lookup.
- World information.
- Boss tracking.
- Hunting spot notes.
- Imbuement helper.
- Charm or bestiary tracker.
- Account dashboard for saved characters.

The first feature should stay small enough to finish end-to-end across API, backend, frontend, and tests.

### 6. External Integrations

- Design API endpoints so bots can consume them without frontend-specific assumptions.
- Consider API keys or bot-specific JWT clients later.
- Keep Discord and TS3 bot integrations as future consumers, not part of the initial core.
- Use an external Tibia API as the upstream data source once the specific API is selected.
- Route frontend requests through `TibiaHub.Api` instead of calling the external Tibia API directly from Angular.

### 7. Testing And Portfolio Polish

- Add backend unit tests for business logic.
- Add API integration tests for important endpoints.
- Add frontend tests once Angular structure is stable.
- Add documentation for setup, running locally, and project decisions.
- Keep commits and documentation clean enough to show the learning process.

## Current Decisions

- API and backend code will be separate projects inside the same solution.
- The frontend will live in the same repository as a separate Angular app.
- PostgreSQL will be used as the database.
- Docker will be used for local PostgreSQL setup.
- Authentication will use ASP.NET Core Identity plus JWT.
- Character lookup will be the first feature.
- Character data will come from an external Tibia API, selected later.

## Open Decisions

- Exact solution and folder naming.
- Exact external Tibia API provider.
- Whether refresh tokens are needed in the first auth version.
- Whether to add Docker Compose immediately or when persistence is first implemented.

## Current Input Needed

Before implementation starts, the most useful decisions are:

- Pick final names for the backend projects and Angular folder.
- Find the external Tibia API link.
- Decide whether the first implementation should focus on project structure first or character lookup first.
