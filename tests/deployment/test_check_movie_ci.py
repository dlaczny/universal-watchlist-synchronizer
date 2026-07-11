from __future__ import annotations

import importlib.util
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "scripts" / "check-movie-ci.py"
SHA = "a" * 40


def load_module():
    spec = importlib.util.spec_from_file_location("check_movie_ci", MODULE_PATH)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def run(*, status: str, conclusion: str | None, sha: str = SHA, event: str = "push"):
    return {
        "head_sha": sha,
        "event": event,
        "status": status,
        "conclusion": conclusion,
        "run_number": 12,
    }


def test_completed_successful_push_is_deployable() -> None:
    module = load_module()

    code, message = module.classify_response(
        {"workflow_runs": [run(status="completed", conclusion="success")]}, SHA
    )

    assert code == module.EXIT_SUCCESS
    assert "successful" in message.lower()


def test_incomplete_exact_push_is_pending() -> None:
    module = load_module()

    code, _ = module.classify_response(
        {"workflow_runs": [run(status="in_progress", conclusion=None)]}, SHA
    )

    assert code == module.EXIT_PENDING


def test_completed_unsuccessful_push_is_failed() -> None:
    module = load_module()

    code, message = module.classify_response(
        {"workflow_runs": [run(status="completed", conclusion="failure")]}, SHA
    )

    assert code == module.EXIT_FAILED
    assert "failure" in message.lower()


def test_wrong_sha_or_event_is_missing() -> None:
    module = load_module()
    payload = {
        "workflow_runs": [
            run(status="completed", conclusion="success", sha="b" * 40),
            run(status="completed", conclusion="success", event="pull_request"),
        ]
    }

    code, _ = module.classify_response(payload, SHA)

    assert code == module.EXIT_MISSING


def test_newest_exact_run_controls_the_result() -> None:
    module = load_module()
    old_success = run(status="completed", conclusion="success")
    old_success["run_number"] = 10
    new_pending = run(status="queued", conclusion=None)
    new_pending["run_number"] = 11

    code, _ = module.classify_response(
        {"workflow_runs": [old_success, new_pending]}, SHA
    )

    assert code == module.EXIT_PENDING


def test_api_url_targets_workflow_exact_sha_and_push_event() -> None:
    module = load_module()

    url = module.build_runs_url(
        "dlaczny/universal-watchlist-synchronizer", "movie-ci.yml", SHA
    )

    assert "/actions/workflows/movie-ci.yml/runs?" in url
    assert f"head_sha={SHA}" in url
    assert "event=push" in url
