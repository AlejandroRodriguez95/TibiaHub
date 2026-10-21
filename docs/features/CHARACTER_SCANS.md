# Character scans

Signed-in users can open **Tools > Character scan** (`/tools/character-scan`) to compare same-world login/logout timing over a rolling 14-day window. Cross-world matching and premium entitlements are not implemented. Scans query stored observations only and never trigger upstream requests. Collection must run before useful history exists; 14 days of data do not appear immediately.

## Collection and API behavior

The independent `OnlineCollector` discovers every active regular and tournament world through TibiaData `GET /v4/worlds`, refreshing discovery every six hours. It fetches `GET /v4/world/{name}` for each discovered world with bounded concurrency and a due time per world. The minimum interval per world is 60 seconds after its previous attempt completes; requests may take longer under load, network failures, or upstream cooldowns. A slow batch can reduce coverage; incomplete intervals are excluded rather than treated as precise events.

TibiaData's [FAQ](https://tibiadata.com/faq/) documents a 60-second cache for these endpoints and no requests-per-second limit for the production API. Its development host has a quota unsuitable for all-world monitoring and is explicitly rejected. The shared request policy staggers production TibiaData requests by 250ms and preserves full host-wide Retry-After cooldowns. Other providers retain their existing pacing. Run one active collector per public IP/process deployment; multi-replica collection needs distributed coordination.

The response's `information.timestamp` is the source observation time, distinct from our fetch time. Repeated or older source timestamps do not create observations or session changes. Missing lists, count mismatches, duplicate names, incorrect worlds, timestamps more than three minutes old, and timestamps more than five seconds in the future fail closed. Failures are recorded as gaps; they are never interpreted as a world with zero players. Offline world responses produce explicitly uncertain boundaries.

## PostgreSQL storage

- `ScannedCharacters`: a world, display name, and normalized name, referenced by ID from sessions.
- `OnlineSessions`: observed login/logout boundaries, their preceding observation times, and reliability flags for each boundary. A partial unique index allows at most one open session per character.
- `WorldObservations`: source/fetch timestamps, world, online count, source identity, and the reason a transition was excluded. It does not contain a full copy of the online list.

Every fresh, validated observation is committed with its session changes in one transaction. Unchanged players cause no session writes. Processing loads only that world's open sessions and current online set; historical sessions are not held in RAM. New scopes reconstruct current state from open sessions and the last valid observation, so restarts do not require the initial full list. The initial observation has unknown login times and cannot generate candidate logins. Session boundaries describe observations, not exact real-world login times; uninterrupted presence across a collection gap cannot be proven.

Daily cleanup removes sessions ending before the 14-day cutoff, older observation metadata except the latest valid observation per world, and characters with no retained sessions. Open sessions and sessions overlapping the cutoff remain. Queries additionally enforce the 14-day scan window even between cleanup runs.

Apply the migration before starting the collector:

```powershell
dotnet ef database update --project TibiaIntel
```

Collection respects both `DataRefresh:Enabled` and `CharacterScan:Enabled`. Tests disable background collection and use temporary PostgreSQL databases.

## Matching

For each reliable target logout, find reliable candidate logins on the same world:

- Observed at the logout snapshot through 60 seconds afterward, inclusive: **+10**.
- More than 60 seconds through 120 seconds afterward, inclusive: **+5**.
- Before the logout snapshot or after 120 seconds: no points.

A candidate receives at most one award per target logout; if they reconnect repeatedly, the higher award wins. The target cannot match itself. All target logouts and candidate logins use source observation times. The response shows observed logouts, usable logouts, retained history bounds, total score, and counts in each scoring band. Scores express timing correlations, not probabilities or proof of shared ownership. Renames and transfers are not identity-linked; names are normalized within a world.

The complete two-minute follow-up window must have continuous reliable observations, including an observation at or after the window end. A newest or incomplete logout is not scored yet. Gaps greater than 90 seconds by default, failed requests, source changes, offline worlds, server-save periods, and mass changes prevent matching across their intervals.

Default exclusions are conservative and configurable in `CharacterScan` in `DataRefresh/refresh-sources.json`:

- Server save: 09:55-10:15 in Europe/Berlin, accounting for daylight saving. These are configured exclusion bounds, not automatic outage detection.
- Mass disconnect/reconnect: at least 20 departures or arrivals, representing at least 40% of the previous online population.
- Mass events, offline worlds, and server-save observations also have a two-minute recovery exclusion.

Mass-event detection is a heuristic: unusual legitimate activity can be excluded, and small disconnect events may not meet the thresholds. Failed writes leave previously committed state intact; subsequent source gaps are excluded.

## Endpoints

The endpoint requires an authenticated account and returns no-store responses:

- `GET /tools/character-scan?name=Example`: same-world timing results with automatic world selection. The character name is bounded; its world is resolved from its most recent retained session. Transfers use the most recently observed session start, without linking history across worlds. Characters without retained history return 404.
