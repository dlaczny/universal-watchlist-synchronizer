---
type: Report
title: TV Integration Rollout
description: Cumulative evidence ledger for the TV read model and staged reversible destination rollout.
tags:
  - report
  - tv
  - rollout
timestamp: 2026-07-29T00:00:00Z
version: 1.2.0
---

# Current Status

The schema-v2 reversible destination release is deployed and operating in
production at commit `57f23f0805dcd0e98703e39bfb9cd57e84641192`. The worker
performs scheduled Trakt-backed TV reconciliation for Sonarr and the Plex
Watchlist. Existing Sonarr rows retain `manual` ownership; worker-created rows
retain `worker` ownership. TV cleanup, terminal deletion, no-recycle-bin
deletion, and Trakt-history writes remain disabled.

# 2026-07-29 Reversible Destination Rollout

The production host reported the following redacted evidence. No protected
credential, raw upstream body, or personal watch-history payload was retained.

| Stage | Redacted production evidence | Outcome |
|---|---|---|
| Exact release | API and worker ran image/repository/state SHA `57f23f0805dcd0e98703e39bfb9cd57e84641192`; both containers were healthy. | Completed |
| Report-only review | Run 20 used generation `tv-20260729144411861-a91fdaa3f1190f24757fe8be13d9be61`: 21 Plex adds, 1 Sonarr season monitor, 16 Sonarr episode searches, 4 provider-uncertain skips, and 452 inactive skips. The only blocker was the expected `tv_apply_disabled`; there were no collection errors or delete/remove actions. | Completed |
| Existing-Sonarr adoption | The supervised adoption recorded 16 exact-TVDB Sonarr rows as `manual`; four exact-TVDB Sonarr rows created by the worker are recorded as `worker`. Manual rows were not mutated or made deletion-eligible by adoption. | Completed |
| Supervised reversible apply | Run 21 completed 21 Plex adds, 1 Sonarr season monitor, and 16 Sonarr searches. Four provider-uncertain Sonarr decisions and 452 inactive decisions were skipped. There were no blockers, collection errors, failures, or delete/remove actions. | Completed |
| External destination verification | Plex exposed all 21 intended exact-TVDB watchlist rows after the supervised apply. The four worker-created Sonarr series and the separately selected season were present and monitored. | Completed |
| Scheduled convergence | Scheduled run 22 succeeded after a successful movie run. Run 23 on the same generation reported 23 Plex `keep` decisions, 16 completed Sonarr search decisions, 4 provider-uncertain skips, and 452 inactive skips, with no blockers, collection errors, destination additions, failures, or removals. | Completed |
| Prohibited-operation guard | Live configuration retained `TRAKT_HISTORY_SYNC_APPLY=false`, all three TV cleanup/deletion gates false, and only `/app/data` mounted into the worker. | Completed |

The live scheduler now retains `TV_SYNC_ENABLED=true`,
`TV_SYNC_APPLY=true`, and `TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false`.

# 2026-07-24 Redacted Production Evidence

| Scope | Evidence | Release boundary |
|---|---|---|
| Production read model | Trakt connected; First complete generation published; 251/251 published shows had usable TVDB IDs. | The release remains read-only. |

Add dated, redacted evidence only after the corresponding real host operation.
The absence of a row is a stop condition, not permission to proceed.

# Required Reversible Destination Evidence

The stages below are ordered. Each row remains **Not run** until a human has
performed the actual server-local operation and recorded the required redacted
artifact. Keep `TRAKT_HISTORY_SYNC_APPLY` and all TV cleanup flags false at
every stage.

| Stage | Required evidence | Artifact to retain | Current status |
|---|---|---|---|
| Exact-SHA deployment with TV disabled | Deployer status, healthy movie workflow, `TV_SYNC_ENABLED=false` | Release SHA, redacted health/status metadata | Completed 2026-07-29 |
| Report-only TV collection | Schema-v2 generation and one `sync_tv.py --once` report with `TV_SYNC_APPLY=false` | Generation ID, report status, counts, selected-season/provider states, stable blocker/reason totals | Completed 2026-07-29 |
| Report review | Human approval that exact identity, collection, provider, and threshold facts permit the next stage | Review timestamp and approved report filename/hash; no secrets or raw bodies | Completed 2026-07-29 |
| Supervised existing-Sonarr adoption | One `--apply` run with both host apply and adoption gates true | Redacted action totals and audit evidence that only exact rows received `manual` origin | Completed 2026-07-29 |
| Adoption review | Human review of origin audit and destination outcomes before normal apply | Review timestamp and stable outcome/reason totals | Completed 2026-07-29 |
| Supervised reversible apply | One `--apply` run with adoption false | Redacted Sonarr/Plex Watchlist action totals, report status, and worker health | Completed 2026-07-29 |
| Second convergence pass | A fresh collection followed by a second supervised apply | Report showing only expected `keep`/`skip` decisions or a recorded investigation | Completed 2026-07-29 |
| Prohibited-operation guard | Configuration inspection showing history and cleanup flags false; no Plex library/media-root write surface | Redacted resolved configuration and boundary-test result | Completed 2026-07-29 |

Record only redacted timestamps, generation IDs, report status, counts, stable
reason codes, action outcomes, and review decisions. Do not record device/user
codes, access or refresh tokens, client secrets, protected ciphertext, sync
keys, Plex tokens, API keys, database connection strings, raw upstream bodies,
or personal media history.

# Historical Phase 1 Read-Model Evidence

| Evidence | Command or API call | Artifact to retain | Current status |
|---|---|---|---|
| Device connection established without logging tokens | Protected `POST /api/integrations/trakt/device/start`, then protected status read | Redacted response metadata and API log excerpt showing no secret | Recorded in redacted production evidence on 2026-07-24 |
| First complete generation published | Protected `POST /api/sync/tv` | Redacted result and `GET /api/export/tv/sync-state` envelope | Recorded in redacted production evidence on 2026-07-24 |
| Cursor-race rejection left the old pointer unchanged | Controlled integration test or supervised test account change during collection | Pointer-state comparison and rejected-run log category | Local test-validated 2026-07-22; production pending |
| Source failure left the old pointer unchanged | Controlled unavailable/malformed source test | Pointer-state comparison and fixed failure category | Local test-validated 2026-07-22; production pending |
| Provider failure published unknown rather than unavailable | Controlled TMDB provider failure | Generation provider state and redacted failure evidence | Local test-validated 2026-07-22; production pending |
| Legacy row quarantined or migrated deterministically | Migration startup with representative legacy data | Counts, stable migration reason, and no conflicting current row | Local test-validated 2026-07-22; production pending |
| All six TV mutation gates observed false | `docker compose ... config` and redacted worker/API environment inspection | Compose/config output with the six false values | Local configuration and deployment test-validated 2026-07-22; production pending |

# 2026-07-22 Local Release Gate

This evidence applies to commit `ee9f9e7` before the rollout-record commit.
It was collected in an isolated local worktree with a locally running MongoDB
test service. No external Trakt, TMDB, Plex, Sonarr, or production service was
contacted; no Compose stack was started.

| Check | Evidence | Outcome |
|---|---|---|
| Backend Release suite | `dotnet restore`, Release build, then `dotnet test backend/Watchlist.sln --configuration Release --no-build` | Passed: build had 0 warnings and 0 errors; 760 application and 74 API tests passed. |
| TV publish/key-ring focus | Focused Release test filter covering `TvSyncServiceTests`, `TvSnapshotValidatorTests`, `MongoTvGenerationRepositoryTests`, `DataProtectionTraktTokenProtectorTests`, `DataProtectionKeyRingHostedServiceTests`, `TraktConnectionServiceTests`, and `MongoTraktConnectionRepositoryTests` | Passed: 274 tests. The in-process fixtures retain no production identifier or token artifact. |
| Publish-last matrix | `TvSyncServiceTests` in the focused suite | Passed: successful source publishes; TMDB provider failure publishes `unknown`; Trakt source failure and pre/post activity-cursor change do not stage or publish; two hourly scheduled absences emit `tv:42:2:source_removed`; an activity generation leaves absence confirmations unchanged. |
| Key-ring recovery | Data-protection and connection-service tests in the focused suite | Passed: ciphertext survives provider restart with the same key ring; a different key ring produces sanitized `token_unreadable`/`refresh_required` state without changing stored ciphertext. No real OAuth flow was run. |
| Worker regression | `python -m pytest -q` and production-entrypoint `compileall` in `workers/vod-filter` | Passed: 133 tests and compilation. A targeted scan found no Sonarr or Trakt-history worker surface; existing Plex-watchlist calls are movie-only. |
| Deployment configuration | `tests/deployment/test_tv_phase1_deployment.py`; all three resolved Compose files with placeholder env files; Git Bash `bash -n scripts/deploy-movie-sync.sh` | Passed: 1 deployment test; all Compose files resolve. The six TV switches are hard-set to `false` in both backend and worker service environments. |
| Container images | Local `docker build` for API and worker, followed by image inspection | Passed: `watchlist-api:tv-phase1` and `watchlist-worker:tv-phase1` built. Both declare a non-root user and a healthcheck. |
| Secret and write-surface scans | Gitleaks v8.30.1 history scan from the primary checkout plus directory scan of a `git archive HEAD` publishable tree; true-gate and report-name scans | Passed: 197 history commits and 2.87 MB publishable tree scanned with no leaks. No `mutationCapable=true` or true TV apply/adoption assignment was found. `docs/reports` contains no sensitive-name references. |

The direct `/api/integrations/trakt/*` and `/api/sync/tv` runtime checks have
been recorded in the dated redacted production evidence above. This does not
authorize any destination mutation.

# Recording Rules

Artifacts may contain timestamps, stable reason codes, counts, and non-secret
generation identifiers or publish pointers. They must not contain Trakt
device/user codes, access/refresh tokens, client secrets, protected ciphertext,
sync keys, Plex tokens, database connection strings, authorization headers, or
raw private upstream payloads. A successful read-model deployment does not
authorize any future Plex, Trakt-history, Sonarr, or Plex-watchlist mutation
phase.

# Links

- [TV Sync Operations](../runbooks/tv_sync_operations.md)
- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
- [Approved TV Design](../superpowers/specs/2026-07-13-tv-show-integration-design.md)
