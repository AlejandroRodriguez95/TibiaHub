# TibiaHub

TibiaHub is a learning and portfolio project for building a Tibia fansite with ASP.NET Core and Angular.

The goal is to build a web application with reusable API functionality that can later support other clients, such as Discord bots or TeamSpeak 3 bots.

## Planned Stack

- ASP.NET Core for the backend/API.
- Angular for the frontend.
- PostgreSQL for persistence.
- Docker for local database setup.
- ASP.NET Core Identity for user management.
- JWT for API authentication.

## Architecture Direction

The project will use separate projects inside the same solution instead of separate repositories.

Planned structure:

```text
TibiaHub.sln
src/
  TibiaHub.Api
  TibiaHub.Application
  TibiaHub.Domain
  TibiaHub.Infrastructure
client/
  tibiahub-angular
tests/
  TibiaHub.Tests
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

Character data will come from an external Tibia API selected later. The Angular frontend should call `TibiaHub.Api`, and the backend API should handle communication with the external Tibia API.

## Current Status

This project is in the early planning/setup phase.

Current repository contents:

- Minimal ASP.NET Core project.
- Roadmap document.
- Initial Git setup.

See [docs/ROADMAP.MD](docs/ROADMAP.MD) for the current roadmap and open decisions.

## Running Locally

The local development setup is not finalized yet.

Current backend project can be run with:

```powershell
dotnet run --project TibiaHub/TibiaHub.csproj
```

Docker, PostgreSQL, the Angular client, authentication, and the final project structure will be added in later milestones.

