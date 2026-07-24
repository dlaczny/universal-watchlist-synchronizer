---
type: Backlog
title: TV Reversible Destination Sync Implementation Plan
description: Test-first plan for a Polish-provider-aware, reversible Sonarr and Plex Watchlist TV destination sync.
tags:
  - tv
  - trakt
  - sonarr
  - plex
  - worker
  - rollout
timestamp: 2026-07-24T00:00:00Z
version: 1.0.0
---

# TV Reversible Destination Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a report-first, reversible TV worker that uses a published Trakt generation to reconcile selected Sonarr seasons and Plex Watchlist entries without any history write, library mutation, or Sonarr deletion.

**Architecture:** Extend the existing immutable TV export to schema version 2 with a destination-specific capability envelope and per-season Polish availability. A new isolated Python workflow reads that export, exact-matches Sonarr by TVDB and Plex by verified external identity, produces a deterministic SQLite-backed plan, then applies only explicitly armed reversible actions. Movie workflow files and tables stay untouched.

**Tech Stack:** .NET 10/C# 12, MongoDB 8, ASP.NET Minimal API, Python 3.11, httpx, PlexAPI, SQLite, Sonarr API v3/v4, pytest, xUnit, FluentAssertions, Docker Compose, GitHub Actions.

---

## Locked Decisions

- TVDB is the sole identity that can authorize Sonarr actions. Missing or
  conflicting TVDB IDs produce a blocker; no title/year fallback exists.
- The selected regular season is Season 1 for an unstarted show; otherwise it
  is Trakt's `nextEpisode` season, or the next aired numbered season after the
  most recently completed season.
- Sonarr adds/monitors/searches only when that selected season is confirmed
  unavailable in Poland. The search contains only aired, unwatched episodes.
- Plex Watchlist is desired when the selected season is confirmed available in
  Poland or the configured Plex TV library has at least one exactly identified
  episode. It may remove a manually added exact Plex Watchlist row when neither
  condition remains true.
- Existing exact Sonarr rows are adopted only after a report review. Their
  `manual` origin is stored; this release contains no Sonarr deletion for any
  origin.
- `TV_SYNC_ENABLED`, `TV_SYNC_APPLY`, and `TV_SYNC_ADOPT_EXISTING_DESTINATIONS`
  default to false. Effective apply requires both the host flag and `--apply`.
- Android, movie behavior, Plex history → Trakt, Plex-library mutation, Sonarr
  file/season/series deletion, and every cleanup flag remain out of scope.

### Task 1: Correct the active TV planning and rollout records

**Files:**
- Create: `docs/superpowers/plans/2026-07-24-tv-reversible-destination-sync.md`
- Modify: `docs/backlog/roadmap.md`
- Modify: `docs/reports/tv_integration_rollout.md`
- Modify: `docs/superpowers/plans/2026-07-13-tv-integration-program.md`
- Create: `tests/test_tv_destination_plan_docs.py`
- Test: `tests/validate_okf.py`

- [ ] **Step 1: Write the documentation assertions before editing links**

Add `tests/test_tv_destination_plan_docs.py` with these checks:

```python
def test_roadmap_points_to_the_current_destination_plan() -> None:
    roadmap = (ROOT / "docs/backlog/roadmap.md").read_text(encoding="utf-8")
    assert "2026-07-24-tv-reversible-destination-sync.md" in roadmap
    assert "2026-07-13-tv-phase-3-reversible-destinations.md" not in roadmap

def test_rollout_records_real_phase_one_production_evidence() -> None:
    ledger = (ROOT / "docs/reports/tv_integration_rollout.md").read_text(encoding="utf-8")
    assert "Trakt connected" in ledger
    assert "First complete generation published" in ledger
    assert "251/251" in ledger
```

- [ ] **Step 2: Run the documentation assertions and verify RED**

Run:

```powershell
python -m pytest tests/test_tv_destination_plan_docs.py -q
```

Expected: failure because roadmap and rollout ledger still describe the historic
Phase 2 → Phase 3 order and production evidence as pending.

- [ ] **Step 3: Update the durable planning documents**

Make these exact edits:

- In `docs/backlog/roadmap.md`, replace the two Phase 2/Phase 3 bullet points
  with a link to this plan as the active next TV release; retain Plex-history
  and deletion work as later independent phases.
- In `docs/reports/tv_integration_rollout.md`, add a dated redacted production
  evidence row: Trakt connected, a complete generation published, all 251
  published shows had usable TVDB IDs, and the release remained read-only.
  Do not write credentials, generation IDs, titles, or token material.
- In `docs/superpowers/plans/2026-07-13-tv-integration-program.md`, label the
  historic Phase 2 and Phase 3 documents as superseded for ordering only, link
  this plan as the active destination phase, and retain Phase 4/5 as blocked.

- [ ] **Step 4: Re-run documentation validation**

Run:

```powershell
python -m pytest tests/test_tv_destination_plan_docs.py -q
python tests/validate_okf.py
```

Expected: both commands exit `0`.

- [ ] **Step 5: Commit the planning cleanup**

```powershell
git add docs tests/test_tv_destination_plan_docs.py
git commit -m "docs: activate reversible TV destination plan"
```

### Task 2: Publish a strict destination-ready TV export

**Files:**
- Create: `backend/src/Watchlist.Application/WorkerTvDestinationSyncDto.cs`
- Modify: `backend/src/Watchlist.Application/WorkerTvSnapshotDto.cs`
- Modify: `backend/src/Watchlist.Application/WorkerTvSeasonDto.cs`
- Modify: `backend/src/Watchlist.Application/TvExportService.cs`
- Modify: `backend/tests/Watchlist.Application.Tests/TvExportServiceTests.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/TvSyncApiTests.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvExportServiceTests.cs`

- [ ] **Step 1: Write failing schema-v2 export tests**

Add tests that require the export to include exact schema version `2`, a
destination capability envelope, and selected-season availability:

```csharp
[Fact]
public async Task Export_ProvidesDestinationCapabilityAndSeasonAvailability()
{
    WorkerTvSnapshotDto snapshot = await CreateService(validPublishedGeneration)
        .GetTvSyncSnapshotAsync(CancellationToken.None) ?? throw new XunitException();

    snapshot.SchemaVersion.Should().Be("2");
    snapshot.DestinationSync.Capable.Should().BeTrue();
    snapshot.DestinationSync.Blockers.Should().BeEmpty();
    snapshot.Shows.Single().Seasons.Single(season => season.SeasonNumber == 1)
        .PolandAvailability.State.Should().Be("confirmed_unavailable");
}

[Fact]
public async Task Export_MarksInvalidPublicationDestinationIncapable()
{
    WorkerTvSnapshotDto snapshot = await CreateService(invalidPublishedGeneration)
        .GetTvSyncSnapshotAsync(CancellationToken.None) ?? throw new XunitException();

    snapshot.DestinationSync.Capable.Should().BeFalse();
    snapshot.DestinationSync.Blockers.Should().Contain("tv_generation_not_valid");
}
```

- [ ] **Step 2: Run the export tests and verify RED**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvExportServiceTests"
```

Expected: compilation failure because `DestinationSync` and season
`PolandAvailability` do not exist.

- [ ] **Step 3: Add immutable DTOs and derive the envelope**

Create the capability record and extend the DTOs without changing Phase 1
`MutationCapable`:

```csharp
public sealed record WorkerTvDestinationSyncDto(
    bool Capable,
    IReadOnlyList<string> Blockers);

public sealed record WorkerTvSnapshotDto(
    string SchemaVersion,
    string GenerationId,
    DateTimeOffset PublishedAt,
    DateTimeOffset GeneratedAt,
    string Kind,
    bool MutationCapable,
    WorkerTvDestinationSyncDto DestinationSync,
    IReadOnlyList<string> HealthReasons,
    WorkerTvPlexHistoryDto PlexHistory,
    IReadOnlyList<WorkerTvShowDto> Shows,
    IReadOnlyList<WorkerTvCleanupAuthorizationDto> CleanupAuthorizations);

public sealed record WorkerTvSeasonDto(
    int SeasonNumber,
    int Aired,
    int Completed,
    bool MonitoredDesired,
    IReadOnlyList<int> SearchAiredUnwatchedEpisodes,
    string CleanupState,
    TvProviderAvailabilityDto PolandAvailability,
    IReadOnlyList<WorkerTvEpisodeDto> Episodes);
```

In `TvExportService`, emit schema version `2`, map each domain season's
`Availability`, and create `DestinationSync` from the immutable manifest:
`Capable` is true only when `ValidationStatus == "valid"`, the generation has
a positive publication time, and no manifest validation failures exist. Use
stable blockers `tv_generation_not_valid`, `tv_generation_unpublished`, and
`tv_generation_validation_failed`; sort and de-duplicate them. Per-show
identity and availability uncertainty stays in the existing per-show blockers,
not in the global envelope.

- [ ] **Step 4: Add API serialization coverage and run GREEN**

Update seeded API data and assert `/api/export/tv/sync-state` serializes
`schemaVersion:"2"`, `destinationSync.capable`, and season
`polandAvailability`, while preserving `mutationCapable:false`.

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvExportServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvSyncApiTests"
```

Expected: both commands exit `0`.

- [ ] **Step 5: Commit the export contract**

```powershell
git add backend/src backend/tests/Watchlist.Application.Tests/TvExportServiceTests.cs backend/tests/Watchlist.Api.Tests
git commit -m "feat: publish destination-ready TV export"
```

### Task 3: Add strict Python TV contract parsing and disabled-by-default configuration

**Files:**
- Create: `workers/vod-filter/src/models/tv_sync.py`
- Create: `workers/vod-filter/tests/vod_filter/test_tv_backend_client.py`
- Modify: `workers/vod-filter/src/clients/watchlist_app_client.py`
- Modify: `workers/vod-filter/src/config.py`
- Modify: `workers/vod-filter/tests/vod_filter/test_config.py`
- Modify: `workers/vod-filter/example.env`
- Modify: `deploy/production/worker.env.example`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_backend_client.py`

- [ ] **Step 1: Write failing parser/configuration tests**

Require schema version 2, a capable envelope, exact positive TVDB IDs, regular
season availability, and independent false defaults:

```python
def test_fetch_tv_snapshot_rejects_non_v2_or_incapable_payload() -> None:
    client = WatchlistAppClient("http://backend", http_client=FakeHttpClient(
        tv_snapshot(schema_version="1", capable=False)
    ))
    with pytest.raises(WatchlistAppError, match="destinationSync"):
        client.fetch_tv_sync_snapshot()

def test_config_keeps_tv_collection_and_apply_disabled_by_default(monkeypatch) -> None:
    clear_tv_environment(monkeypatch)
    config = Config()
    assert config.tv_sync_enabled is False
    assert config.tv_sync_apply is False
    assert config.tv_sync_adopt_existing_destinations is False
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_backend_client.py tests\vod_filter\test_config.py -q
Pop-Location
```

Expected: import failures because TV models, fetch method, and configuration
properties do not exist.

- [ ] **Step 3: Implement immutable worker models and strict parsing**

Create dataclasses for `TvSnapshot`, `TvShow`, `TvSeason`, `TvEpisode`, and
`TvAvailability`. Implement `WatchlistAppClient.fetch_tv_sync_snapshot()` to
make one unauthenticated `GET /api/export/tv/sync-state`, reject invalid JSON,
schema versions other than `2`, an incapable envelope, duplicate Trakt/TVDB
identities, non-UTC timestamps, specials in selected-season candidates, and
credential-shaped keys at every object depth. Return only frozen typed models.

Add these configuration fields; require Sonarr and Plex TV settings only when
`TV_SYNC_ENABLED=true`:

```python
self.tv_sync_enabled = os.getenv("TV_SYNC_ENABLED", "false").lower() == "true"
self.tv_sync_apply = os.getenv("TV_SYNC_APPLY", "false").lower() == "true"
self.tv_sync_adopt_existing_destinations = (
    os.getenv("TV_SYNC_ADOPT_EXISTING_DESTINATIONS", "false").lower() == "true"
)
self.tv_sync_interval_seconds = self._parse_int("TV_SYNC_INTERVAL_SECONDS", "900", minimum=60)
self.tv_sync_max_snapshot_age_minutes = self._parse_int("TV_SYNC_MAX_SNAPSHOT_AGE_MINUTES", "30", minimum=1)
```

When enabled, validate `SONARR_URL`, `SONARR_API_KEY`,
`SONARR_ROOT_FOLDER`, `SONARR_QUALITY_PROFILE_ID`, and
`PLEX_TV_LIBRARY_NAME`; leave all cleanup flags false and reject true values.

- [ ] **Step 4: Run GREEN and commit**

Run:

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_backend_client.py tests\vod_filter\test_config.py -q
Pop-Location
```

Expected: exit `0`.

```powershell
git add workers/vod-filter/src workers/vod-filter/tests/vod_filter/test_tv_backend_client.py workers/vod-filter/tests/vod_filter/test_config.py workers/vod-filter/example.env deploy/production/worker.env.example
git commit -m "feat: add strict TV worker input contract"
```

### Task 4: Build exact Sonarr and Plex TV boundary clients

**Files:**
- Create: `workers/vod-filter/src/clients/sonarr_tv_client.py`
- Create: `workers/vod-filter/src/clients/plex_tv_client.py`
- Create: `workers/vod-filter/tests/vod_filter/test_sonarr_tv_client.py`
- Create: `workers/vod-filter/tests/vod_filter/test_plex_tv_client.py`
- Test: `workers/vod-filter/tests/vod_filter/test_sonarr_tv_client.py`

- [ ] **Step 1: Write failing exact-identity client tests**

Cover the complete permitted surface:

```python
def test_sonarr_lookup_uses_tvdb_term_and_rejects_mismatched_result() -> None:
    client = SonarrTvClient("http://sonarr", "key", http_client=FakeHttpClient([
        response(200, [{"id": 44, "tvdbId": 999, "title": "Wrong"}])
    ]))
    with pytest.raises(SonarrTvError, match="TVDB identity mismatch"):
        client.lookup_by_tvdb(123)

def test_plex_watchlist_remove_requires_exact_verified_show_guid() -> None:
    client = PlexTvClient(fake_account_with_guid("tmdb://9"))
    assert client.remove_watchlist_show(tvdb_id=123, tmdb_id=9, imdb_id=None) is False
```

Also assert that no client has a `delete_series`, `delete_episode_file`, or
Plex library-write method.

- [ ] **Step 2: Run focused clients tests and verify RED**

Run:

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sonarr_tv_client.py tests\vod_filter\test_plex_tv_client.py -q
Pop-Location
```

Expected: import failures for both new clients.

- [ ] **Step 3: Implement only the allowed boundary operations**

Implement these public methods and no deletion method:

```python
class SonarrTvClient:
    def get_series_by_tvdb(self, tvdb_id: int) -> SonarrSeries | None: pass
    def lookup_by_tvdb(self, tvdb_id: int) -> SonarrSeriesLookup: pass
    def add_series(self, lookup: SonarrSeriesLookup, root_folder: str, quality_profile_id: int) -> SonarrSeries: pass
    def set_series_monitored(self, series: SonarrSeries, monitored: bool) -> SonarrSeries: pass
    def set_season_monitored(self, series: SonarrSeries, season_number: int) -> SonarrSeries: pass
    def search_episode_ids(self, series_id: int, episode_ids: list[int]) -> None: pass

class PlexTvClient:
    def get_watchlist_shows(self) -> list[PlexTvShow]: pass
    def get_library_show_identities(self, library_name: str) -> set[VerifiedTvIdentity]: pass
    def add_watchlist_show(self, identity: VerifiedTvIdentity) -> bool: pass
    def remove_watchlist_show(self, tvdb_id: int, tmdb_id: int | None, imdb_id: str | None) -> bool: pass
```

Sonarr lookup sends `GET /api/v3/series/lookup?term=tvdb:<id>` and verifies the
returned `tvdbId` before post/put commands. Plex GUID parsing accepts only
`tvdb://`, `tmdb://`, and `imdb://` values that produce a verified backend
identity; title matching is never used.

- [ ] **Step 4: Run GREEN and commit**

Run the Step 2 command. Expected: exit `0`.

```powershell
git add workers/vod-filter/src/clients workers/vod-filter/tests/vod_filter/test_sonarr_tv_client.py workers/vod-filter/tests/vod_filter/test_plex_tv_client.py
git commit -m "feat: add exact TV destination clients"
```

### Task 5: Add TV SQLite state, collector, and deterministic planner

**Files:**
- Create: `workers/vod-filter/src/models/tv_destination.py`
- Create: `workers/vod-filter/src/models/migrations/0001_tv_sync.sql`
- Create: `workers/vod-filter/src/services/tv_state_store.py`
- Create: `workers/vod-filter/src/services/tv_sync_collector.py`
- Create: `workers/vod-filter/src/services/tv_sync_planner.py`
- Create: `workers/vod-filter/tests/vod_filter/test_tv_state_store.py`
- Create: `workers/vod-filter/tests/vod_filter/test_tv_sync_collector.py`
- Create: `workers/vod-filter/tests/vod_filter/test_tv_sync_planner.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_sync_planner.py`

- [ ] **Step 1: Write failing selected-season and ownership tests**

```python
def test_unstarted_unavailable_show_selects_season_one_and_sonarr_add() -> None:
    plan = build_plan(show(unstarted=True, season(1, availability="confirmed_unavailable")), [])
    assert plan.decisions_for("sonarr") == [
        decision("sonarr_add", tvdb_id=100, season_number=1),
        decision("sonarr_monitor_season", tvdb_id=100, season_number=1),
    ]

def test_completed_first_season_selects_second_season_only() -> None:
    plan = build_plan(show(next_episode=(2, 1), season(2, availability="confirmed_unavailable")), [])
    assert plan.selected_season_by_tvdb[100] == 2

def test_existing_sonarr_series_is_adoption_candidate_not_auto_owned() -> None:
    plan = build_plan(show(), [sonarr_series(tvdb_id=100)])
    assert plan.decisions_for("sonarr") == [decision("sonarr_adoption_candidate", tvdb_id=100)]
```

- [ ] **Step 2: Run planner tests and verify RED**

Run:

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_state_store.py tests\vod_filter\test_tv_sync_collector.py tests\vod_filter\test_tv_sync_planner.py -q
Pop-Location
```

Expected: import failures for TV state, collector, and planner modules.

- [ ] **Step 3: Implement isolated state and pure planning**

Create SQLite tables `tv_sync_runs`, `tv_destination_ownership`, and
`tv_destination_actions`; do not alter `schema.sql` or movie tables. Persist
`origin` as exactly `worker` or `manual`, use a unique
`(destination, tvdb_id)` ownership key, and use a lease row keyed
`tv_destination_sync`.

Use these immutable decision fields:

```python
@dataclass(frozen=True)
class TvDecision:
    action_id: str
    destination: Literal["sonarr", "plex_watchlist"]
    action: Literal["sonarr_add", "sonarr_monitor_series", "sonarr_monitor_season",
                    "sonarr_search_episodes", "sonarr_adoption_candidate",
                    "plex_add", "plex_remove", "keep", "skip", "uncertain"]
    tvdb_id: int
    selected_season_number: int | None
    reason: str
    episode_numbers: tuple[int, ...] = ()
```

The collector fetches exactly one backend snapshot, all Sonarr series, Plex TV
watchlist, Plex TV-library identities, and TV ownership rows. Any failed
boundary becomes a named collection error and prevents the planner from
producing an applyable plan. The planner uses the locked selection and desired
state rules, deterministic TVDB ordering, and action ids
`{generation_id}:{destination}:{tvdb_id}:{season_or_0}:{action}`.

- [ ] **Step 4: Run GREEN and commit**

Run the Step 2 command. Expected: exit `0`.

```powershell
git add workers/vod-filter/src/models workers/vod-filter/src/services workers/vod-filter/tests/vod_filter/test_tv_state_store.py workers/vod-filter/tests/vod_filter/test_tv_sync_collector.py workers/vod-filter/tests/vod_filter/test_tv_sync_planner.py
git commit -m "feat: plan TV destination reconciliation"
```

### Task 6: Add policy, reversible executor, reports, and TV CLI

**Files:**
- Create: `workers/vod-filter/src/services/tv_sync_policy.py`
- Create: `workers/vod-filter/src/services/tv_destination_executor.py`
- Create: `workers/vod-filter/src/services/tv_sync_report.py`
- Create: `workers/vod-filter/sync_tv.py`
- Create: `workers/vod-filter/tests/vod_filter/test_tv_sync_policy.py`
- Create: `workers/vod-filter/tests/vod_filter/test_tv_destination_executor.py`
- Create: `workers/vod-filter/tests/vod_filter/test_tv_sync_report.py`
- Create: `workers/vod-filter/tests/vod_filter/test_sync_tv_cli.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_destination_executor.py`

- [ ] **Step 1: Write failing policy/execution tests**

```python
def test_apply_requires_cli_flag_and_host_gate() -> None:
    blockers = evaluate_tv_plan(plan(), TvSyncPolicy(enabled=True, apply_enabled=False), apply_requested=True)
    assert blockers == ["tv_apply_disabled"]

def test_executor_records_plex_removal_before_next_action() -> None:
    state = FakeStateStore()
    result = TvDestinationExecutor(state, FakeSonarr(), FakePlex()).execute(
        plan_with(decision("plex_remove", tvdb_id=100)), apply=True
    )
    assert state.actions == [("plex_remove", "completed")]
    assert result.errors == ()

def test_executor_never_exposes_sonarr_delete_action() -> None:
    assert "delete" not in {item.action for item in plan().decisions}
```

- [ ] **Step 2: Run the focused suite and verify RED**

Run:

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_policy.py tests\vod_filter\test_tv_destination_executor.py tests\vod_filter\test_tv_sync_report.py tests\vod_filter\test_sync_tv_cli.py -q
Pop-Location
```

Expected: import failures for the policy, executor, report, and CLI modules.

- [ ] **Step 3: Implement gates and the only executable actions**

`TvSyncPolicy` blocks apply for an incapable/stale snapshot, collection errors,
duplicate identities, unknown/stale provider evidence for a Sonarr action,
missing verified Plex identity, action-count threshold breach, disabled TV
collection, disabled host apply, or disabled adoption. Report-only mode retains
the `tv_apply_disabled` blocker but exits successfully when all other facts are
safe.

The executor dispatches only these branches:

```python
if decision.action == "sonarr_add":
    sonarr.add_series(decision.lookup, config.sonarr_root_folder, config.sonarr_quality_profile_id)
elif decision.action == "sonarr_monitor_series":
    sonarr.set_series_monitored(decision.series, monitored=True)
elif decision.action == "sonarr_monitor_season":
    sonarr.set_season_monitored(decision.series, decision.selected_season_number)
elif decision.action == "sonarr_search_episodes":
    sonarr.search_episode_ids(decision.series.id, list(decision.episode_numbers))
elif decision.action == "plex_add":
    plex.add_watchlist_show(decision.identity)
elif decision.action == "plex_remove":
    plex.remove_watchlist_show(decision.tvdb_id, decision.tmdb_id, decision.imdb_id)
```

For every success, write the action audit immediately. `sonarr_adoption_candidate`
records only a report row unless both adoption gate and apply are true, then it
creates `manual` ownership without changing monitoring. Write redacted JSON and
Markdown reports under `data/reports`; include counts, stable reasons, IDs, and
action outcomes but no headers, API keys, tokens, or raw upstream bodies.

`sync_tv.py` parses `--apply`, `--report-dir`, `--quiet`, and `--once`; it
initializes the new TV state store, collects, plans, evaluates, executes,
reports, and writes a `tv_sync` workflow heartbeat.

- [ ] **Step 4: Run GREEN and commit**

Run the Step 2 command. Expected: exit `0`.

```powershell
git add workers/vod-filter/src/services workers/vod-filter/sync_tv.py workers/vod-filter/tests/vod_filter/test_tv_sync_policy.py workers/vod-filter/tests/vod_filter/test_tv_destination_executor.py workers/vod-filter/tests/vod_filter/test_tv_sync_report.py workers/vod-filter/tests/vod_filter/test_sync_tv_cli.py
git commit -m "feat: add reversible TV sync execution"
```

### Task 7: Integrate independent scheduling, containers, deployment, and regression coverage

**Files:**
- Modify: `workers/vod-filter/continuous_sync.py`
- Modify: `workers/vod-filter/healthcheck.py`
- Modify: `workers/vod-filter/Dockerfile`
- Modify: `deploy/production/compose.yaml`
- Modify: `deploy/production/worker.env.example`
- Modify: `.github/workflows/movie-ci.yml`
- Create: `workers/vod-filter/tests/vod_filter/test_tv_workflow_simulation.py`
- Modify: `workers/vod-filter/tests/vod_filter/test_continuous_sync.py`
- Modify: `workers/vod-filter/tests/vod_filter/test_worker_healthcheck.py`
- Modify: `tests/deployment/test_tv_phase1_deployment.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_workflow_simulation.py`

- [ ] **Step 1: Write failing scheduler/deployment tests**

```python
def test_continuous_mode_runs_movie_and_tv_on_independent_deadlines(monkeypatch) -> None:
    clock = ManualClock()
    calls = run_until(clock, movie_interval=3600, tv_interval=900)
    assert calls == ["movie", "tv", "tv", "tv", "tv", "movie", "tv"]

def test_production_compose_keeps_tv_apply_and_cleanup_disabled() -> None:
    compose = load_compose("deploy/production/compose.yaml")
    worker = compose["services"]["movie-sync-worker"]
    assert worker["environment"]["TV_SYNC_APPLY"] == "false"
    assert worker["environment"]["TV_SYNC_ALLOW_SEASON_FILE_DELETION"] == "false"
    assert worker["environment"]["TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION"] == "false"
```

- [ ] **Step 2: Run integration tests and verify RED**

Run:

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_continuous_sync.py tests\vod_filter\test_worker_healthcheck.py tests\vod_filter\test_tv_workflow_simulation.py -q
Pop-Location
python -m pytest tests\deployment\test_tv_phase1_deployment.py -q
```

Expected: failures because independent TV scheduling and the TV simulation do
not exist.

- [ ] **Step 3: Wire the isolated workflow**

Add `sync_tv.py` to the Docker image. Change `continuous_sync.py` to compute
independent movie/TV deadlines; TV invocation is skipped while
`TV_SYNC_ENABLED=false`. Preserve movie command arguments and movie interval.
Change the heartbeat format to include a `workflows` map while retaining the
legacy top-level fields; health succeeds when the movie workflow is healthy and
TV is disabled, and when both workflows are healthy once TV is enabled.

Add non-secret configuration names to examples and Compose, retain every apply,
adoption, and cleanup setting as `false`, and add TV report storage under the
existing `/app/data` mount. Do not add a media-root mount. Add `sync_tv.py` to
the worker compile step in CI.

- [ ] **Step 4: Add end-to-end simulation and run GREEN**

The simulation must cover: unstarted unavailable Season 1 add/monitor/search;
provider-available Season 1 skip; completion advancing to unavailable Season
2; first Plex library episode adding Plex Watchlist; neither provider nor
library removing an exact Plex Watchlist row; manual Sonarr adoption; unknown
provider blocking Sonarr; and a second apply producing only keep/skip actions.

Run:

```powershell
Push-Location workers\vod-filter
python -m pytest -q
python -m compileall -q src continuous_sync.py sync_movies.py sync_tv.py healthcheck.py
Pop-Location
python -m pytest tests\deployment -q
```

Expected: all worker and deployment tests pass.

- [ ] **Step 5: Commit the integrated TV workflow**

```powershell
git add workers/vod-filter deploy/production .github/workflows tests/deployment
git commit -m "feat: schedule reversible TV destination sync"
```

### Task 8: Complete release validation and supervised rollout

**Files:**
- Modify: `docs/architecture/system_boundaries.md`
- Modify: `docs/architecture/tv_sync_read_model.md`
- Modify: `docs/apis/backend_api.md`
- Modify: `docs/apis/export_endpoints.md`
- Modify: `docs/runbooks/tv_sync_operations.md`
- Modify: `docs/runbooks/validation.md`
- Modify: `docs/reports/tv_integration_rollout.md`
- Test: `tests/test_tv_destination_plan_docs.py`

- [ ] **Step 1: Add failing documentation/contract assertions**

```python
def test_tv_operations_document_report_only_before_apply() -> None:
    runbook = (ROOT / "docs/runbooks/tv_sync_operations.md").read_text(encoding="utf-8")
    assert "TV_SYNC_ENABLED=true" in runbook
    assert "TV_SYNC_APPLY=false" in runbook
    assert "TV_SYNC_ADOPT_EXISTING_DESTINATIONS=true" in runbook
    assert "Sonarr deletion" not in runbook.split("Reversible destination rollout", 1)[1]
```

- [ ] **Step 2: Run the assertions and verify RED**

Run:

```powershell
python -m pytest tests/test_tv_destination_plan_docs.py -q
```

Expected: failure because the operations runbook still documents Phase 1 only.

- [ ] **Step 3: Document the exact release boundary and rollout**

Document schema v2, `destinationSyncCapable`, exact TVDB identity, selected
season/provider logic, Plex Watchlist removal authority, manual Sonarr origin,
and all prohibited operations. Add report-only, adoption, and apply commands
that require a human report review between each stage. Record no success in the
rollout ledger until the actual stage has been performed; leave history and
deletion phases explicitly disabled.

- [ ] **Step 4: Run the full release gate**

Run:

```powershell
python tests\validate_okf.py
python -m pytest tests\deployment -q
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
dotnet test backend\Watchlist.sln --configuration Release --no-build
Push-Location workers\vod-filter
python -m pytest -q
python -m compileall -q src continuous_sync.py sync_movies.py sync_tv.py healthcheck.py
Pop-Location
```

Expected: all commands exit `0`.

- [ ] **Step 5: Commit, deploy, and perform staged operations**

```powershell
git add docs tests/test_tv_destination_plan_docs.py
git commit -m "docs: operate reversible TV destination sync"
git push origin main
```

After exact-SHA CI succeeds, deploy with TV disabled. Then perform these
server-local stages in order: report-only; review report; one adoption apply
with `TV_SYNC_ADOPT_EXISTING_DESTINATIONS=true`; review report; reversible
apply with adoption false; review report; second convergence run. Record only
redacted counts, stable reason codes, and result statuses in the rollout ledger.

## Plan Self-Review

- **Spec coverage:** Tasks 2–3 implement the versioned backend/export boundary;
  Task 4 covers exact destination boundaries; Tasks 5–6 cover selection,
  ownership, plan, policy, executor, audit, and reports; Task 7 covers isolated
  scheduling and deployment; Task 8 covers validation and supervised rollout.
  The explicitly excluded history, cleanup, Android, and movie behaviors have
  no implementation task.
- **No placeholder scan:** This plan contains no unassigned implementation
  work. Every mutation has a named action, identity requirement, gate, test,
  and command.
- **Type consistency:** `DestinationSync`, `TvSnapshot`, `TvDecision`,
  `SonarrTvClient`, and `PlexTvClient` are introduced before later tasks use
  them. The policy and executor use exactly the decision action strings defined
  in Task 5.
