# TibiaIntel

TibiaIntel is a learning and portfolio project for building a Tibia fansite with ASP.NET Core and Angular.

The goal is to build a web application with reusable API functionality that can later support other clients, such as Discord bots or TeamSpeak 3 bots.

## Stack and repository structure

- .NET 10 / ASP.NET Core API with EF Core and Identity cookie authentication.
- Angular 22 frontend.
- PostgreSQL 17, with Docker Compose for local development.
- JSON snapshots for shared NPC, world, and market data; PostgreSQL for accounts, observed character sessions, and private VIP lists/groups.

```text
TibiaIntel.sln
TibiaIntel/                 ASP.NET Core API, collectors, and EF migrations
client/tibiaintel-angular/  Angular application
tests/TibiaIntel.Tests/     Backend unit and integration tests
docs/                      Feature contracts, design, and project plans
```

The backend currently lives in one project. Splitting it into API, Application,
Domain, and Infrastructure projects is a future refactoring option. Angular calls
the backend; character lookup uses TibiaData through the API.

## Current Status

The API supports public TibiaData character lookup and Identity account registration,
login, current-user lookup, and logout. Angular provides character lookup and account
forms with cookie sessions and CSRF protection.

The Petrol & Lantern frontend includes a Tools tab with an NPC selling catalogue.
Configure merchants in `TibiaIntel/DataRefresh/refresh-sources.json`. A shared background
collector refreshes prices and delivery-task membership daily at 10:30 Europe/Vienna,
retaining the last complete snapshot across failures and restarts. Independently cached
[world market averages](docs/features/MARKET_VALUES.md) support price comparisons. See [NPC selling](docs/features/NPC_SELLING.md) and
[shared data refreshing](docs/DATA_REFRESH.md).

See [the roadmap](docs/ROADMAP.MD), [current next steps](docs/TODO.md), and
[authentication details](docs/AUTHENTICATION.md).

Character timing scans are available under **Tools > Character scan** (`/tools/character-scan`) for signed-in accounts. The collector monitors all active worlds and stores 14 days of observed sessions in PostgreSQL. Apply database migrations before starting the backend. See [character scans](docs/features/CHARACTER_SCANS.md) for scoring, exclusions, retention, and configuration.

The private [VIP list](docs/features/VIP_LIST.md) at `/tools/vip` lets signed-in users
track characters in custom groups with live online/offline/unknown status. Visitors
see a login prompt. The default limit is configurable through
`Vip:MaxCharactersPerUser` (1,000); apply the `AddVipLists` migration before starting
against a database that does not have it yet. VIP behavior has been confirmed working
in local use; [frontend clarity and polish](docs/FRONTEND_DESIGN.md#deferred-frontend-polish)
across all features are deferred to a later pass.

## Running Locally

The Compose project is named `tibiaintel` and uses the `tibiaintel` database and
user. When upgrading an existing checkout after the rename, stop the previous
PostgreSQL container before starting this stack because both use port 5433.
Existing Docker volumes are preserved, but the renamed stack uses a new volume.
Back up and transfer any existing database data before switching; otherwise run
the migrations below to initialize the new database.

Install the .NET 10 SDK and Docker Desktop. From the repository root, start Docker Desktop with Linux containers, then run:

```powershell
docker compose up -d
dotnet tool restore
dotnet ef database update --project TibiaIntel
dotnet run --project TibiaIntel --launch-profile http
```

In another terminal:

Install Node.js (which includes npm) and make sure it is on your Windows `PATH`.
Open a new terminal after installing, then verify with `node --version` and
`npm --version`. Rider may provide its own Node.js runtime that is available only
inside the IDE; a regular Command Prompt needs Node.js on its own `PATH`.

```powershell
cd client/tibiaintel-angular
npm ci
npm start
```

Run these commands from `client/tibiaintel-angular`, where `package.json` lives.
`npm ci` installs this project's dependencies, including the Angular CLI;
you do not need to install Angular CLI globally. If CMD reports that `npm` is not
recognized, check the Node.js installation and `PATH` first.

Open `http://localhost:4200`; the API runs on `http://localhost:5267`.
Use Node.js 22.22.3+, 24.15.0+, or another version supported by Angular 22.
The Development connection string already matches Compose's default database on port
5433. `.env.example` documents Compose overrides; a root `.env` is read by Compose,
not by ASP.NET Core. If you change the database credentials or port, also set
`ConnectionStrings__DefaultConnection` in the API process environment. Keep real
credentials outside source control. The project does not currently declare a
`UserSecretsId`; initialize user secrets before using that storage option.

## Admin panel

Register an account normally, then grant it the Identity `Admin` role from a trusted
terminal with access to the application's database:

```powershell
dotnet run --project TibiaIntel -- --grant-admin "your-email@example.com"
```

The command updates an existing account and exits without starting the server.
It uses the same connection configuration as the application; apply migrations first.
Log out and back in after granting access, then open **My account → Admin panel**
or `http://localhost:4200/admin`.

`GET /admin/users` returns registered user IDs and emails, ordered by email.
Anonymous requests receive 401; signed-in users without the `Admin` role receive 403.
Registration cannot assign roles. Identity role tables already exist in the initial
migration, so this feature needs no new migration. The panel displays email only;
extend `AdminUserResponse` and the Angular table when adding more profile fields.

## Backend Tests

Start Docker Desktop (using Linux containers), then run:

```powershell
dotnet test TibiaIntel.sln
```

The registration integration tests use xUnit, `WebApplicationFactory`, and Testcontainers.
They start a temporary PostgreSQL 17 container, apply the application's migrations,
and remove the container when the suite finishes. No development database or `.env`
configuration is needed. The first run requires internet access to restore NuGet
packages and download the container images.

Coverage includes registration and password rules, cookie sessions and CSRF,
admin authorization and default-world preferences, NPC parsing, market snapshots,
upstream pacing, online observations, and character scan scoring/retention.

For frontend tests and a production build, run from `client/tibiaintel-angular`:

```powershell
npm test -- --watch=false
npm run build
```

## Documentation

- [Roadmap](docs/ROADMAP.MD) and [remaining work](docs/TODO.md).
- [Authentication and account preferences](docs/AUTHENTICATION.md).
- [NPC selling](docs/features/NPC_SELLING.md), [market values](docs/features/MARKET_VALUES.md), and [character scans](docs/features/CHARACTER_SCANS.md).
- [Shared data refresh operations](docs/DATA_REFRESH.md).
- [Frontend design](docs/FRONTEND_DESIGN.md) and [ASP.NET Core study notes](docs/ASP_NET_CORE_PROGRAM_SERVICES.md).
