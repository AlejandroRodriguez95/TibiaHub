# TibiaIntel Angular frontend

Angular 22 application using the [Petrol & Lantern design](../../docs/FRONTEND_DESIGN.md).

## Local development

Use Node.js compatible with the installed Angular packages (currently
`^22.22.3 || ^24.15.0 || >=26.0.0`). From this directory:

```powershell
npm ci
npm start
```

Open `http://localhost:4200`. Start the API from the repository root with
`dotnet run --project TibiaIntel --launch-profile http` after setting up PostgreSQL
and applying migrations. See the [root README](../../README.md) for the full setup.
The npm scripts use the local Angular CLI; no global CLI installation is required.

## Routes

| Path | Purpose |
| --- | --- |
| `/` | Landing page and character search handoff. |
| `/characters?name=Example` | Public character lookup; the name parameter runs a lookup. |
| `/tools/npc-selling` | Public NPC purchases and world market comparisons. |
| `/tools/vip` | Private VIP list and custom groups with live presence; login prompt for anonymous visitors. |
| `/tools/character-scan` | Session timing results for signed-in users. |
| `/login`, `/register`, `/account` | Account forms, session details, default-world preference, and logout. |
| `/admin` | Admin user list. |
| `/tools`, `/scans` | Redirects to NPC selling and character scans respectively. |

Unknown routes redirect home. Components display authentication/authorization states;
there are no route guards. The API enforces protected endpoint access.

## Authentication and API configuration

API requests use relative `/api` URLs. The development server proxies these to `http://localhost:5267`, including WebSockets. In production, forward `/api/*` to the backend without stripping the prefix and serve other paths with an Angular index.html fallback.
Account and authenticated tool requests send credentials. The browser manages the
HttpOnly Identity cookie; credentials are not stored in localStorage. Each account
write first obtains a fresh token from `/account/csrf`, then sends `X-CSRF-TOKEN`.
See [authentication](../../docs/AUTHENTICATION.md) for contracts and deployment notes.

Production hosting must serve `index.html` for frontend routes, including direct
links and refreshes. The backend does not currently host the Angular build.

## Verification and build

```powershell
npm test -- --watch=false
npm run build
```

`npm test` runs Angular's Vitest setup; omit `--watch=false` for watch mode.
Build artifacts are written under `dist/tibiaintel-angular/`. No end-to-end test
runner is configured.

Use `npm exec ng generate component component-name` for local CLI scaffolding.

The [VIP feature](../../docs/features/VIP_LIST.md) uses an RxJS SignalR event stream and Angular signals for displayed state. Its connection closes when the page is destroyed.
