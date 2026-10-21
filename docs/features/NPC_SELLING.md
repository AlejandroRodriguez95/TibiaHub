# NPC selling tool

## Purpose and user flow

Publicly accessible under **Tools** in the navigation, at `/tools`. Select a configured NPC to see every item they buy, its icon (or a placeholder), name, NPC purchase price, and average market value. Prices are per item, in gold coins. Rows matching the delivery-task catalogue have a muted amber background and a visible **Delivery task** label; color alone never carries meaning.

Average market values come from a separately collected world catalogue. Select a **Market world** to compare its monthly average against the NPC price. Higher values get a sea-glass cell border, independently of delivery-task highlighting. Missing averages are **Unavailable**, never placeholder zeroes. See [MARKET_VALUES.md](MARKET_VALUES.md) for average definitions, item matching, freshness, and cache details.

Only collected data is served. Browser requests do not scrape upstream sites or trigger a refresh. The tool needs neither a login nor a database migration.

## Sources and findings (2026-10-20)

- [TibiaWiki API](https://tibiawiki.dev/swagger-ui/index.html), schema at `/api-docs`: `/api/npcs/{name}` validates each configured NPC name. Its NPC payload does not include purchase lists.
- `/api/items/{name}` includes `sellto`, `npcvalue`, and `task_item` (verified with Golden Legs). These could support a future richer item catalogue. However, `/api/items?expand=true` currently returns HTTP 413: 6,560 requested pages exceed the public 5,000-page cap. There is no documented filtered purchase-list endpoint. `/api/pages/Delivery_Task` returns 404 because this page does not map to a supported model.
- The [TibiaWiki MediaWiki API](https://tibia.fandom.com/api.php) supports `action=parse&prop=text&format=json&page=...`. Its rendered NPC page has a dedicated `npc-trade-buys` table containing item-specific purchase prices and icons. Its [Delivery Task page](https://tibia.fandom.com/wiki/Delivery_Task) supplies the complete task item tables. This avoids thousands of individual item requests and handles different NPC prices directly.

The collector reads only the NPC's **Buys** table, never its **Sells** table. Delivery matching is exact, case-insensitive, against decoded wiki item names. It is not a substring match and does not indicate the player's personal weekly assignments. Required trade access, quests, and other in-game restrictions still apply. Icons come from the source's HTTPS Wikia image host; failed/missing images fall back to a decorative diamond. Attribution links remain visible; wiki content is CC BY-SA as noted on the source page.

## Configuration and collection

Edit **`TibiaIntel/DataRefresh/refresh-sources.json`**, outside the feature, to hardcode NPC names in `NpcTradeSource.NpcNames`. It starts with `Rashid` as a working example. Use exact wiki NPC names. Duplicates and surrounding spaces are removed; an empty list is supported. Name changes automatically invalidate the previous configuration's catalogue and trigger collection within the coordinator's next poll. Invalid NPCs or upstream/parser failures never publish a partial catalogue.

The shared refresh coordinator collects on first run, catches up after downtime, and refreshes daily at **10:30 Europe/Vienna** (CET in winter, CEST in summer). Change the JSON time zone if a fixed UTC+01:00 schedule is needed instead. Retry after failure: 15 minutes. Refer to [DATA_REFRESH.md](../DATA_REFRESH.md) for the separate infrastructure contract.

## API

`GET /tools/npc-selling?world=Antica` returns `updatedAt` (UTC), `isStale`, `worlds`, market availability/freshness metadata, and `npcs: [{ name, items: [{ name, npcPrice, iconUrl, isDeliveryTask, averageMarketValue, isMarketHigher, marketSourceUpdatedAt }] }]`. The world parameter is optional; without it market values are null. `marketValuesArePlaceholders` is false.

The endpoint serves the last complete snapshot on upstream failure and marks it stale after 25 hours. With no snapshot, or a changed source configuration, it returns HTTP 503 and `Retry-After: 30`. The UI explains unavailability and provides Retry. An empty configured NPC list returns an empty catalogue. The user must explicitly select an NPC. All timestamps are displayed in the user's local time.

## Acceptance checks

- Tools appears in navigation; selection shows only that NPC's purchases.
- No unselected or invented merchant rows are shown.
- NPC prices, icons, and delivery membership come from collected source data; no demo prices are seeded.
- Matching delivery rows have both color and text indication.
- Market values are world-specific, missing values are identified, and higher-than-NPC borders coexist with delivery-task marks.
- Initial collection, missing sources, stale data, empty configuration, and broken icons have useful states.
- Shared refresh infrastructure retains the previous complete snapshot and survives restarts.
