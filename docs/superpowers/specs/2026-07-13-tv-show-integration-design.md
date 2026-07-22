---
type: Backlog
title: Trakt, Plex, Sonarr, And Polish Availability TV Integration Design
description: Approved design for Trakt-backed TV discovery and progress, Plex history synchronization, guarded Sonarr cleanup, Plex watchlist lifecycle, and Poland-specific provider availability.
tags:
  - tv
  - trakt
  - plex
  - sonarr
  - tmdb
  - lifecycle
  - availability
timestamp: 2026-07-13T00:00:00Z
version: 0.4.0
---

# Status

This document is the reviewed and conversationally approved design for the TV
and Sonarr integration. It authorizes the linked implementation planning
program, but it does not by itself authorize code changes, production writes,
or destructive behavior. Those remain held behind task-by-task execution,
validation, rollout gates, and supervised production evidence.

The existing Letterboxd, Radarr, and movie synchronization path remains
unchanged. TV automation must remain disabled until its implementation, tests,
operator-visible reports, durable OKF updates, rollout gates, and supervised
production validation are complete.

The ordered execution plans are indexed by
[TV Integration Program](../plans/2026-07-13-tv-integration-program.md).

## Phase 1 Implementation Status

The non-destructive backend portion of Phase 1 is implemented and test
validated: protected Trakt device OAuth, complete published TV generations,
legacy migration, PL provider observations, browse/detail/status routes, and a
read-only export. Its operational contract is [TV Sync Read Model](../../architecture/tv_sync_read_model.md).
It has not been claimed as a production rollout in this document; evidence is
recorded only in [TV Integration Rollout](../../reports/tv_integration_rollout.md).

Android-specific work is deferred in the [Android TV Integration Backlog](../../backlog/android_tv_tv_integration.md)
and may resume only after an explicit user request. The remaining Plex history,
Trakt-write, Sonarr, Plex-watchlist, and deletion phases remain unimplemented
and all TV mutation gates remain false.

# Goal

Use Trakt as the TV source of truth and provide one coherent TV workflow:

- import shows from the Trakt watchlist;
- retain shows whose Trakt progress says aired episodes remain unfinished;
- read completed episode plays from one configured Plex account and TV library;
- deliver each Plex play to Trakt with durable effectively-once processing;
- show watched and aired progress, next episodes, and Polish streaming
  availability in the application;
- remove a caught-up show from the Plex watchlist while keeping a continuing
  series monitored in Sonarr;
- restore the Plex watchlist entry when a new aired episode becomes unwatched;
- delete the files for a concluded, fully watched season and unmonitor that
  season;
- remove a fully watched series and its files from Sonarr only when Trakt says
  the show is ended or canceled and every destructive safety gate passes; and
- detect a later revival and restore normal Sonarr management.

Trakt remains the external authority for membership, viewing progress, and
production status. MongoDB is still required as the complete-snapshot read
model, durable event and outbox store, safety ledger, and audit source. The
application must never try to reproduce this design by querying Trakt live from
Android or by keeping only an in-memory cursor.

# Non-Goals

- Changing the production movie source or movie lifecycle.
- Deleting media through the Plex library API.
- Using title matching to authorize a Sonarr or Plex mutation.
- Treating Trakt WatchNow as the provider-availability authority.
- Automatically deleting a Sonarr series merely because an unstarted show was
  manually removed from the Trakt watchlist.
- Automatically managing season 0 or searching for specials.
- Supporting multiple Trakt users in the first implementation.
- Reconstructing Plex plays that the configured Plex server no longer exposes.
- Synchronizing a Plex Mark Unwatched action back into Trakt history; it only
  invalidates cleanup evidence in this version.
- Replaying pre-cutover Plex rewatches merely to reproduce historical Trakt
  play counts.
- Enabling destructive behavior as part of the initial deployment.

# Chosen Approach

The implementation uses a hybrid backend-and-worker design.

| Concern | Authority or owner |
|---|---|
| TV watchlist, watched progress, and show status | Trakt |
| TV identity, artwork, and provider metadata | TMDB cached by the backend |
| Plex episode play ingestion and Trakt history writes | .NET backend |
| Complete TV snapshots and lifecycle decisions | .NET backend plus MongoDB |
| Sonarr and Plex watchlist collection and mutation | Python TV worker |
| Destination ownership, action audit, and run lease | Worker SQLite |
| Browse and detail contracts | Backend projections from MongoDB |
| User interface | Read-only Android client of the backend |

The worker consumes only GET /api/export/tv/sync-state and protected cleanup
claim/result endpoints. It does not read MongoDB directly. The backend decides
source-side lifecycle eligibility and publishes stable cleanup event IDs. The
worker independently collects fresh destination state, applies destination
safety policy, and executes only a successfully claimed action.

This preserves the established system boundary: integration secrets and
normalized data belong to the backend, while host-local Sonarr and Plex
watchlist credentials and mutations belong to the worker.

# Superseded TV Behavior

This design supersedes the dormant TMDB-account TV importer as TV membership
authority. TMDB remains an enrichment source only.

The following current behavior must not survive the migration:

- a missing or empty TMDB TV response hard-deleting TV rows;
- writing TV data independently to both watchlist_items and a new TV aggregate;
- dropping a fetched TVDB ID before persistence;
- marking a TV row enriched without fetching TV provider data;
- interpreting unavailable provider data as Not released;
- returning an empty GET /api/export/sonarr/tv as if it were production desired
  state; or
- allowing the combined sync response to hide a partial TV failure.

The migration also removes Android's direct call to the protected availability
refresh route, adds explicit API handling for typed TMDB parse failures, adds a
backend-DTO-to-Android contract check to CI, and rejects or repairs seed TV rows
whose external identities disagree. These are prerequisites for exposing the
new TV read model, not unrelated refactors.

The new tv_shows collection is the sole TV aggregate. GET /api/watchlist
projects TV rows from tv_shows into the common browse DTO. Existing TV rows in
watchlist_items are migrated once without granting their old source any
disappearance or deletion authority. No two collections may independently
write lifecycle state for the same show.

The legacy POST /api/sync/tmdb/tv route remains disabled during migration and
is later removed or retained only as an explicitly non-authoritative metadata
tool. The empty GET /api/export/sonarr/tv endpoint is compatibility-only and
must never become the worker's desired-state contract.

# Identity Rules

The canonical source key is the Trakt show ID. The backend persists all
available Trakt, TVDB, TMDB, and IMDb identifiers, but destination mutation
requires one unambiguous TVDB ID.

Identity rules are:

1. Resolve a Trakt show to TVDB through Trakt IDs and verify it against TMDB
   external IDs when those IDs are available.
2. Resolve Plex shows from nested GUIDs. Accept exact TVDB first and exact TMDB
   or IMDb only as evidence for a verified mapping to the same TVDB show.
3. Resolve Sonarr by its stored TVDB ID.
4. Quarantine missing or conflicting identifiers. Keep the show visible with
   an identity warning, but do not add, remove, monitor, unmonitor, or delete
   anything for it.
5. Never use title, normalized title, year, season title, or fuzzy matching to
   authorize a mutation.

Every cleanup event binds the canonical Trakt ID and exact TVDB ID. The worker
must verify that the current Sonarr series still has that TVDB ID immediately
before mutation.

# Trakt Source Model

## Account Connection

The first version supports one Trakt account through OAuth 2.0 device
authorization.

The backend provides protected administrative routes:

| Endpoint | Purpose |
|---|---|
| POST /api/integrations/trakt/device/start | Start device authorization and return the user code, verification URL, polling interval, and expiry. |
| GET /api/integrations/trakt/status | Return disconnected, pending, connected, refresh_required, or revoked without exposing tokens. |
| DELETE /api/integrations/trakt/connection | Revoke local use, erase stored tokens, and freeze TV lifecycle mutation. |

These endpoints use the existing sync-key protection and are not called by the
read-only Android client. The backend polls the device token endpoint at the
server-specified interval and persists the connection only after success.

Trakt client credentials stay in host configuration. Access and refresh tokens
are encrypted before MongoDB persistence with ASP.NET Data Protection. The
production backend receives a dedicated, protected, persistent Data Protection
keyring mount because its root filesystem is read-only. Losing the keyring is
treated as a revoked/unreadable connection, never as permission to reset state.

## Tracked, Watch-Now, And Dormant Sets

The backend fetches all pages of:

- the Trakt show watchlist;
- GET /sync/progress/watched with hide_completed false,
  hide_not_completed false, and only_rewatching false so completed shows stay
  included; and
- detailed GET /shows/{id}/progress/watched for changed and managed shows.

For an active season or any cleanup candidate, the backend also reads
GET /shows/{id}/seasons/{season}?extended=full so episode air dates and the
known season schedule are independent of watched-progress counts.

For every tracked show it also reads
GET /shows/{id}/seasons/0?extended=full and persists canonical positive
episode identities in a separate identity-only list. This exists solely so a
configured-account Plex S00 play can be resolved exactly in Phase 2. Specials
never enter watched progress, aired/completed totals, provider claims,
automatic search, or cleanup semantics.

The tracked catalog is the union of the current watchlist, all returned watched
progress rows, and previously retained lifecycle rows. Within that catalog:

- a show explicitly in the current watchlist is active, including a deliberate
  re-add for a rewatch;
- a show with completed less than aired is active even after Trakt
  automatically removes it from the watchlist after its first watched episode;
- a show with aired greater than zero and completed equal to aired is caught
  up only when it is not explicitly in the current watchlist;
- a previously managed show absent from both watchlist and progress is
  source_removed only after two consecutive complete hourly snapshots agree;
  and
- a source_removed row remains in MongoDB for history, leaves the active UI and
  Plex desired set, and cannot by itself authorize a Sonarr or file deletion.

Current watchlist membership is the stronger explicit user signal. It keeps the
show active and Plex-desired and blocks season or whole-series cleanup. In the
normal Trakt flow the show leaves its built-in watchlist after the first watched
episode, so unfinished progress then keeps it active and completion makes it
caught up.

The backend never requests only unfinished shows. Completed progress must stay
visible so caught-up, terminal, and revival transitions can be detected.

## Collection Schedule

The source collector runs as follows:

- every five minutes, read GET /sync/last_activities;
- when relevant watchlist or watched activity changes, perform an early
  complete TV refresh;
- every hour, perform a complete refresh regardless of activity so a newly
  aired episode changes progress even when the user has done nothing;
- refresh detailed progress for every changed, active, caught-up, terminal
  candidate, and retired show needed for a safe decision; and
- refresh full show metadata, including Trakt production status, daily for
  managed and retired shows; and
- refresh full show metadata again in every scheduled generation while a show
  is a terminal candidate, using extended=full so two qualifying generations
  never rely on one stale status lookup.

A refresh reads the activity marker before and after pagination. If relevant
activity changes while pages are being collected, the generation is discarded
and retried. The stored activity cursor advances only after successful
publication.

The exact Trakt terminal strings are ended and canceled. No other value,
including returning series, continuing, planned, upcoming, pilot, or in
production, is terminal.

# Complete Snapshot Publication

MongoDB deployments do not require multi-document transactions, so TV uses a
publish-last generation protocol:

1. Serialize source refreshes.
2. Collect every required Trakt page and attach the latest completed
   configured-account Plex history watermark and capability state.
3. Validate pagination, uniqueness, identity shapes, count invariants, and the
   pre/post activity marker.
4. Write generation-scoped TV rows, progress, Plex evidence, lifecycle events,
   and proposed cleanup events.
5. Write an immutable manifest only after every publication-critical write
   succeeds.
6. Advance the published TV pointer last.

Readers resolve all TV rows through one published generation and never combine
generations. A failed or abandoned generation is invisible to browse, export,
and cleanup.

Each manifest records:

- generation ID and previous published generation ID;
- kind, including scheduled_full or activity_full;
- started, completed, and published UTC timestamps;
- Trakt activity cursor before and after collection;
- request filter/version information;
- page and item counts for watchlist and progress;
- deterministic hashes of canonical membership and progress;
- Plex history watermark and collection time;
- enrichment freshness and errors;
- validation result and redacted failure reasons; and
- stable lifecycle and cleanup event IDs included in the generation; and
- mutationCapable plus the exact health reasons that made it true or false.

Trakt watchlist, progress, identity, or OAuth failures are
publication-critical. They preserve the last good manifest and freeze TV
mutation. A complete Trakt browse generation may publish while Plex history is
unavailable or bootstrap is incomplete, but it is marked mutationCapable false,
does not advance cleanup candidates, and cannot be used by the worker for any
mutation. TMDB provider failure does not hide otherwise valid TV source state,
but provider status becomes unknown or stale and cannot support an unavailable
claim.

An empty result is not assumed to be valid disappearance. Two distinct,
consecutive scheduled_full hourly generations must confirm an empty or
individual disappearance before membership is retired softly. Rapid retries
and activity-triggered generations do not satisfy that rule. Empty-source
confirmation never authorizes file deletion.

# MongoDB Model

## tv_shows

The collection stores one immutable row per generation ID and Trakt show ID.
The published manifest selects the only generation visible to readers; failed
staged rows cannot overwrite the previous view. Superseded generations are
retained for a bounded audit window and then pruned only after they are no
longer referenced by a lifecycle or cleanup event.

Each generation-scoped row contains:

- Trakt, TVDB, TMDB, and IMDb IDs plus identity verification status;
- title, year, artwork, overview, and Trakt production status;
- current watchlist membership and source timestamps;
- aired and completed totals, last watched episode, next episode, and
  per-season/per-episode progress;
- primary lifecycle state and last lifecycle event;
- per-season cleanup state;
- current terminal candidate state;
- Plex show and episode mappings for the configured account and library;
- Poland provider results and freshness;
- latest published generation ID; and
- migration provenance for a legacy TV row.

The primary lifecycle state is active, caught_up, source_removed,
terminal_cleanup_pending, or retired_terminal. Reactivated is an audit event,
not a permanent state. A reactivated show immediately settles to active when an
aired unwatched episode exists, or caught_up when only a nonterminal status
reversal exists.

Season cleanup is an orthogonal per-season state because an active show can
have an older watched season pending cleanup. The API may show a
season_cleanup_pending badge without replacing the show's primary lifecycle
state.

## tv_sync_manifests

This collection stores immutable complete-generation manifests and the
published pointer used by browse and export.

## plex_watch_events

This collection is the durable, deduplicated ledger of Plex episode plays. Its
primary unique key is:

Plex machine identifier + configured account ID + Plex history event key.

When an older Plex response has no stable history key, the complete fallback
key is Plex machine identifier + configured account ID + episode rating key +
viewedAt normalized to a whole UTC second. A unique index prevents overlapping
polls from creating a second event. Each ledger row is accepted or quarantined
and independently carries a nullable bootstrap outcome: reconciled,
superseded, selected_for_delivery, or not_applicable. Bootstrap never replaces
the accepted/quarantined disposition. A conflicting key or identity is
quarantined with a stable reason and blocks mutation for the affected show.

Accepted rows also have an orthogonal nullable post-cutover routing state.
Rows first inserted after a completed cutover start pending regardless of
their viewedAt, so late overlap arrivals are not lost. A separate idempotent
router creates deterministic outbox ID `trakt-history:{eventId}` and then CAS
transitions pending to enqueued. An exact existing row recovers a crash between
those writes; a conflicting payload blocks. Local-evidence-only specials
transition pending to not_applicable without an outbox. Bootstrap outcome and
post-cutover routing state cannot both be set.

An exactly identified season-0 play is accepted as configured-account local
evidence with delivery mode local_evidence_only_special. It is never sent to
the Trakt outbox in the first implementation. Missing or conflicting special
identity remains quarantined.

Each legitimate post-bootstrap Plex rewatch is a distinct history event and is
delivered to Trakt so later Trakt rewatch counts remain accurate.

## trakt_history_outbox

One outbox row corresponds to one accepted Plex history event selected for
delivery. States are pending, leased, confirmed, ambiguous, retry_wait, and
dead_letter. It stores exact episode identity, viewedAt, attempt metadata, a
redacted receipt, and the next permitted attempt time.

## tv_lifecycle_events

This append-only audit contains added, caught_up, reactivated, source_removed,
season_candidate_started, season_cleanup_authorized,
season_cleanup_completed, terminal_candidate_started,
terminal_cleanup_authorized, terminal_cleanup_completed,
cleanup_canceled, and destination_drift events.

Every event has a stable ID, lifecycle version, published generation ID,
predicate hash, UTC time, and redacted reason data.

## tv_cleanup_authorizations

Lifecycle events remain immutable. This separate mutable projection provides
the one-use operational state for a cleanup intent. The initial document uses
the lifecycle event ID as its authorization ID and stores pending, leased,
converged, canceled, or expired status; the latest eligible manifest and
predicate hash; a 30-minute expiresAt; worker and lease IDs; target binding; and
redacted child-action results.

Before a claim, a later complete mutation-capable manifest may refresh the
projection's manifest binding and expiry while the same lifecycle event stays
eligible. Once claimed, canceled, or converged, it cannot be silently renewed.
An expired lease is never reused. Crash recovery creates a separate explicit
authorization document with a new authorization ID, a link to the expired
authorization, the immutable original target, and a current mutation-capable
manifest. Reconciliation-only recovery can report an already-absent target but
cannot call Sonarr; any new Sonarr call requires a separately gated retry
authorization.

# Plex History To Trakt

## Plex Collection

The backend polls only the configured Plex account and configured TV library
every five minutes. It uses the Plex server machine identifier, account ID,
library section ID, history key, show rating key, episode rating key,
season/episode numbers, and viewedAt time.

The initial deployment performs a complete paginated backfill of all history
the server exposes for that account and library. The ledger retains every
accessible event, but bootstrap synchronizes watched state rather than replaying
years of rewatch counts:

1. Fetch current Trakt watched progress/history before creating bootstrap
   outbox rows.
2. Group Plex backfill events by exact episode.
3. If Trakt already marks the episode watched, mark its historical Plex events
   bootstrap_reconciled without a Trakt write.
4. If Trakt does not mark it watched, enqueue only the latest accepted Plex play
   for that episode, mark it selected_for_delivery, and mark older plays
   bootstrap_superseded.
5. Mark accepted local-evidence-only specials not_applicable without an outbox
   row.
6. Record bootstrapCutoverAt and the final Plex watermark.

Backfill is rate-limited and must finish successfully before any destructive TV
cleanup can be enabled. After cutover, every new accepted Trakt-eligible Plex
event, including a rewatch, is independently eligible for the outbox; accepted
local-evidence-only specials remain ledger evidence only. Collection uses a durable
watermark with a 24-hour overlap to catch late or reordered events. The
watermark advances only after every fetched event is durably stored.

Each run orders collection, any required bootstrap, post-cutover routing, then
delivery. Routing runs even while Trakt writes are disabled. Pending or
conflicting routing freezes mutation just like unresolved outbox work.

Plex Play History availability is a required capability for this feature. If
the server/account cannot expose it, including because the required Plex
subscription capability is unavailable, health reports the limitation and TV
mutation freezes. Watched state is not inferred from another Plex account.

An event with missing or conflicting external show identity is quarantined. It
is visible in status and never sent using a title match.

## Outbox Delivery

One per-account Trakt operation coordinator serializes source generation and
history writes. A complete source refresh holds the generation lease from its
first activity read through manifest publication, so the application's own
history POST cannot change last_activities during pagination. An outbox worker
obtains the same exclusive lease for one write, releases it promptly, and then
requests a complete progress refresh.

The backend sends one Plex event per POST /sync/history request unless a future
Trakt contract proves per-item acknowledgement for batching.

Delivery rules are:

1. Atomically lease one pending row.
2. Refresh OAuth if necessary before the write.
3. Submit the exact TVDB/Trakt show identity, season, episode, and Plex
   viewedAt timestamp.
4. On a definite success, persist the Trakt receipt and mark confirmed.
5. On a definite pre-write authentication failure, refresh and retry according
   to policy.
6. On HTTP 429, honor Retry-After and serialize further Trakt writes.
7. On a timeout, connection loss, or ambiguous server failure, mark ambiguous
   and do not retry blindly.
8. Leave an ambiguous event quarantined for at least 15 minutes, then reconcile
   it twice against Trakt history on separate polls using exact episode
   identity and the submitted timestamp normalized to a whole UTC second.
   Confirm exactly one match. More than one match requires operator review.
   Retry only after both completed reconciliations find no match.

Completed outbox rows are never recreated if a user later edits Trakt history.
Only a new Plex history event creates a new Trakt play. Trakt does not provide
an idempotency key for this non-idempotent endpoint, so an indefinitely delayed
remote commit makes mathematical exactly-once delivery impossible. The durable
ledger, quarantine interval, repeated reconciliation, and no-blind-retry rule
provide effectively-once behavior and surface any detected duplicate.

Outbox leases recover after process failure. Retryable rows use bounded
exponential backoff. Authentication revocation, identity conflicts, and
exhausted attempts are operator-visible; no row disappears silently. Any
pending, ambiguous, or dead-letter play for a show blocks cleanup for that show.

After a confirmed Trakt history write, the backend refreshes that show's
progress and publishes a complete generation before it can become caught up or
cleanup-eligible.

# TV Lifecycle

## Show-Level Transitions

| Input | Result |
|---|---|
| In current watchlist, regardless of existing progress | active |
| Not in watchlist and completed less than aired | active |
| Not in watchlist, aired greater than zero, and completed equals aired | caught_up |
| Caught-up show gains a newly aired unwatched episode | active plus reactivated event |
| Terminal cleanup predicates enter grace | terminal_cleanup_pending |
| Terminal Sonarr target converges absent | retired_terminal |
| Retired show gains an unwatched aired episode | active plus reactivated event |
| Retired or caught-up show is explicitly re-added to the watchlist | active plus reactivated event |
| Retired show changes to nonterminal without an unwatched aired episode | caught_up plus reactivated event |
| Absent from both watchlist and progress in two hourly full generations | source_removed |

For active shows, the worker keeps or adds the show in Sonarr and keeps the
series monitored. It puts the show on the Plex watchlist when it is an
explicit Trakt watchlist item or has at least one aired unwatched episode.

For caught-up continuing shows, the worker removes only an app-owned or adopted
Plex watchlist entry. It leaves the Sonarr series monitored and configured to
monitor new seasons. Catching up in the middle of an airing season does not
authorize season file deletion while either source knows about a future
episode.

A production-status reversal out of ended or canceled restores or re-monitors
the Sonarr series. It does not by itself restore Plex watchlist membership.
Plex is restored when explicit current watchlist membership or an aired
unwatched episode exists. This preserves the rule that a caught-up show with no
released episode stays off the Plex watchlist unless the user deliberately
re-adds it for a rewatch.

Retired and source-removed rows remain eligible for daily status and episode
refresh so revival is detectable.

For a source_removed show, the worker removes an owned or adopted Plex
watchlist entry but makes no Sonarr change in the first version. In particular,
source removal does not unmonitor or delete an existing series or its files.
A destination that was never owned or adopted is reported but not changed.

## Candidate Continuity

Seven days means seven days of observed eligibility, not merely wall-clock age
since the first candidate. Scheduled full generations are expected hourly. A
candidate accumulates only the interval between two consecutive successful,
mutation-capable scheduled generations when that interval is no more than two
hours and every predicate remains true.

A gap longer than two hours, a scheduled generation with mutationCapable false,
an unavailable configured-account Plex verification, or a contradictory
predicate resets the candidate and appends cleanup_canceled. An activity-driven
generation can cancel a candidate immediately when it finds a contradiction,
but it does not advance the seven-day observation duration. This prevents an
outage from aging a candidate into immediate deletion on recovery.

## Destination Adoption

The worker owns a destination row when it added that exact TVDB show or when
the operator explicitly adopted an existing exact match during rollout.
Ownership is persisted in SQLite with the Sonarr series ID or Plex watchlist
identity and the source lifecycle version.

TV_SYNC_ADOPT_EXISTING_DESTINATIONS defaults to false. Report-only rollout
lists exact-TVDB adoption candidates. When the operator enables adoption after
review, the worker records those exact existing Sonarr series and Plex
watchlist rows as managed.

Reversible additions may converge an already-present exact row and record
adoption only under that gate. Plex removals and every destructive Sonarr
action require existing ownership or adoption. A cleanup event alone does not
silently claim an unrelated manually managed series.

# Sonarr Desired State

An eligible active show is looked up and added by exact TVDB ID with the
configured root folder, quality profile, language profile where required, and
series type. The series is monitored and Monitor New Seasons remains enabled.

The worker does not trigger a broad search that would redownload watched
history. It maps Trakt progress to exact Sonarr episodes:

- fully watched concluded numbered seasons are unmonitored after cleanup;
- a season with an unwatched or future regular episode remains monitored;
- searches target only exact aired, unwatched episode IDs;
- an unstarted watchlist show may search all aired regular episodes because
  they are all unwatched; and
- specials remain unmonitored and are not automatically searched in the first
  implementation.

If a cleaned season later receives a newly known episode, its cleanup state is
canceled, that season is monitored again, and the exact aired unwatched episode
becomes search-eligible.

# Configured-Account Watched Evidence

Deletion requires both Trakt completion and Plex evidence for the configured
account. For each local episode, Plex evidence means:

- the durable ledger contains at least one accepted completed play for the
  exact episode and configured account; and
- a fresh account-scoped Plex library read reports the episode currently
  played, such as viewCount greater than zero.

The play itself may be old; freshness applies to the verification read. If the
user marks an episode unwatched in Plex, the current account-scoped played state
becomes false, the evidence fails, and any season or terminal candidate resets.
An account ambiguity, missing per-user view state, or disagreement between the
ledger and current Plex state fails closed.

Every episode linked to a multi-episode file must satisfy this rule. The only
vacuous case is terminal removal of a series whose complete Sonarr and
filesystem inventories prove that it has zero local episode media files.

# Season Cleanup

A numbered season greater than zero becomes a cleanup candidate only when all
of these predicates are true:

- at least one regular episode has aired;
- detailed Trakt progress says completed equals aired for that season;
- Trakt has no known future or unaired regular episode in that season;
- Trakt next_episode does not point to that season;
- a fresh complete Sonarr collection has no future or unknown-air-date regular
  episode in that season;
- every Sonarr episode file maps exactly to one or more episodes and every
  linked episode is watched by the configured Plex account;
- the latest configured-account Plex verification is successful and no more
  than 30 minutes old;
- no Trakt outbox row for the show is pending, leased, ambiguous, retry_wait,
  or dead_letter;
- no accepted post-cutover Plex event for the show is pending routing or has a
  routing conflict;
- no plex_watch_events row for the show is quarantined;
- the show and Sonarr series are an exact TVDB match and the series is owned or
  adopted; and
- the show is not explicitly present in the current Trakt watchlist.

The first qualifying scheduled_full generation records candidateSince and zero
eligibleObservedDuration. The predicates must remain true until the common
continuity calculation accumulates seven complete days. An unwatch, new
episode, changed air date, identity conflict, explicit watchlist membership,
stale evidence, or source disagreement cancels and resets the candidate.

After the grace period, the backend publishes one season cleanup event. The
worker claims it, refetches the latest backend TV export, collects live Sonarr
and Plex library/watchlist state, verifies the backend's Plex-history evidence
and watermark, and rechecks every predicate. The worker does not call Trakt or
take ownership of Plex-history ingestion. The season authorization and its
published manifest must both be current and no more than 30 minutes old.

The worker deletes each exact Sonarr episode-file record for the season through
Sonarr with file deletion enabled. A file representing multiple episodes is
deleted only when every linked episode is watched. It then updates that season
to monitored false while leaving the series monitored and Monitor New Seasons
enabled.

Successful child-file actions are audited immediately. A partial failure
retries only unresolved files from fresh state. A season with no files can
still converge by becoming unmonitored, but it counts as one season cleanup for
policy purposes.

# Terminal Whole-Series Cleanup

## Source-Side Predicates

A show can enter terminal grace only when two distinct consecutive
scheduled_full hourly generations, not rapid retries, both prove:

- Trakt status is exactly ended or canceled;
- aired is greater than zero;
- completed equals aired;
- Trakt reports no next episode;
- there is no explicit current Trakt watchlist membership;
- exact TVDB identity is verified; and
- no pending/conflicting post-cutover route, unresolved Plex-to-Trakt outbox
  event, or quarantined Plex watch event exists for the show.

The candidate must then accumulate seven complete days of eligible observed
duration under the common two-hour continuity rule. Any changed predicate
cancels the candidate and invalidates its cleanup event.

## Worker Live Gates

Before the backend will lease a terminal cleanup event, and again immediately
before the Sonarr call, the worker must prove:

- the published manifest is less than 30 minutes old and is still the current
  published generation;
- the current Sonarr show has the authorized TVDB ID and expected Sonarr
  series ID;
- Sonarr also reports no next airing; unknown or disagreement fails closed;
- the series is owned or explicitly adopted;
- the configured Plex account and TV library were collected successfully;
- every local numbered-season file maps to watched episodes;
- every downloaded special is watched by the configured Plex account;
- no episode file has an unknown or conflicting mapping;
- a read-only filesystem verification accounts for every media file under the
  Sonarr series path;
- Sonarr's recycle-bin path is configured, unless the separate irreversible
  override is explicitly enabled; and
- the final plan stays within the destructive run caps.

The backend's seven-day terminal evaluator is source-scoped. It never treats a
season observation as whole-series evidence. Publishing terminal deletion
permission additionally requires a fresh, complete show-level worker
observation that binds the exact Sonarr series and accounts for every numbered
season file and downloaded special.

An unwatched downloaded special blocks whole-series deletion. Season 0 is
never independently cleaned.

A series with zero local episode files may still be removed when the complete
Sonarr and read-only filesystem inventories both prove there are no untracked
media files. In that case configured-account Plex watched evidence is
vacuously complete, but the successful fresh Plex collection is still
required.

The worker needs a read-only mapped view of every Sonarr TV root for terminal
verification. If no safe path mapping is configured, terminal deletion remains
blocked. Known non-media sidecars are included in the audit; an untracked media
file or an unknown subdirectory blocks deletion.

## Sonarr Call

The terminal mutation is:

DELETE /api/v3/series/{id}?deleteFiles=true&addImportListExclusion=false

Keeping addImportListExclusion false permits a rare revival to be re-added.
The application will not ordinarily re-add a retired terminal show, but an
independent Sonarr import list could do so. Such a reappearance creates a drift
alert and is not deleted again without a new lifecycle authorization.

The worker verifies that the series is absent after the call. Terminal cleanup
suppresses individual season cleanup for the same show in that run. If
terminal cleanup is not yet authorized, an independently eligible older season
may still be cleaned.

# Cleanup Authorization And Crash Recovery

Every season or terminal cleanup intent is represented by one immutable
tv_lifecycle_events record that binds:

- action type;
- Trakt and TVDB show IDs;
- season number when applicable;
- published source generation;
- lifecycle version and predicate hash;
- candidate start and authorization time;
- configured-account Plex evidence time, nullable history watermark,
  collection complete/success flags, and binding-wide distinct observed-event
  count;
- expected source progress; and
- the eligibility facts that created the intent.

The watermark may be null only when the bound collection is complete and
successful and its binding-wide observed-event count is exactly zero. A
positive count requires a watermark; any other null combination rejects the
intent before publication.

Cancellation, expiry, leasing, and completion append lifecycle facts and update
the separate tv_cleanup_authorizations projection. The export exposes pending
authorizations, but an immutable event is not permission to call Sonarr until
its current projection is atomically claimed through:

- POST /api/worker/tv/cleanup-authorizations/{eventId}/claim; and
- POST /api/worker/tv/cleanup-authorizations/{authorizationId}/result.

The claim includes worker ID, manifest ID, action type, Sonarr series ID,
expected episode-file IDs or terminal path fingerprint, and the worker's live
predicate hash. The backend grants one 10-minute lease only while the
authorization remains current, unexpired, uncanceled, and its manifest is no
more than 30 minutes old. A new claim requires pending state; an exact
same-worker replay may revalidate the one already leased projection. Result reporting records each child
action and marks the authorization converged only when the target has
converged. The live predicate hash contains stable semantic facts only;
freshness timestamps are validated and audited separately so a safe final
recollection can match the initial semantics.

Every claim reads current Phase 2 history health in addition to the immutable
manifest. For mutation-bearing initial or retry claims, a pending/conflicting
post-cutover route, unresolved outbox row, or quarantined Plex event atomically
cancels an active pending projection. If already leased, it immutably marks
mutation revoked, retains the original lease only for result/audit recovery,
and rejects further external-action permission. A strictly audit-only
reconcile-only recovery may be created while the blocker remains, can report
only independently confirmed absence/completion, and never grants a Sonarr
call. The exact same worker may
replay the exact claim as a pre-action live revalidation; it receives the same
lease without an expiry extension only while every gate still passes. The
worker performs that replay immediately before every Sonarr cleanup mutation.
A canceled, revoked, or changed revalidation stops before the external call. This makes
new routing blockers effective even when they appear after the authorizing
manifest was published.

Each claim response includes server `leaseIssuedAt`, current
`leaseValidatedAt`, and fixed `leaseExpiresAt`. The worker computes conservative
remaining time from validated-to-expiry using the monotonic timestamp captured
before the request, then retains the minimum deadline for that authorization/
lease across replays. A replay can only reduce the local budget; it cannot reset
ten minutes. A restarted process derives a new conservative remainder from the
server's current validation time because a host monotonic absolute value is not
portable across boots.

Mutation revocation does not discard an action that already occurred. Before
the original lease expires, the result endpoint still accepts the exact
same-worker append-only child audit and may record factual convergence from
strict postconditions; that channel grants no new call. After expiry, a new
reconcile-only authorization can record independently confirmed absence while
the blocker remains. Retrying a still-present target requires the blocker
resolved, a new current manifest, all gates, and a distinct retry authorization.

The worker also holds a single-run SQLite lease, so overlapping local
processes cannot bypass volume caps, and renews it throughout collection and
execution. Before every Sonarr mutation it also requires sufficient remaining
backend lease time. If a process crashes after a Sonarr action but before
result reporting, the old lease expires and is never extended. The next run
recollects live state and obtains a current recovery authorization: an already
absent file or series is reported without a new Sonarr call, while a still-
present original target requires full current gates and an explicit retry
authorization.

SQLite retains the destination ownership record, live observations, event and
authorization IDs, recovery link/mode, lease ID, exact strict query booleans,
per-child results, and redacted failures.

# Plex Watchlist Lifecycle

The Plex library remains read-only. The worker mutates only the Plex universal
watchlist:

- add an owned/adopted exact show for any explicit Trakt watchlist item or an
  active show with an aired unwatched episode;
- remove an owned/adopted exact show after a complete published generation
  makes it caught up;
- keep a caught-up continuing show absent while Sonarr remains monitored; and
- add it again when a later complete generation contains an aired unwatched
  episode.

Plex watchlist actions use exact external show identity. A Plex library match
does not suppress watchlist removal and never authorizes library deletion.
Each action is independently retryable and audited.

# Poland-Specific Availability

Trakt WatchNow access is not a required dependency. TMDB provides:

- series watch providers from /tv/{series_id}/watch/providers; and
- season watch providers from
  /tv/{series_id}/season/{season_number}/watch/providers.

The backend reads the PL result and matches the user's services by stable TMDB
provider ID, not provider name. It uses the same configured provider IDs as the
movie application without changing movie matching semantics.

Provider categories remain separate:

- flatrate means included with a configured subscription;
- free and ads are shown distinctly;
- rent and buy are shown distinctly; and
- a TMDB response with no configured provider is confirmed unavailable on the
  configured services only when the PL response was fetched successfully.

Missing, failed, or stale provider data is unknown, never Not released or
confirmed unavailable. Results are cached for 24 hours; the provider catalog
and regions refresh daily. The UI displays TMDB and JustWatch attribution and
uses TMDB's provider link instead of constructing unsupported deep links.

The primary card shows current-series availability and the current or next
relevant season when available. The detail view retains per-season provider
data so availability differences between seasons are visible.

# Backend Contracts

The following routes are proposed:

| Endpoint | Contract |
|---|---|
| POST /api/sync/tv | Protected complete TV refresh; returns the published generation ID or a typed failure without changing the published pointer. |
| POST /api/sync/all | Keeps movie behavior and adds a separately reported TV result; partial TV failure cannot be masked. |
| GET /api/export/tv/sync-state | Complete worker snapshot with generation metadata, managed shows, progress, desired Plex state, and pending cleanup events. |
| GET /api/watchlist?collection=tv&state=active | Active TV browse rows. |
| GET /api/watchlist?collection=tv&state=caught_up | Dormant caught-up rows. |
| GET /api/watchlist?collection=tv&state=retired | Terminal cleanup history. |
| GET /api/watchlist/{id} | Existing detail route extended for TV with season/episode progress, lifecycle, availability, and last known destination status. |
| POST /api/worker/tv/cleanup-authorizations/{eventId}/claim | Protected atomic cleanup lease. |
| POST /api/worker/tv/cleanup-authorizations/{authorizationId}/result | Protected partial/final cleanup result and convergence record. |
| POST /api/worker/tv/runs | Protected redacted run summary for UI status and operations. |

The public state query intentionally maps state=retired to the stored
retired_terminal lifecycle value. This keeps the URL concise while the DTO and
tests retain the precise internal enum.

The TV export contains only one published generation. At minimum it includes:

- generation ID, timestamps, kind, source health, and freshness;
- exact show identities and source membership;
- Trakt status, aired/completed totals, detailed progress, next episode, and
  per-season desired monitoring;
- desired Sonarr and Plex watchlist state;
- cleanup event IDs and source predicate hashes;
- provider state and freshness; and
- explicit blockers such as missing identity, unresolved outbox, stale Plex
  evidence, or incomplete backfill.

The worker-run summary gives the backend last-known Sonarr and Plex watchlist
status without moving destination ownership into the backend. It contains no
credentials or media paths.

Protected routes use the existing sync-key boundary. Read routes retain the
trusted-LAN policy unless a later security decision changes it.

# Android TV Experience

Android remains a read-only backend client.

TV browse cards show:

- poster, title, and year;
- completed of aired progress;
- lifecycle state or season-cleanup badge;
- next episode and air date when known;
- Plex availability; and
- matching configured Polish subscription providers.

The TV detail view shows:

- per-season and per-episode watched state;
- last watched and next episode;
- Trakt production status;
- caught-up, reactivated, cleanup-pending, or retired lifecycle information;
- last known Sonarr monitoring and Plex watchlist state;
- Poland provider results and freshness; and
- a clear unknown state for identity, provider, or integration failures.

The Android parser and backend DTO are versioned together. A cross-contract
fixture test runs in CI so a backend field or enum change cannot silently break
the Android TV rail. Android never receives Trakt, Plex, Sonarr, TMDB, MongoDB,
or sync-key credentials.

# Failure Handling

- Revoked or unreadable Trakt OAuth preserves the last good UI snapshot and
  freezes lifecycle publication and all TV mutation.
- Partial pages, malformed JSON, duplicate source identities, pagination
  races, impossible progress counts, or source timeout publish nothing.
- Plex history collection failure or incomplete bootstrap publishes no
  mutation-capable generation.
- TMDB provider failure publishes source state with provider status unknown.
- Trakt writes are serialized, honor Retry-After, and never blindly retry an
  ambiguous history request.
- A failed destination collection blocks every action whose proof depends on
  that boundary. Any destructive cleanup depends on Sonarr, Plex library,
  configured-account watched evidence, and path verification, so failure of
  any one blocks all destructive actions. An independently safe reversible
  action may proceed only when all of its own source and destination
  collections are complete.
- A failed cleanup gate records one stable, specific reason. Unknown is never
  treated as false or safe.
- A successful action in a partial run is audited immediately. The next run
  recollects live state and retries only unresolved desired state.
- Secrets, OAuth codes after use, tokens, API keys, full media paths, and
  response bodies that may contain them are redacted from logs and reports.

# Safety Policy

Backend Trakt history delivery has its own
TRAKT_HISTORY_SYNC_APPLY switch, false by default. It controls only outbox
writes; Plex history collection, bootstrap reconciliation, and reporting can
run while it is false.

The worker uses three independent feature switches, all false by default:

- TV_SYNC_APPLY;
- TV_SYNC_ALLOW_SEASON_FILE_DELETION; and
- TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION.

TV_SYNC_APPLY controls only worker Sonarr and Plex-watchlist actions and never
enables backend Trakt writes. The irreversible override is named
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE; it is a fourth separate worker switch,
also defaults false, and cannot be implied by global apply.

A CLI `--apply` value is only a per-run request. Effective worker apply requires
both that request and `TV_SYNC_APPLY=true`; command-line input cannot override a
false environment gate.

One run may plan at most two season cleanups and one terminal whole-series
cleanup. If either proposed destructive count exceeds its cap, the worker
executes zero destructive TV actions for the entire run. Reversible additions
and Plex watchlist actions may proceed only if their own collections are
healthy and the report clearly states the destructive block.

All destructive actions require:

- a current complete manifest carrying a cleanup event whose grace and
  qualification were established by scheduled source generations;
- a fresh complete worker collection;
- exact TVDB identity;
- app ownership or explicit adoption;
- a stable MongoDB cleanup event;
- a successful atomic claim;
- seven days of continuously valid eligibility;
- configured-account Plex evidence;
- no unresolved history outbox event;
- no pending or conflicting post-cutover routing event;
- a final live recheck; and
- durable SQLite and backend result audit.

No source disappearance, title match, empty result, provider result, Plex
library presence, or Trakt status by itself can authorize deletion.

# Observability

Backend health and status expose, without secrets:

- Trakt connection and refresh health;
- last activity poll and last complete hourly generation;
- published generation age and item counts;
- Plex history capability, bootstrap progress, and watermark;
- outbox counts by state and oldest age;
- identity quarantine counts;
- provider enrichment age;
- active, caught-up, source-removed, terminal-candidate, and retired counts;
  and
- proposed, claimed, completed, canceled, and failed cleanup counts.

Worker reports expose:

- collected Sonarr, Plex watchlist, Plex library, and path-verification counts;
- adoption candidates and managed destination counts;
- deterministic proposed actions and stable reasons;
- policy blockers and feature-gate states;
- action results and convergence;
- destructive count caps;
- heartbeat age; and
- the exact backend generation used.

Report-only and apply runs use the same collector, planner, and policy engine.
Only the executor mode changes.

# Rollout

## Phase 1: Read Model

- Introduce Trakt device OAuth, complete watchlist/progress snapshots, tv_shows,
  migration from legacy TV rows, TMDB TV enrichment, API DTOs, and Android
  read-only progress.
- Keep all worker TV mutation disabled.
- Correct typed TMDB parsing and availability unknown semantics.

## Phase 2: Plex History And Trakt Writes

- Backfill configured-account Plex TV history into the durable ledger.
- Enable the outbox in small serialized batches.
- Enable TRAKT_HISTORY_SYNC_APPLY only for a supervised batch after bootstrap
  reconciliation has been reviewed.
- Reconcile counts and ambiguous outcomes.
- Require bootstrap completion and a clean outbox before later cleanup phases.

## Phase 3: Reversible Destinations

- Add exact-TVDB Sonarr desired-state planning and Plex watchlist lifecycle.
- Run report-only first.
- Review and explicitly enable adoption of existing exact matches.
- Enable Sonarr additions and Plex watchlist add/remove with destructive gates
  still disabled.

## Phase 4: Season Cleanup

- Run season cleanup in report-only mode for at least seven days.
- Review every candidate, multi-episode mapping, special, ownership record,
  cap, and path-verification result.
- Enable season file deletion for one supervised run.
- Require an immediate convergence run before unattended use.

## Phase 5: Terminal Cleanup

- Run whole-series cleanup in report-only mode for at least seven more days.
- Verify Sonarr recycle-bin configuration and the read-only TV-root mapping.
- Enable at most one supervised terminal deletion.
- Inspect Sonarr recycle bin, Plex state, MongoDB event, SQLite audit, and
  backend result before unattended use.

## Phase 6: Normal Operation

- Keep continuing shows monitored.
- Keep retired rows refreshable for revival.
- Preserve the disabled TMDB account importer until its compatibility route is
  removed.
- Update standing architecture, API, integration, operations, deployment, and
  decision documents before declaring the feature production behavior.

# Validation Strategy

## Backend

Automated tests cover:

- Trakt device flow, refresh, revocation, redaction, and persistent keyring
  behavior;
- watchlist and progress pagination;
- the watchlist plus unfinished-progress union after Trakt auto-removal;
- completed progress retention;
- pre/post activity race rejection;
- hourly new-airing detection without user activity;
- publish-last behavior and generation-coherent reads;
- empty, malformed, duplicate, partial, and impossible source rejection;
- exact status mapping, including only ended and canceled as terminal;
- Plex history pagination, account/library filtering, bootstrap, overlap, and
  watermark recovery;
- duplicate history suppression and legitimate rewatch preservation;
- outbox leasing, rate limiting, token refresh, ambiguous reconciliation,
  retry, and dead-letter visibility;
- lifecycle, grace reset, reactivation, source removal, and cleanup event
  idempotency;
- claim/result atomicity and crash recovery;
- PL provider-ID and flatrate semantics;
- unknown versus confirmed unavailable;
- legacy TV migration without disappearance authority; and
- API authorization, serialization, filters, and typed failure responses.

## Worker

Automated tests cover:

- strict complete TV export parsing;
- exact-TVDB Sonarr lookup/add and targeted episode search;
- season monitoring derived from watched progress;
- app ownership and explicit adoption;
- caught-up Plex removal and new-airing restoration;
- concluded-season calculation;
- seven-day candidate persistence and reset;
- multi-episode files with all watched and partially watched combinations;
- downloaded specials and unknown mappings;
- zero-file and untracked-media terminal cases;
- stale manifest, next-airing disagreement, path mapping, and recycle-bin
  blockers;
- cleanup claim, lease expiry, partial child actions, and convergence;
- one-worker SQLite lease;
- cap overflow blocking all destructive actions;
- feature-switch independence;
- exact Sonarr terminal delete query values;
- no Plex library mutation;
- redacted JSON and Markdown reports; and
- full report-only and apply workflow simulations.

## Android And Cross-Component

Tests cover:

- TV progress and lifecycle parsing;
- active, caught-up, and retired filters;
- next-episode and availability rendering;
- unknown/stale states;
- backend DTO fixture compatibility; and
- no integration credentials in client configuration or responses.

Full validation includes existing OKF, backend, worker, deployment, Docker,
Compose, Android, and redacted secret-scan checks. A mocked end-to-end
simulation must cover Plex play, one Trakt history entry, caught-up transition,
Plex watchlist removal, new episode reactivation, concluded-season cleanup,
terminal cleanup, and revival.

# Acceptance Criteria

- The first watched Plex episode can cause Trakt to remove the show from its
  built-in watchlist without making the application lose the unfinished show.
- Bootstrap records all accessible Plex history but does not replay legacy
  rewatches for an episode Trakt already marks watched.
- An overlapping Plex history poll creates one outbox row for one play, while a
  genuine post-bootstrap rewatch creates a second intentional Trakt play.
- An ambiguous Trakt timeout is never retried blindly; it is quarantined and
  reconciled twice, with any detected duplicate or unresolved ambiguity made
  operator-visible.
- Watching the last currently aired episode of a continuing show removes only
  an owned or adopted Plex watchlist entry and leaves Sonarr monitored.
- Deliberately re-adding that show to the Trakt watchlist makes it active,
  restores its owned Plex watchlist entry, and blocks cleanup.
- A newly aired unwatched episode reactivates the show, monitors its season,
  searches only the exact missing aired episode, and restores Plex watchlist
  membership.
- Catching up during a weekly season does not delete files while Trakt or
  Sonarr knows a later episode in that season.
- A source-removed show leaves the active UI and owned Plex watchlist only after
  two complete hourly confirmations; it causes no Sonarr change or file
  deletion.
- A scheduled-source gap longer than two hours or a current Plex played state
  changed to unwatched resets the seven-day cleanup clock.
- A concluded fully watched numbered season remains eligible for seven days,
  deletes only exact fully watched episode files, becomes unmonitored, and
  leaves the series monitored.
- A multi-episode file with one unwatched episode is not deleted.
- One terminal snapshot cannot authorize deletion.
- A fully watched ended or canceled show can produce exactly one cleanup event
  only after two qualifying hourly snapshots, seven days of continuous
  eligibility, and all source-side checks.
- Missing identity, explicit watchlist membership, pending/conflicting
  post-cutover routing, pending outbox, stale Plex evidence, unwatched special,
  unknown mapping, untracked media, non-owned series, missing recycle bin,
  path-verification failure, next-airing disagreement, stale manifest, or cap
  overflow each blocks deletion with a specific reason.
- A successful terminal action removes the exact Sonarr series with
  deleteFiles=true and addImportListExclusion=false, verifies absence, and
  never calls a Plex library deletion API.
- A worker crash cannot bypass caps, duplicate a Trakt play, or blindly repeat
  a Sonarr deletion.
- A later new episode or terminal-status reversal restores Sonarr management;
  Plex restoration follows the separate watchlist/unwatched rule.
- A TV source failure preserves the last good UI state and authorizes no
  mutation.
- Polish availability matches configured services by TMDB provider ID and
  distinguishes included subscription, other offer types, confirmed
  unavailable, unknown, and stale.
- Existing movie synchronization behaves exactly as before.

# Required Documentation Decisions

Before either deletion gate can be enabled, implementation must update or add a
durable decision that explicitly permits these two new deletion categories:

- Sonarr episode-file deletion for a concluded watched season; and
- Sonarr whole-series deletion with files for a terminal watched show.

The implemented contracts must also update the standing system boundary,
backend API, export endpoint, Trakt, Plex, TMDB, worker, Android, deployment,
operations, validation, roadmap, and changelog concepts. This design is the
planning authority until those documents describe deployed behavior.

# References

- [System Boundaries](../../architecture/system_boundaries.md)
- [Production Movie Sync](../../architecture/movie_sync_production.md)
- [Backend Owns Integrations](../../decisions/backend_owns_integrations.md)
- [VOD Filter Worker Boundary](../../decisions/vod_filter_worker_boundary.md)
- [MongoDB Read Model](../../decisions/mongodb_read_model.md)
- [Android TV Read-Only V1](../../decisions/android_tv_read_only_v1.md)
- [Backend API](../../apis/backend_api.md)
- [Export Endpoints](../../apis/export_endpoints.md)
- [TMDB Integration](../../integrations/tmdb.md)
- [Plex Integration](../../integrations/plex.md)
- [Roadmap](../../backlog/roadmap.md)
- [Trakt OAuth](https://docs.trakt.tv/docs/authentication-oauth)
- [Trakt Watchlist](https://docs.trakt.tv/reference/getsyncwatchlistget)
- [Trakt Watched Progress](https://docs.trakt.tv/reference/getsyncprogresswatched)
- [Trakt Show Progress](https://docs.trakt.tv/reference/getshowsprogresswatched)
- [Trakt Last Activities](https://docs.trakt.tv/reference/getsynclastactivities)
- [Trakt Add To History](https://docs.trakt.tv/reference/postsynchistoryadd)
- [Trakt Status Contract](https://github.com/trakt/trakt-api/blob/master/projects/api/src/contracts/_internal/response/statusResponseSchema.ts)
- [Plex Dashboard And Play History](https://support.plex.tv/articles/200871837-status-and-dashboard/)
- [Sonarr API Contract](https://github.com/Sonarr/Sonarr/blob/master/src/Sonarr.Api.V3/openapi.json)
- [TMDB TV Providers](https://developer.themoviedb.org/reference/tv-series-watch-providers)
- [TMDB Season Providers](https://developer.themoviedb.org/reference/tv-season-watch-providers)
- [TMDB Attribution And Provider Data](https://developer.themoviedb.org/docs/faq)
