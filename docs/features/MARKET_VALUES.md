# World market values

The NPC selling tool at `/tools/npc-selling` (`/tools` redirects there) includes a **Market world** dropdown supplied by the independent world directory. Market collection intersects that directory with the provider's `/world_data` list. Worlds without collected market data remain unavailable; supported does not imply a recent scan.

## Collection

The independent `market-prices` job uses the shared refresh coordinator: daily at **10:30 Europe/Vienna**, immediately when no complete cache exists, and catch-up after a missed schedule. Editing `NpcTradeSource.NpcNames` does **not** invalidate or refresh market data. World changes in the browser read cached data only.

Source: [TibiaMarket API](https://api.tibiamarket.top:8001/docs#/default/get_market_values_market_values_get), contract at `/openapi.json`.

- `GET /world_data` returns supported worlds and their `last_update` scan timestamps in one call.
- `GET /item_metadata` returns all item metadata, including IDs, in-game names, and wiki names.
- `GET /market_values?server={world}&skip={offset}&limit=10000` collects **all** items for each supported world, without NPC/item filters. Pagination continues until a short page; duplicate IDs or an excessive page count reject that world's refresh.

Each collection attempt (including retries) reads world statuses and item metadata. A world is queued only if its cache is missing, belongs to another source, lacks a scan marker, or the advertised scan timestamp is newer than the cached marker. Unchanged and older timestamps do not trigger price downloads, and skipped worlds keep their original collection times. Legacy caches without scan markers are fetched once to establish them.

Each world's full price snapshot stores its successfully fetched provider scan timestamp as `ProviderUpdatedAt`. This is persisted atomically with the prices, so successful worlds remain skipped after a restart or a crash before queue progress was saved. Successful worlds become available immediately; failed worlds retain previous prices and retry after 15 minutes. A provider update that occurs during collection is detected on the next scheduled attempt or retry.

Queue priority is descending aggregate request count, then newest provider scan, then world name for ties. The order is recalculated between world fetches. A validated world selection in `GET /tools/npc-selling` counts as demand, including preferred defaults and unavailable prices. Invalid selections and requests without a world do not count. Counts are case-insensitive, held in memory since backend startup, and contain no user identifiers. They reset on restart; scan markers persist. Popularity changes ordering only: every queued world is still attempted, and requests never trigger upstream fetches directly.

An initial collection downloads every supported world; later collections download only worlds with newer scans or missing caches. The provider enforces **one request per five seconds** (verified via its 429 response); the shared request policy spaces market-host requests by at least six seconds, including retries and pagination. `MarketSource.RequestIntervalSeconds` can increase the spacing but cannot lower it below six seconds. HTTP 429/5xx retries are limited to two; the full `Retry-After` delay applies to every caller of that host, including after a final failure. Per-request timeouts exclude limiter queueing and cooldown waits. One world failing does not block already collected worlds or the NPC tool. Every scheduled attempt rechecks provider timestamps, so newer scans can be queued even while other worlds still need retries. No browser request triggers upstream downloads.

## Average and matching rules

Default comparison: **monthly buy average** (`month_average_buy` with positive `month_bought`), useful for comparison with selling into market buy offers. Set `MarketSource.AverageBasis` to `sell` for `month_average_sell` with positive `month_sold`. Both fields are cached, so changing this setting does not require another collection. The UI identifies the chosen basis. These are historical averages, before fees, not guaranteed current execution prices.

Match NPC wiki item names to metadata `wiki_name` first, then to a unique in-game `name`, case-insensitively. Match prices by item ID after that. Ambiguous variants are not guessed. Missing items, non-positive averages, and missing/zero trading volume produce `null`, displayed as **Unavailable**. No placeholder zero prices remain.

If the selected average is **strictly greater** than the NPC purchase price, the market-price cell receives a sea-glass border and **Above NPC** text. Delivery-task rows retain their independent amber background/label. Both indications can coexist; equality does not trigger the market border.

## Freshness and persistence

`MarketUpdatedAt` is our collection time. Each item also retains the provider's Unix `time` as `MarketSourceUpdatedAt`; this can be much older. The UI shows row scan dates and warns if the oldest source scan or local collection is over 25 hours old. A daily collection must not be represented as a fresh provider scan. Pending refreshes are identified while last-good data remains available.

Storage remains JSON files under `TibiaIntel/App_Data/refresh`, **not PostgreSQL**:

- `WorldList.json`: shared world directory, refreshed independently.
- `MarketWorldList.json`: market-supported worlds, advertised scan timestamps, and pending-cycle progress.
- `MarketItemIndex.json`: all item names and IDs.
- `MarketWorldPrices.Antica.json` (and one file per world): all collected prices, item timestamps, and the persisted provider scan marker.

The files are ignored by Git and loaded on demand into memory. Missing/corrupt caches trigger collection. The market source key includes source URLs and a format version, never the NPC list or average basis.

## API additions

`GET /tools/npc-selling` adds `worlds` and market availability metadata. `GET /tools/npc-selling?world=Antica` joins that world's cached averages into NPC items. World names are validated against the cached list; unknown worlds return 400. Missing market data does not hide NPC prices. Item fields include nullable `averageMarketValue`, `isMarketHigher`, and `marketSourceUpdatedAt`. `marketValuesArePlaceholders` is now false. See the controller for the full response contract.
