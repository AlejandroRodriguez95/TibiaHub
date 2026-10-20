# World market values

The NPC selling tool at `/tools` now includes a **Market world** dropdown. Its values come from the [TibiaWiki world list](https://tibiawiki.dev/api/worlds), intersected with the market provider's `/world_data` list. The wiki list includes historic worlds and a category page; only named worlds the market provider actually supports are offered. Supported does not imply a recent scan.

## Collection

The independent `market-prices` job uses the shared refresh coordinator: daily at **10:30 Europe/Vienna**, immediately when no complete cache exists, and catch-up after a missed schedule. Editing `NpcTradeSource.NpcNames` does **not** invalidate or refresh market data. World changes in the browser read cached data only.

Source: [TibiaMarket API](https://api.tibiamarket.top:8001/docs#/default/get_market_values_market_values_get), contract at `/openapi.json`.

- `GET /world_data` identifies supported worlds.
- `GET /item_metadata` returns all item metadata, including IDs, in-game names, and wiki names.
- `GET /market_values?server={world}&skip={offset}&limit=10000` collects **all** items for each supported world, without NPC/item filters. Pagination continues until a short page; duplicate IDs or an excessive page count reject that world's refresh.

Every world has its own complete snapshot. Successful worlds become available immediately, even while others are still collecting. Failed worlds retain their previous data and are retried after 15 minutes. Progress is persisted, so a restart resumes pending worlds without downloading successful worlds from that cycle again. Future daily cycles rediscover the world list and item metadata.

This initial collection downloads every supported world and currently takes roughly **12 minutes** for 113 worlds. The provider enforces **one request per five seconds** (verified via its 429 response); the client spaces market-host requests by six seconds, including retries and pagination. `MarketSource.RequestIntervalSeconds` configures the spacing. Per-request timeouts and bounded HTTP 429/5xx retries remain in place. One world failing does not block already collected worlds or the NPC tool. A new daily schedule starts a new cycle even if the previous day's failures remain, so successful worlds are not stuck with yesterday's data. No browser request triggers upstream downloads.

## Average and matching rules

Default comparison: **monthly buy average** (`month_average_buy` with positive `month_bought`), useful for comparison with selling into market buy offers. Set `MarketSource.AverageBasis` to `sell` for `month_average_sell` with positive `month_sold`. Both fields are cached, so changing this setting does not require another collection. The UI identifies the chosen basis. These are historical averages, before fees, not guaranteed current execution prices.

Match NPC wiki item names to metadata `wiki_name` first, then to a unique in-game `name`, case-insensitively. Match prices by item ID after that. Ambiguous variants are not guessed. Missing items, non-positive averages, and missing/zero trading volume produce `null`, displayed as **Unavailable**. No placeholder zero prices remain.

If the selected average is **strictly greater** than the NPC purchase price, the market-price cell receives a sea-glass border and **Above NPC** text. Delivery-task rows retain their independent amber background/label. Both indications can coexist; equality does not trigger the market border.

## Freshness and persistence

`MarketUpdatedAt` is our collection time. Each item also retains the provider's Unix `time` as `MarketSourceUpdatedAt`; this can be much older. The UI shows row scan dates and warns if the oldest source scan or local collection is over 25 hours old. A daily collection must not be represented as a fresh provider scan. Pending refreshes are identified while last-good data remains available.

Storage remains JSON files under `TibiaHub/App_Data/refresh`, **not PostgreSQL**:

- `MarketWorldList.json`: supported worlds and pending-cycle progress.
- `MarketItemIndex.json`: all item names and IDs.
- `MarketWorldPrices.Antica.json` (and one file per world): all collected prices and source timestamps.

The files are ignored by Git and loaded on demand into memory. Missing/corrupt caches trigger collection. The market source key includes source URLs and a format version, never the NPC list or average basis.

## API additions

`GET /tools/npc-selling` adds `worlds` and market availability metadata. `GET /tools/npc-selling?world=Antica` joins that world's cached averages into NPC items. World names are validated against the cached list; unknown worlds return 400. Missing market data does not hide NPC prices. Item fields include nullable `averageMarketValue`, `isMarketHigher`, and `marketSourceUpdatedAt`. `marketValuesArePlaceholders` is now false. See the controller for the full response contract.
