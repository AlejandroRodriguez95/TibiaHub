# VIP list

## Status and deferred frontend work

On 2026-10-23, the user confirmed that the VIP feature works as intended in local
use. The current frontend still needs clearer presentation and interaction; its
layout is not the final approved design. Frontend changes are deferred to a later
pass covering all features, not just VIP.

For VIP, that pass should make adding characters, assigning multiple groups,
expanding/collapsing groups, and distinguishing character versus group status easier
to understand. Specific layout and interaction choices remain open. Preserve the
working feature behavior, private account ownership, configurable limits, and live
updates while improving the UI. See [frontend design](../FRONTEND_DESIGN.md#deferred-frontend-polish)
and [remaining work](../TODO.md).

## User experience

**Tools > VIP list** (`/tools/vip`) is visible to all visitors. Anonymous visitors see
links to log in or register. Each signed-in account owns a private list of characters
and custom groups. A character may belong to multiple groups and counts only once
toward the account limit. Ungrouped characters have their own section.

Groups can be expanded/collapsed, renamed, and deleted. Deleting a group removes its
memberships, keeping its characters and their other memberships. Removing a character
from the VIP list removes all its memberships. Group expansion is local to the open
page; characters, groups, and memberships persist in PostgreSQL.

Character names and world are resolved through TibiaData when adding a character;
canonical names prevent case-insensitive duplicates. Invalid or unavailable lookups
leave the list unchanged. Renames and world transfers are not automatically linked:
remove and re-add a character to resolve its current name/world.

Online names are green; offline names red. Text labels also communicate status.
Missing, failed, offline-world, disabled, or stale collection produces **Unknown**.
A group is online if any member is online, otherwise unknown if any member is unknown,
otherwise offline. Empty groups are unknown. Group status is an aggregate presence
indicator, not proof that the characters belong to one person.

## Collection and live updates

The existing `OnlineCollector` continues to collect all active worlds. Its configured
60-second per-world interval starts after an attempt finishes; network delays, batches,
and cooldowns can extend it. VIP subscriptions do not add world polling or scan jobs.
The only upstream VIP request is character identity resolution on add, using the shared
host pacing policy.

Status reads use existing open `OnlineSessions` and each world's latest
`WorldObservation`, from a consistent database transaction. Both source and fetch
age must be within `Vip.StatusMaxAgeSeconds` (default 120). A failed latest attempt or
an offline world immediately makes status unknown. Scan scoring exclusions such as
server save or mass change do not by themselves invalidate a complete online list.

The authenticated `/hubs/vip` SignalR connection is active while the VIP page is open.
The server identifies subscribers from their authentication principal, never from a
browser-supplied user ID. A shared broadcaster checks stored state every five seconds
for connected accounts only. It sends a full `Snapshot` initially or after list/group
changes, and `Statuses` deltas only when presence changes. No per-user upstream workers
are created. Multiple tabs share one server-side status comparison per user.

The frontend wraps SignalR events in an RxJS observable and renders state using Angular
signals (reactive values that update the screen when changed). Reconnects obtain a
fresh snapshot; disconnects display unknown status. Events are timestamped to avoid
older responses overwriting newer status. Leaving the page closes the connection and
unsubscribes. Failed initial connections can be retried from the UI.

Cookie authentication and the existing development CORS origin apply. Browser hub
origins are explicitly checked because CORS alone does not protect WebSockets.
Hub methods are read-only; all list writes use the existing global CSRF filter.
Authentication expiration closes a connection. As with existing cookie sessions,
logout in a different tab does not revoke a copied or already-established session;
leaving the VIP page disconnects its connection.

## Configuration

In `TibiaIntel/appsettings.json`:

```json
"Vip": {
  "MaxCharactersPerUser": 1000,
  "MaxGroupsPerUser": 100,
  "StatusMaxAgeSeconds": 120
}
```

Environment overrides include `Vip__MaxCharactersPerUser`. Limits must be positive;
status age must be at least 60 seconds. The backend enforces limits and returns them
with snapshots so the UI displays current values. Reducing a limit keeps existing
entries; users can remove them but cannot add while at/above the new limit. The group
limit also bounds membership growth. Account-row locks serialize concurrent list
writes before checking limits. Duplicate membership IDs are de-duplicated.

## API and storage

All endpoints require login, return private no-store responses, and scope records to
the current account. Unknown or other users' IDs return 404. Unsafe requests require
a fresh CSRF token in `X-CSRF-TOKEN` plus cookies.

| Method | Path | Body / behavior |
| --- | --- | --- |
| GET | `/tools/vip` | Snapshot with `capturedAt`, `maxCharacters`, `maxGroups`, `characters`, `groups`. |
| POST | `/tools/vip/characters` | `{ "name": "Lazy Snorlax" }`; resolve and add. |
| DELETE | `/tools/vip/characters/{id}` | Remove character and memberships. |
| POST | `/tools/vip/groups` | `{ "name": "Alejandro" }`; create group. |
| PUT | `/tools/vip/groups/{id}` | `{ "name": "Alejandro" }`; rename group. |
| DELETE | `/tools/vip/groups/{id}` | Remove group, retain characters. |
| PUT | `/tools/vip/characters/{id}/groups` | `{ "groupIds": [1, 2] }`; replace memberships; `[]` ungroups. |

Successful writes return 204. Entries contain `id`, `name`, `world`, `groupIds`,
`status` (`online`, `offline`, `unknown`), and nullable `lastObservedAt`. Groups contain
`id` and `name`. The hub's read-only `Snapshot` method returns the same snapshot;
`Statuses` events contain `capturedAt` and changed character IDs/statuses.

For a new or not-yet-updated database, apply the `AddVipLists` migration before
starting the backend. Run this from the repository root
(`C:\Users\Alejandro\Desktop\TibiaIntel` in the current local setup), with
PostgreSQL running:

```powershell
dotnet ef database update --project TibiaIntel
```

`VipCharacters` and `VipGroups` belong to Identity users; `VipMemberships` is a
many-to-many join. User deletion cascades to owned VIP data. VIP characters are
independent of retained scan history, so pruning observations never deletes a VIP.

The existing single-backend deployment constraint remains. Scaling live connections
across replicas requires shared collector coordination and a SignalR backplane or
managed SignalR service. The five-second connected-account reads should be measured
before a large deployment; limits do not alone guarantee a fixed total memory bound.

## Implementation references

- [SignalR JavaScript client and reconnect behavior](https://learn.microsoft.com/aspnet/core/signalr/javascript-client?view=aspnetcore-10.0).
- [SignalR authentication and authorization](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0).
- [Angular signals and RxJS interoperability](https://angular.dev/ecosystem/rxjs-interop).
