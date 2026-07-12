# Change Log

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
