#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
import sys
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode
from urllib.request import Request, urlopen


EXIT_SUCCESS = 0
EXIT_PENDING = 2
EXIT_FAILED = 3
EXIT_MISSING = 4
EXIT_ERROR = 5

SHA_PATTERN = re.compile(r"^[0-9a-f]{40}$")
REPOSITORY_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
WORKFLOW_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+\.ya?ml$")


def build_runs_url(repository: str, workflow: str, sha: str) -> str:
    if not REPOSITORY_PATTERN.fullmatch(repository):
        raise ValueError("repository must use the owner/name form")
    if not WORKFLOW_PATTERN.fullmatch(workflow):
        raise ValueError("workflow must be a YAML filename")
    if not SHA_PATTERN.fullmatch(sha):
        raise ValueError("sha must be a lowercase 40-character Git commit SHA")

    query = urlencode({"head_sha": sha, "event": "push", "per_page": 20})
    return (
        "https://api.github.com/repos/"
        f"{quote(repository, safe='/')}/actions/workflows/{quote(workflow, safe='')}/runs?{query}"
    )


def classify_response(payload: dict[str, Any], sha: str) -> tuple[int, str]:
    raw_runs = payload.get("workflow_runs")
    if not isinstance(raw_runs, list):
        return EXIT_ERROR, "GitHub returned an invalid workflow-runs response."

    exact_runs = [
        run
        for run in raw_runs
        if isinstance(run, dict)
        and run.get("head_sha") == sha
        and run.get("event") == "push"
    ]
    if not exact_runs:
        return EXIT_MISSING, f"No Movie CI push run exists for {sha}."

    latest = max(
        exact_runs,
        key=lambda run: (
            int(run.get("run_number") or 0),
            int(run.get("run_attempt") or 0),
            int(run.get("id") or 0),
        ),
    )
    status = str(latest.get("status") or "unknown")
    conclusion = str(latest.get("conclusion") or "unknown")

    if status != "completed":
        return EXIT_PENDING, f"Movie CI for {sha} is {status}."
    if conclusion == "success":
        return EXIT_SUCCESS, f"Movie CI for {sha} completed successfully."
    return EXIT_FAILED, f"Movie CI for {sha} completed with {conclusion}."


def fetch_response(url: str, timeout_seconds: float) -> dict[str, Any]:
    request = Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": "watchlist-homelab-deployer",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    with urlopen(request, timeout=timeout_seconds) as response:
        payload = json.load(response)
    if not isinstance(payload, dict):
        raise ValueError("GitHub response must be a JSON object")
    return payload


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Allow deployment only after Movie CI succeeds for an exact push SHA."
    )
    parser.add_argument("--repository", required=True, help="Public GitHub owner/name")
    parser.add_argument("--workflow", default="movie-ci.yml", help="Workflow filename")
    parser.add_argument("--sha", required=True, help="Exact 40-character commit SHA")
    parser.add_argument("--timeout", type=float, default=15.0, help="GitHub API timeout")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        url = build_runs_url(args.repository, args.workflow, args.sha)
        payload = fetch_response(url, args.timeout)
        code, message = classify_response(payload, args.sha)
    except (HTTPError, URLError, TimeoutError, ValueError, json.JSONDecodeError) as error:
        print(f"Movie CI lookup failed: {error}", file=sys.stderr)
        return EXIT_ERROR

    output = sys.stdout if code in {EXIT_SUCCESS, EXIT_PENDING, EXIT_MISSING} else sys.stderr
    print(message, file=output)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
