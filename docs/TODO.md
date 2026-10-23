# TibiaIntel TODO

## Current state

- One .NET 10 backend project, Angular 22 frontend, and PostgreSQL 17 via Compose on port 5433.
- EF migrations define Identity storage and character observation/session tables. Apply all migrations locally; their presence in Git does not mean a database has been updated.
- Public character lookup is at `/characters`; `/` is the landing page.
- Registration, cookie login, session restoration, logout, and saved default-world preferences are implemented at `/login`, `/register`, and `/account`.
- Account writes require CSRF tokens. Development CORS permits credentials from `http://localhost:4200`.
- The Admin role protects `/admin/users`; `/admin` provides the frontend panel.
- `/tools/npc-selling` serves collected NPC purchases with delivery-task labels and cached world market comparisons. `/tools` redirects there.
- Signed-in users can query retained timing history at `/tools/character-scan`; `/scans` redirects there.
- Shared world/NPC/market refresh jobs persist JSON snapshots. The separate online collector stores observed sessions in PostgreSQL with 14-day retention.
- Backend and frontend tests cover account, admin, tools, and scan behavior. See [README](../README.md) for commands.

## Remaining work

- Manually exercise account flows, default-world saving, admin access, and tool loading/error states in the browser after changes.
- Move any real database credentials out of tracked configuration. Initialize a .NET user-secrets ID if choosing user secrets; keep Compose and API overrides consistent.
- Add email confirmation and password recovery before public account registration.
- Configure production API URLs, HTTPS, origins, SPA route fallback, and persistent Data Protection keys before hosting.
- Provide persistent snapshot storage and select one active collector instance. Distributed collection needs leases and shared rate-limit coordination.
- Consider frontend route guards for navigation convenience; API authorization already protects admin and character scan endpoints.
- Consider splitting backend projects when complexity warrants it; the current single-project layout is intentional for now.

## Authentication decision

Use Identity cookies for the Angular browser frontend. JWT issuance and refresh tokens remain outside current scope. Future bots can use a separate mechanism when needed; public character lookup and NPC selling do not require login.

See [authentication](AUTHENTICATION.md), [the roadmap](ROADMAP.MD), and the feature documents for behavior and limitations.
