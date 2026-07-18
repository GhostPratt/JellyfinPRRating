# Jellyfin PR Rating Plugin

Calculates a "PR" age rating for movies and TV shows and applies it as metadata tags.

## How it works

- When new media is added (and via a weekly backfill task), the plugin scrapes
  parental-guidance sources and computes a recommended age:
  - **Movies**: Common Sense Media (base score) adjusted by Kids in Mind,
    Parent Previews, and Dove.
  - **TV shows**: Common Sense Media only, with stronger adjustments.
- The score is rounded down and applied as a `PR-##` tag (e.g. `16.8` → `PR-16`).
- Scores of 19 or higher are tagged `PR-18` plus a separate `Vault` tag.
- If no data can be found, no tags are applied.
- A daily task picks one random item from a random library and re-calculates its
  rating, replacing the old tag (and removing `Vault`) if the result changed.

Tag prefix, Vault tag name, threshold, and cap are configurable on the plugin's
settings page.

## Installation

Add this plugin repository in the Jellyfin dashboard
(**Dashboard → Plugins → Repositories**):

```
https://raw.githubusercontent.com/GhostPratt/JellyfinPRRating/master/manifest.json
```

Then install **PR Rating** from the plugin catalog and restart Jellyfin.

Requires Jellyfin 10.11+.