# Change Log

## 2026-07-13

- Reviewed the TV integration design and decomposed it into an ordered,
  test-driven five-phase implementation program covering the read model,
  Plex-history-to-Trakt delivery, reversible destinations, concluded-season
  cleanup, and terminal-series cleanup/revival. The review made apply a hard
  environment gate, separated freshness from semantic hashes, and required new
  current-manifest authorization for crash-recovery calls. It also requires a
  non-extending backend claim revalidation immediately before every Sonarr
  cleanup mutation so newly pending Plex-to-Trakt routing cancels stale
  authority. No implementation or TV production mutation has started.
- Recorded the approved design for Trakt-backed TV membership and progress,
  Plex-history-to-Trakt synchronization, caught-up Plex watchlist lifecycle,
  guarded Sonarr season and terminal cleanup, and Poland-specific provider
  availability. Production TV mutation remains disabled.
- Released watched lifecycle handling from exact SHA `63fbf58` after local
  release validation, two clean Gitleaks scans, and successful `Movie CI` run
  29226820025.
- Established a 317-movie Radarr baseline from a non-empty 289-movie Letterboxd
  source, with zero watched authorizations, conflicts, collection errors,
  removals, or file-deletion decisions.
- Enabled the mode-`0600` host-only watched-file gate and completed armed report
  30 with a fresh successful heartbeat and no destructive decisions.
- Recorded that the destructive path is test-covered and armed but not yet
  live-exercised because no real watched transition occurred during rollout.

## 2026-07-12

- Implemented publish-last Letterboxd active/watched/reactivated lifecycle
  state, coherent watched export, durable Radarr observations, exact watched
  and manual Plex cleanup authorization, default-off watched file deletion,
  cleanup audit history, and report fields. Production baseline and supervised
  rollout remain pending; pre-feature watched history is not backfilled.
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
