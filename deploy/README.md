# Debian container deployment

See [how deployment works and remaining work](../docs/DEPLOYMENT_EXPLAINED.md)
for the architecture, runtime settings, persistence, and production readiness review.

Keep the existing host Nginx for HTTPS. The separate production Compose file
does not change the local development database stack.

## Current server configuration

The working host Nginx configuration is saved in [host-nginx.conf](host-nginx.conf).
It serves `tibiaintel.net` and `www.tibiaintel.net`, uses existing Certbot
certificate paths, redirects HTTP to HTTPS, and forwards the API and frontend
to the loopback ports below. It belongs at `/etc/nginx/sites-available/default`
on this server; it is separate from the web container's `web.conf`. Back up the
existing host file before replacing it, then run `nginx -t` before reloading.
Certificate files and private keys are not included in the repository.

The production `.env` also needs `REVERSE_PROXY_IP` for trusted forwarded headers.
The observed Docker gateway on this server was `172.18.0.1`; verify it after
network changes instead of assuming that address on another deployment:

```sh
docker inspect tibiaintel-production-api-1 --format '{{range .NetworkSettings.Networks}}{{.Gateway}}{{end}}'
```

As of 2026-10-23, the live NPC catalogue was verified through HTTPS with 41 NPCs
and 2,547 item entries. The list is maintained in
`TibiaIntel/DataRefresh/refresh-sources.json`; source-recognized spellings include
`Dal the Huntress`, `Nah'Bob`, and `Kiru` (the requested `Kiro` was assumed a typo).
Rebuild/recreate the API after changing this source file. Publication waits for
every configured NPC to collect successfully; a rejected name fails the refresh.

Production Compose explicitly sets `DataRefresh__CacheDirectory` to the mounted
`/var/lib/tibiaintel/refresh` directory. The shared refresh source file no longer
sets a cache path, preventing it from overriding `appsettings.Production.json`.
Do not fix snapshot errors by granting write access to `/app`.

## Build and start

Create a private `.env` file at the repository root containing a strong
`POSTGRES_PASSWORD`. Do not commit it. Avoid semicolons in this password because
it is interpolated into an Npgsql connection string. Protect the file with
`chmod 600 .env`. Changing it later does not change an existing database password.

From the repository root:

```sh
docker compose -f compose.production.yml build
docker compose -f compose.production.yml up -d --wait postgres
docker compose -f compose.production.yml run --rm api --migrate-db
docker compose -f compose.production.yml up -d
docker compose -f compose.production.yml logs --tail=100 api web
```

The migration command exits without starting collectors or the web server.
Back up the database before migrations on subsequent releases. Stop the API
before migrations that require downtime. Do not start the API if migration fails.

The API listens only on host loopback port 5267; static Angular files are served
by a small Nginx container on loopback port 8080. PostgreSQL has no published port.
Configure the existing host Nginx to proxy `/api/` to `http://127.0.0.1:5267`
without stripping the prefix, and other paths to `http://127.0.0.1:8080`.
Forward the public Host, original scheme, and client address, and support
WebSocket upgrades for `/api/hubs/vip`. Before public rollout, configure ASP.NET
forwarded headers to trust the actual Docker-side address of the host proxy;
set REVERSE_PROXY_IP in .env to the verified Docker-side proxy address. Compose passes it to ReverseProxy__KnownProxy; the API processes one trusted forwarding hop.
Do not expose the application publicly until that proxy configuration is verified.

Grant an existing account admin access:

```sh
docker compose -f compose.production.yml run --rm api --grant-admin "your-email@example.com"
```

## Persistent data

Named volumes preserve PostgreSQL data, JSON snapshots, and Data Protection keys.
The API runs as the .NET image's non-root `app` user; its image prepares writable
storage directories before first volume creation. If attaching existing volumes,
ensure their ownership permits that user to write.

Keys are stored at `/var/lib/tibiaintel/keys` with a stable `TibiaIntel` application
name so replacement containers can validate existing login cookies. This follows
[Microsoft's container guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview).
Filesystem permissions protect access; key files are not encrypted at rest by
this configuration. Restrict Docker/host access and protect backups accordingly.
Do not commit or copy production keys into development environments.

Never run `docker compose -f compose.production.yml down -v` unless intentionally
deleting production data. Volumes are not backups; set up off-server backups.
Rebuilding images and recreating containers keeps the volumes.

`ASPNETCORE_ENVIRONMENT=Production` loads `appsettings.Production.json` at runtime.
It selects `/var/lib/tibiaintel/refresh` and `/var/lib/tibiaintel/keys`. Release
builds select compiler optimizations; they do not select the runtime environment.
Environment overrides are `DataRefresh__CacheDirectory` and
`DataProtection__KeysDirectory`. When hosting directly without Docker, create both
directories with ownership for the service user first.

Run one API instance for now: it also runs the collectors and VIP broadcaster.
