# TibiaIntel

TibiaIntel is a learning and portfolio project for building a Tibia fansite with ASP.NET Core and Angular.

The goal is to build a web application with reusable API functionality that can later support other clients, such as Discord bots or TeamSpeak 3 bots.

## Planned Stack

- ASP.NET Core for the backend/API.
- Angular for the frontend.
- PostgreSQL for persistence.
- Docker for local database setup.
- ASP.NET Core Identity for user management.
- ASP.NET Core Identity cookies for Angular authentication.

## Architecture Direction

The project will use separate projects inside the same solution instead of separate repositories.

Planned structure:

```text
TibiaIntel.sln
src/
  TibiaIntel.Api
  TibiaIntel.Application
  TibiaIntel.Domain
  TibiaIntel.Infrastructure
client/
  tibiaintel-angular
tests/
  TibiaIntel.Tests
docs/
  ROADMAP.MD
```

The API should be the reusable surface for the Angular frontend and future bot integrations.

## First Planned Feature

The first feature will be a simple Tibia character lookup.

Initial displayed data may include:

- Character name.
- Level.
- Vocation.
- World.
- Guild.
- Residence or other available profile fields.

Character data will come from an external Tibia API selected later. The Angular frontend should call `TibiaIntel.Api`, and the backend API should handle communication with the external Tibia API.

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

Character timing scans are available under **Tools > Character scan** (`/tools/character-scan`) for signed-in accounts. The collector monitors all active worlds and stores 14 days of observed sessions in PostgreSQL. Apply the new migration before restarting the backend. See [character scans](docs/features/CHARACTER_SCANS.md) for scoring, exclusions, retention, and configuration.

## Running Locally

The Compose project is named `tibiaintel` and uses the `tibiaintel` database and
user. When upgrading an existing checkout after the rename, stop the previous
PostgreSQL container before starting this stack because both use port 5433.
Existing Docker volumes are preserved, but the renamed stack uses a new volume.
Back up and transfer any existing database data before switching; otherwise run
the migrations below to initialize the new database.

Start Docker Desktop with Linux containers, then run:

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
npm install
npm start
```

Run these commands from `client/tibiaintel-angular`, where `package.json` lives.
`npm install` installs this project's dependencies, including the Angular CLI;
you do not need to install Angular CLI globally. If CMD reports that `npm` is not
recognized, check the Node.js installation and `PATH` first.

Open `http://localhost:4200`; the API runs on `http://localhost:5267`.
Use Node.js 22.22.3+, 24.15.0+, or another version supported by Angular 22.
Configure `ConnectionStrings:DefaultConnection` for your local PostgreSQL instance;
keep real credentials outside source control.

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

Coverage includes successful registration and password hashing, duplicate emails
(including different casing), request validation, and Identity password rules.


Authentication coverage also verifies current-user lookup, isolated cookie sessions, logout, and CSRF rejection.

