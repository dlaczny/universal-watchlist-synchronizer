# Change Log

## 2026-07-12

- Deployed backend and movie worker to the homelab from exact Movie-CI-passing
  SHAs while keeping all runtime credentials host-local and mode `0600`.
- Added separate long backend-refresh timeouts and a deployment window that
  accommodates the real movie dataset.
- Made Radarr exclusion overrides explicit, skipped same-folder TMDB
  collisions, widened Plex discovery while retaining exact-TMDB authorization,
  and converted true catalog misses into non-mutating report skips.
- Hardened deployment acceptance so every release and rollback must write a
  fresh worker heartbeat instead of inheriting persisted health.
- Completed supervised apply and convergence: 36 Radarr additions and 64 Plex
  watchlist additions succeeded across the rollout, with no automatic removals,
  file deletion, or Plex-library deletion.
- Enabled hourly unattended movie apply and the five-minute CI-gated deployment
  timer; preserved the legacy dirty checkout for rollback observation.

## 2026-07-11

- Added authenticated movie-only backend sync and a complete worker snapshot.
- Replaced the production worker path with one ownership-aware plan-and-apply
  engine, policy gates, reports, and heartbeat health.
- Added non-root production containers and a unified secret-safe `Movie CI`.
- Added exact-SHA homelab deployment with a clean checkout, health cutover, and
  rollback while preserving the legacy dirty checkout.
- Updated OKF concepts to implemented movie behavior and marked Android TV and
  TV/Sonarr work on hold.

## 2026-07-08

- Converted active repository knowledge to an OKF-first `docs/` bundle.
- Removed the previous non-OKF documentation tree after moving useful content
  into OKF concepts.
- Added OKF concepts for project scope, architecture, systems, APIs, integrations,
  data models, runbooks, decisions, backlog, references, and cleanup reporting.
- Added validation for the `docs/` OKF bundle.
