"""Single-run composition root for report-first reversible TV synchronization."""

from __future__ import annotations

import argparse
import os
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from dotenv import load_dotenv

sys.path.insert(0, str(Path(__file__).parent))

from healthcheck import write_heartbeat
from src.clients.plex_tv_client import PlexTvClient
from src.clients.sonarr_tv_client import SonarrTvClient
from src.clients.watchlist_app_client import WatchlistAppClient
from src.config import ConfigurationError, load_config
from src.services.tv_destination_executor import TvDestinationExecutor
from src.services.tv_state_store import TvStateStore
from src.services.tv_sync_collector import TvSyncCollector
from src.services.tv_sync_planner import build_tv_plan
from src.services.tv_sync_policy import TvSyncPolicy, evaluate_tv_plan, report_only_blockers
from src.services.tv_sync_report import write_tv_sync_reports
from src.utils.logging import setup_logging


@dataclass(frozen=True)
class TvSyncRunResult:
    exit_code: int
    blockers: tuple[str, ...]
    json_path: Path
    markdown_path: Path


def execute_tv_sync(
    *,
    collector,
    planner,
    executor,
    state_store,
    policy: TvSyncPolicy,
    apply_requested: bool,
    report_dir: Path,
    now: datetime | None = None,
) -> TvSyncRunResult:
    """Collect, plan, gate, execute, report, and persist one isolated TV run."""
    collected = collector.collect()
    plan = planner(collected)
    generation_id = plan.generation_id or "unavailable"
    run_id = state_store.start_run(generation_id)
    try:
        blockers = evaluate_tv_plan(
            plan,
            policy,
            snapshot=collected.snapshot,
            apply_requested=apply_requested,
            now=now,
        )
        apply = apply_requested and policy.enabled and policy.apply_enabled
        execution = executor.execute(
            plan,
            blockers,
            apply=apply,
            adopt=apply and policy.adoption_enabled,
        )
        effective_blockers = tuple(blockers) if apply_requested else report_only_blockers(blockers)
        exit_code = 2 if execution.errors else 3 if effective_blockers else 0
        paths = write_tv_sync_reports(
            execution,
            blockers=blockers,
            report_dir=report_dir,
            run_id=run_id,
            mode="apply" if apply else "report_only",
        )
        state_store.finish_run(run_id, "completed" if exit_code == 0 else "failed")
        return TvSyncRunResult(exit_code, tuple(blockers), paths.json_path, paths.markdown_path)
    except Exception:
        state_store.finish_run(run_id, "failed")
        raise


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Plan and apply reversible TV destination synchronization")
    parser.add_argument("--apply", action="store_true", help="Apply only when the host TV apply gate is also enabled")
    parser.add_argument("--report-dir", default="data/reports")
    parser.add_argument("--quiet", action="store_true")
    parser.add_argument("--once", action="store_true", help="Run once (the TV CLI is always single-run)")
    return parser.parse_args(argv)


def build_tv_sync_policy(config) -> TvSyncPolicy:
    return TvSyncPolicy(
        enabled=config.tv_sync_enabled,
        apply_enabled=config.tv_sync_apply,
        adoption_enabled=config.tv_sync_adopt_existing_destinations,
        max_snapshot_age_minutes=config.tv_sync_max_snapshot_age_minutes,
    )


def main(argv=None) -> int:
    load_dotenv()
    args = parse_args(argv)
    setup_logging(log_level="WARNING" if args.quiet else os.getenv("LOG_LEVEL", "INFO").upper(), log_format="human")
    try:
        config = load_config()
        config.validate()
        if config.watchlist_source != "watchlist_app":
            raise ConfigurationError("sync_tv.py requires WATCHLIST_SOURCE=watchlist_app")
        if not config.tv_sync_enabled:
            raise ConfigurationError("TV_SYNC_ENABLED=true is required for sync_tv.py")
        state_store = TvStateStore(config.database_path)
        backend = WatchlistAppClient(
            config.watchlist_app_url,
            timeout_seconds=config.watchlist_app_timeout_seconds,
            sync_timeout_seconds=config.watchlist_app_sync_timeout_seconds,
            sync_key=config.watchlist_app_sync_key,
        )
        sonarr = SonarrTvClient(config.sonarr_url, config.sonarr_api_key)
        plex = PlexTvClient(config.plex_url, config.plex_token)
        collector = TvSyncCollector(
            backend_client=backend,
            sonarr_client=sonarr,
            plex_client=plex,
            state_store=state_store,
            plex_library_name=config.plex_tv_library_name,
        )
        executor = TvDestinationExecutor(
            state_store,
            sonarr,
            plex,
            sonarr_root_folder=config.sonarr_root_folder,
            sonarr_quality_profile_id=config.sonarr_quality_profile_id,
        )
        result = execute_tv_sync(
            collector=collector,
            planner=build_tv_plan,
            executor=executor,
            state_store=state_store,
            policy=build_tv_sync_policy(config),
            apply_requested=args.apply,
            report_dir=Path(args.report_dir),
        )
    except (ConfigurationError, Exception) as error:
        print(f"TV sync failed: {error}", file=sys.stderr)
        return 1
    heartbeat_status = {0: "completed" if args.apply else "reconciliation", 2: "partial", 3: "blocked"}.get(result.exit_code, "failed")
    write_heartbeat(config.database_path.parent / "last-run.json", status=heartbeat_status, exit_code=result.exit_code)
    if not args.quiet:
        print(f"TV sync JSON report: {result.json_path}")
        print(f"TV sync Markdown report: {result.markdown_path}")
    return result.exit_code


if __name__ == "__main__":
    raise SystemExit(main())
