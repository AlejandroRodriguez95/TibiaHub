# Shared data refresh infrastructure

This infrastructure lives in `TibiaIntel/DataRefresh`, separately from frontend tools and feature controllers. It is intended for future shared data collectors as well as NPC purchases.

## Protocol

Implement `IRefreshJob` with a stable `Name`, `LastSuccessAsync(CancellationToken)`, and `RefreshAsync(CancellationToken)`. Register the job as a singleton `IRefreshJob` in `Program.cs` and add a schedule keyed by its name in `DataRefresh/refresh-sources.json`. Collectors must be safe for singleton use, respect cancellation, validate source responses, and publish only complete results. Feature controllers must read snapshots rather than call upstream services.

`RefreshCoordinator` is a hosted background service with one asynchronous worker per registered job. Each worker polls every 30 seconds, never overlaps itself, and isolates failures from other jobs. A job with no valid snapshot runs immediately. Daily schedules run if the latest scheduled occurrence is newer than the last success, so missed schedules catch up on startup without replaying every missed day. The default daily job is `npc-trades` at 10:30 in `Europe/Vienna`. Time zones use `TimeZoneInfo`, including daylight saving. Invalid DST wall times move to the next valid minute; ambiguous times choose the later occurrence. At the ordinary 10:30 schedule neither ambiguity occurs.

For interval jobs use `IntervalMinutes: 60` **instead of** `DailyAt`. The interval is measured from the last successful completion, not the last attempt. `RetryMinutes` controls failure backoff. Configuration errors and source failures are logged, then retried; they do not terminate the web host. Timing precision is approximately 30 seconds plus collection duration. If a job is disabled or unscheduled it does not run.

```json
{
  "DataRefresh": {
    "Enabled": true,
    "CacheDirectory": "App_Data/refresh",
    "Schedules": [
      { "Job": "npc-trades", "DailyAt": "10:30", "TimeZone": "Europe/Vienna", "RetryMinutes": 15 },
      { "Job": "future-example", "IntervalMinutes": 60, "RetryMinutes": 5 }
    ]
  }
}
```

The JSON source file reloads on change. Environment variables and command-line configuration override it (for example `DataRefresh__Enabled=false`). Cache-directory changes require a restart. Tests disable the coordinator so they never crawl real sites. The JSON is copied into build/publish output.

## Snapshot storage

`JsonSnapshotStore<T>` persists `DataSnapshot<T>` with `UpdatedAt`, `SourceKey`, and `Data`. It serializes access, writes a temporary file, atomically replaces the previous file, and only then updates its in-memory snapshot. Files live in `TibiaIntel/App_Data/refresh` by default and are ignored by Git. Corrupt/unreadable files are logged and treated as missing. Storage failures fail the refresh rather than claiming success. Cancellation before publication leaves the last complete snapshot intact. Mount this directory on persistent storage when running in a container.

For NPC data the source key includes normalized names and source URLs, so adding/removing an NPC triggers collection and prevents serving an outdated merchant selection. Any failed NPC or task-page parse rejects the whole new snapshot. Existing same-configuration data stays available. The HTML adapter accepts only the dedicated purchase table and task item tables; changes in source markup fail closed rather than silently publishing incomplete prices. Purchase rows are sorted by name and de-duplicated.

## Source behavior and operations

The independent `market-prices` job uses the same coordinator and schedule, but is never invalidated by NPC configuration changes. It reads the shared world directory and intersects it with the market provider's supported worlds, collects the full item feed for each world, and maintains independent atomic world snapshots. Provider scan markers persist with each world's prices; attempts queue only missing caches or newer scans. Aggregate user request counts prioritize queued worlds, followed by scan recency. Queue order is recalculated between fetches. Counts reset on restart; persisted scan markers continue to suppress unchanged downloads. Failed-world retries skip unchanged successful worlds. See [MARKET_VALUES.md](features/MARKET_VALUES.md) for the contract, average semantics, and provider freshness rules. `JsonSnapshotStore<T>` supports an optional escaped file key for per-world files.

`NpcTradeSourceClient` uses a 45-second HTTP timeout and identifies itself with a User-Agent. All refresh clients use the singleton `UpstreamRequestPolicy`, which serializes requests per hostname across jobs and HTTP clients. Market requests have a six-second minimum interval (or a larger configured `MarketSource.RequestIntervalSeconds`); other hosts have a 2.1-second minimum, leaving margin below the wiki API default of 30 requests/minute. The old per-NPC delay is no longer needed. One task-page request and one wiki-page request per NPC obtain the actual purchase catalogue. No full item crawl is performed. Upstream source links and limitations are documented in [the feature](features/NPC_SELLING.md).

Start the backend normally. Watch `RefreshCoordinator` logs for completion/failure. To force a fresh collection, stop the backend and remove the relevant `NpcTradeCatalog.json` cache file, then restart. A failed initial refresh is retried automatically; the API returns 503 until a valid result exists. No public mutation endpoint triggers collection. Source configuration is trusted operator input, not browser input.

This implementation supports a **single backend instance**. Multiple replicas need a distributed job lease and shared durable store before enabling the coordinator on all instances. File snapshots deliberately avoid adding refresh infrastructure to Identity's database. A future SQL/object-store implementation can replace the store while preserving the job protocol.

The independent `world-list` job owns the reusable world directory. `WorldListService` fetches from `WorldSource:Url`, persists `WorldList.json`, and exposes cached reads to account settings, NPC selling, and market collection. Reads never call an external API. Its daily schedule and retry interval are configured separately from market prices. A failed refresh retains the previous directory. Market cycle progress remains in `MarketWorldList.json` and does not control directory availability.

## Upstream rate-limit enforcement

HTTP 429/5xx responses get at most two retries. Both delta-seconds and HTTP-date `Retry-After` headers set a shared per-host cooldown with no 60-second cap. HTTP-date delays use the response's `Date` header when present to tolerate clock skew. Cooldowns are recorded even on the final failed attempt and remain active when a caller cancels, so another job cannot bypass them. Responses without a retry header use exponential backoff in addition to minimum request spacing. Long waits remain cancellable and happen outside the per-request HTTP timeout. Automatic redirects are disabled on refresh clients so hidden redirected requests cannot bypass pacing.

The limiter and cooldown state live in memory in one process. Run one active refresher per public IP; this does not coordinate separate processes, unrelated applications sharing the IP, or restarts. A multi-replica deployment requires a distributed limiter and job lease before enabling refresh on multiple replicas. This is pacing and backoff enforcement for the known upstream limits, not automatic discovery of future quota changes.
