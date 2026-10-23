# TibiaIntel roadmap

## Goal

Build a Tibia fansite as a learning and portfolio project, with an Angular frontend and reusable ASP.NET Core API functionality that could later support Discord or TeamSpeak integrations.

## Implemented foundation

- One .NET 10 ASP.NET Core backend under `TibiaIntel/`, Angular 22 under `client/tibiaintel-angular/`, and backend tests under `tests/TibiaIntel.Tests/`.
- PostgreSQL 17 through Docker Compose, EF Core migrations, and ASP.NET Core Identity.
- Cookie authentication, CSRF protection, account preferences, and Admin role authorization.
- Petrol & Lantern frontend with landing, character lookup, account, admin, and tool pages.
- Public character lookup using TibiaData v4 through the backend.
- [NPC selling](features/NPC_SELLING.md) with delivery-task highlighting and [world market comparisons](features/MARKET_VALUES.md).
- Independent world/NPC/market jobs with durable JSON snapshots and [shared refresh infrastructure](DATA_REFRESH.md).
- Authenticated [character scans](features/CHARACTER_SCANS.md) using 14 days of observed same-world session timing stored in PostgreSQL.
- Backend unit/integration tests and Angular component tests.

## Next milestones

1. Prepare account and hosting flows for deployment: email confirmation, password recovery, production API URLs, HTTPS, persistent Data Protection keys, SPA route fallback, and durable cache storage.
2. Continue browser verification and improve feature coverage as behavior evolves. Keep setup instructions and feature contracts aligned with implementation.
3. Split the backend into API, Application, Domain, and Infrastructure projects if the growing codebase benefits from those boundaries. These projects do not exist yet.
4. Choose another feature when requirements are concrete. Possibilities include guild/world lookup, saved characters, hunting notes, boss tracking, or imbuement helpers.
5. Add bot integrations through the API when needed, with an authentication mechanism chosen for those clients.

## Current decisions and open choices

- Keep the Angular frontend and backend in the same repository. Serve reusable API contracts rather than duplicating logic in future clients.
- Use Identity cookies for browser authentication; JWT issuance and refresh tokens are outside the current scope.
- TibiaData is the selected character/online provider. Wiki and market adapters have separate configuration and persisted snapshots.
- Run a single active backend collector. Multiple replicas require distributed job leases and rate-limit coordination.
- Project splitting, the next feature, hosting topology, and future bot authentication remain open choices.

See [TODO](TODO.md) for practical remaining tasks, [frontend design](FRONTEND_DESIGN.md) for UI conventions, and [README](../README.md) for setup and verification commands.
