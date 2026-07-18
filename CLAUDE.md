# JellyfinPRRating

Jellyfin plugin that calculates a "PR" age rating for movies and TV series by
scraping parental-guidance sites, then applies the result as metadata tags.
The scoring logic is a line-by-line C# port of the (private) Python project
`GhostPratt/PlexRating` — when changing a formula or scraper, keep parity with
that project or consciously diverge.

## High-level flow

1. A trigger fires for an item (new media added, or one of the scheduled tasks).
2. `PrRatingCalculator` scrapes the sources and computes a raw age score:
   - **Movies**: Common Sense Media provides the base score
     (site×3 + parent×2 + kid×1, ÷6), adjusted by Kids-in-Mind, Parent
     Previews, and Dove. Missing non-CSM sources are simply skipped.
   - **TV series**: Common Sense Media only, with stronger adjustment weights.
   - **No Common Sense data → no score → no tags are touched.**
3. `PrTagService` turns the score into tags:
   - Floor the score → `PR-##` (16.8 → `PR-16`).
   - Score ≥ 19 (`VaultThreshold`) → tag is capped at `PR-18` (`MaxTagRating`)
     **plus** a separate `Vault` tag.
   - On recalculation: a stale `PR-##` tag is replaced and `Vault` is removed
     if the item no longer qualifies. If scraping returns no data, existing
     tags are deliberately kept (transient failures must not erase ratings).

## Components

| Path | Role |
|---|---|
| `Plugin.cs` | Plugin entry point (GUID `0842582a-0242-4b3b-8475-71d3885b42a2`), config page registration |
| `ServiceRegistrator.cs` | `IPluginServiceRegistrator` — DI wiring for all services below |
| `Rating/PrRatingCalculator.cs` | Orchestrates scrapers, applies the scoring formulas; splits trailing "(YYYY)" off titles that came from filenames |
| `Rating/CommonSenseScraper.cs` etc. | One scraper per source, all deriving from `ScraperBase` |
| `Rating/ScraperBase.cs` | Shared HTTP: browser-like headers, 15s timeout, one retry on transient failures (network error/timeout/5xx/429), warning logs for non-404 failures |
| `Rating/ScraperHelpers.cs` | Slugify / title normalization / tag stripping (ports of `_http.py`) |
| `Services/PrTagService.cs` | Tag application/removal logic described above |
| `Services/ItemAddedListener.cs` | `IHostedService`; subscribes to `ILibraryManager.ItemAdded`, fire-and-forget rates new Movies/Series |
| `ScheduledTasks/BackfillRatingsTask.cs` | Weekly (Sun 3 AM): rate every item that has no `PR-` tag |
| `ScheduledTasks/RandomRecalculateTask.cs` | Daily (4 AM): random library → random item → re-rate |
| `ScheduledTasks/RecalculateAllTask.cs` | Manual only (no default trigger): re-rate the entire library |
| `Configuration/` | `PluginConfiguration` (TagPrefix, VaultTag, VaultThreshold, MaxTagRating) + dashboard config page |

## Scraping notes (hard-won)

- All parsing is regex over raw HTML — no HTML-parser dependency, so the DLL
  ships alone. When porting BeautifulSoup logic, beware text vs. markup:
  Dove's totals live in `data-rating="N"` attributes on `section-circle`
  elements; naive "first number in block" grabs the wrong digit from CSS
  class names like `rating-circle--negative-4`.
- Kids-in-Mind URLs concatenate words with no separators
  (`/n/nightbitch...htm`); discovery falls back to the per-letter listing
  page with normalized-title matching.
- Common Sense Media ratings come from embedded `csm_review_rating_*` JSON
  keys; slug disambiguation tries `slug`, `slug-0` … `slug-4` and verifies
  `datePublished` against the item year (±1).
- A source can fail transiently on the server even when it works elsewhere;
  never treat fetch failure as proof a review doesn't exist (this mis-scored
  a movie once — see 0.0.3 changelog).

## Constraints

- **Target**: net9.0, Jellyfin packages 10.11.6 (`ExcludeAssets=runtime`),
  `targetAbi` 10.11.0.0. Server runs 10.11.x.
- Jellyfin 10.11 API quirks: `SortOrder` is in
  `Jellyfin.Database.Implementations.Enums`, `ItemSortBy` in
  `Jellyfin.Data.Enums`, `BaseItemKind` filtering via `InternalItemsQuery`;
  the `DefaultAuthorization` policy no longer exists (use `[Authorize]`).
- Analyzers: `TreatWarningsAsErrors` + `AllEnabledByDefault` with a NoWarn
  list (CA1848, CA1062, CA1031, CA1054, CA1308) — new code must build clean
  against the rest.
- Plugin services must be wired through `IPluginServiceRegistrator`;
  `IStartupFilter`/middleware tricks do not work (plugins load after the
  pipeline is built).
- Item queries must set `IsVirtualItem = false` to skip placeholder entries.

## Build, release, distribution

- Build: `dotnet build --configuration Release` →
  `bin/Release/net9.0/JellyfinPRRating.dll`. A deploy is the DLL +
  `meta.json` in the server's plugin directory, then restart Jellyfin.
- **Release flow**: bump `version` in `meta.json` *and* `build.yaml`
  (four-part, e.g. `0.0.4.0`), commit, push, then publish a GitHub release
  tagged with the three-part version (`0.0.4`). The release workflow
  (`.github/workflows/release.yaml`) builds, zips DLL+meta.json, uploads the
  asset, and commits an updated `manifest.json` to master.
- Because the workflow pushes to master, **always pull/rebase before pushing**
  after a release, and never publish the release tag before the code commit
  is on remote master (the workflow builds `ref: master`, not the tag).
- Users install via the Jellyfin plugin repository URL:
  `https://raw.githubusercontent.com/GhostPratt/JellyfinPRRating/master/manifest.json`.
  `manifest.json` checksums are md5 of the release zip; Jellyfin validates
  them, so never hand-edit a checksum without re-hashing.