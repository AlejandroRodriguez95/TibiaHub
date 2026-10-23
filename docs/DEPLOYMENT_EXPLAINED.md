# How production hosting works

This explains the repository setup for one Debian AMD64 server with
2 CPUs, 4 GiB RAM, and 64 GiB disk. Initial hosting is now live; the
[runbook's current server configuration](../deploy/README.md#current-server-configuration)
records the verified routing and NPC catalogue. The readiness list below still
includes follow-up work and checks. For commands, see the
[deployment runbook](../deploy/README.md).

## A request from the browser

```text
Browser -- HTTPS --> host Nginx (existing Debian installation)
                       |
                       +-- /api/* --> localhost:5267 --> api container:8080
                       |                                  |
                       |                                  +--> postgres:5432
                       |
                       +-- other paths --> localhost:8080 --> web container:80
```

The host Nginx owns the public domain and HTTPS certificate. The web container's
second Nginx only serves built Angular files. Two Nginx instances are deliberate:
the existing host installation manages HTTPS, while the frontend image packages
its own static server. Serving Angular directly from the host would also be valid,
but would require copying frontend build files to the host on each release.

Angular runs in the visitor's browser. It requests relative URLs such as
`/api/account/login`, so the browser uses the website's current domain and scheme.
Nginx forwards these requests to .NET without removing `/api`. This keeps browser
pages and API calls on the same origin; no production cross-origin CORS policy is
needed. Local `ng serve` uses `proxy.conf.json` to achieve the same routing.

The VIP connection uses `/api/hubs/vip` and needs WebSocket upgrade forwarding.
Refreshing a frontend page such as `/tools/vip` returns Angular's `index.html`,
then Angular chooses the page. API failures must not return that HTML fallback.

## Images, containers, and builds

An image is a packaged application; a container is a running instance of it.
Compose describes which containers run together, their network, configuration,
ports, startup dependencies, and storage.

The backend Dockerfile first uses the .NET SDK to restore and publish a Release
build. Its final image contains the ASP.NET runtime and published files, and runs
as the non-root `app` user. The frontend Dockerfile uses Node to run `npm ci` and
`npm run build`, then copies only the resulting static files into Nginx. Node,
the Angular development server, and the .NET SDK are not required in the running
application containers. These are multi-stage builds.

The separate `compose.production.yml` starts `postgres`, `api`, and `web`.
The existing `docker-compose.yml` remains the local database-only setup.
Production has its own Compose project name and volumes: local accounts/data
are not automatically transferred. The API and frontend ports are bound to
host loopback; PostgreSQL has no host-published port. Only the host Nginx should
receive public application traffic.

## Release versus Production

These settings control different things:

| Setting | Meaning |
| --- | --- |
| `dotnet publish -c Release` | Compiler/build configuration |
| `ASPNETCORE_ENVIRONMENT=Production` | Runtime configuration environment |
| `appsettings.Production.json` | Production settings loaded by ASP.NET |
| Compose `environment` | Runtime overrides such as the connection string |
| Root `.env` | Values Compose substitutes; not automatically ASP.NET configuration |

Production Compose explicitly sets the runtime environment. That selects the
snapshot and key directories below. Development keeps its existing snapshot
directory and framework-default key handling. In this project, environment and
command-line configuration are reapplied after `refresh-sources.json` so they win.
Double underscores in environment variable names represent configuration nesting,
for example `DataProtection__KeysDirectory`.

## What survives a deployment

| Volume | Container path | Contents | Effect if lost |
| --- | --- | --- | --- |
| `postgres_data` | `/var/lib/postgresql/data` | Accounts, roles, VIP lists, observed sessions | User/history data lost unless restored |
| `refresh_data` | `/var/lib/tibiaintel/refresh` | World/NPC/market JSON snapshots | Must recollect; some tools unavailable initially |
| `protection_keys` | `/var/lib/tibiaintel/keys` | ASP.NET Data Protection keys | Existing cookies/tokens may become invalid |

Docker prefixes named volumes with the Compose project name. Replacing a
container or rebuilding an image keeps these volumes. Changing project/volume
names can make a deployment appear empty by attaching new volumes instead.
`down -v` removes volumes and must not be used for routine production updates.
Volumes do not protect against server/disk loss: backups must exist elsewhere.

Snapshot files contain the latest complete downloaded data. Collectors replace
them atomically so failed downloads do not discard the last valid snapshot.
The image prepares writable directories for the app user. Existing or manually
attached volumes may need ownership adjusted before startup.

Data Protection keys protect login cookies and antiforgery tokens. Stable keys
and the stable application name `TibiaIntel` allow replacement containers to read
previously protected values. This does not change cookie expiry, make sessions
permanent, or replace password hashing. The keys are protected by filesystem
permissions, not encryption at rest in this setup; secure host access and backups.

## Startup, migrations, and collectors

Build images, start PostgreSQL and wait for readiness, run the API image with
`--migrate-db`, and only then start the application. The migration command exits
without starting the HTTP server or background collectors. Startup does not
automatically migrate the database. A migration failure must stop deployment.

The API also runs background refresh jobs, online observations, and VIP broadcasts.
One API instance is intentional. Separating collectors is not needed for the first
deployment. It would require shared-store refresh/invalidation because the current
JSON snapshot reader caches loaded data in memory.

Compose restart policies recover exited containers and normally restart services
after Docker restarts. They do not detect an application that is alive but stuck.
Only PostgreSQL currently has a Compose health check.

## Is this a standard approach?

Yes. Reverse proxy plus static frontend, ASP.NET runtime, PostgreSQL, multi-stage
images, and persistent volumes are conventional choices for a single-server app.
Microsoft documents [ASP.NET behind Nginx](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-10.0)
and [persistent Data Protection keys in containers](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).
Docker documents [multi-stage builds](https://docs.docker.com/build/building/multi-stage/)
and [volume persistence](https://docs.docker.com/engine/storage/volumes/).

This is a deployment foundation, not a complete production rollout. It accepts
single-server downtime and has no automatic failover. Kubernetes, multiple API
replicas, Redis, and a separate worker are not requirements for this initial size.

## Remaining work before public use

- DNS, host Nginx domain/routes, HTTPS, and HTTP redirects are configured. Verify
  automatic certificate renewal, WebSocket upgrades, and frontend deep-link refreshes.
- Configure and verify the implemented trusted forwarded-header handling before the VIP origin check using REVERSE_PROXY_IP in the deployment .env.
  Nginx terminates HTTPS; the internal hop is HTTP. The API must learn the original
  scheme and client IP from a trusted proxy. Docker may make the proxy appear as
  a bridge/gateway address rather than loopback. Do not trust arbitrary forwarders.
- Restrict production hostnames (production Compose restricts `AllowedHosts` to the site domains and loopback names), reject unrelated
  hosts at Nginx, and check firewall exposure. Keep SSH access separate from HTTPS.
- Supply production secrets and decide whether to transfer local database data.
  The current app uses the PostgreSQL bootstrap user, which is a superuser in the
  official image. Create a least-privilege runtime role and use a separate migration
  role for production hardening; this is not yet implemented.
- Set up scheduled off-server PostgreSQL backups, protect key backups, and test a
  restore. A copied live PostgreSQL data directory is not a substitute for a proper
  database backup. Snapshots are rebuildable and can be backed up optionally.
- Add bounded log rotation (Docker's logging limits are not set in this Compose
  file), disk/RAM alerts, external uptime monitoring, and collector-failure alerts.
  Watch database growth and actual resource use on the 4 GiB server. Building on
  CI or another machine can avoid build-time memory pressure on the server.
- Add API readiness/liveness checks and a web health check. Database readiness
  alone does not demonstrate that the app, migrations, or collectors work.
- Apply rate limits to registration, login, and expensive public lookups. Login
  already enables Identity lockout on failed passwords; this does not replace
  request throttling. Public password reset/email verification flows are not yet
  implemented; decide whether they are required for the initial audience.
- Review dependencies and base images for updates/vulnerabilities. The last frontend
  image build reported npm audit findings; triage these before public release.
  Current base image tags float: record image digests/release tags for repeatable
  deployments, keep the previous image, and plan schema-compatible rollback.
- Perform a full HTTPS staging smoke test: registration/login/logout, admin access,
  CSRF, VIP live updates, collectors, migrations, restart/key persistence, and
  backup restore. Verify collectors can reach upstream services, including the
  configured market endpoint on TCP port 8001, and have working DNS/time zones.

## Verification completed locally

Both production images built successfully. The backend suite passed 84 tests.
The static Nginx configuration passed its syntax check, and Compose configuration
validated. A non-root Production API container served the CSRF endpoint, wrote a
key into its volume, and a replacement container reused the same key file.
Subsequently, the app was deployed and the live HTTPS NPC API was verified with
41 NPCs and 2,547 item entries. Market collection was also verified for Antica
and Secura. A browser login test across replacement, backup restore, and full
production acceptance testing remain to be completed.
