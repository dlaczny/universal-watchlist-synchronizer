from __future__ import annotations

import os
from pathlib import Path
import shutil
import subprocess


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy-movie-sync.sh"
PRODUCTION_COMPOSE = ROOT / "deploy" / "production" / "compose.yaml"
BACKEND_DOCKERFILE = ROOT / "backend" / "src" / "Watchlist.Api" / "Dockerfile"
WORKER_DOCKERFILE = ROOT / "workers" / "vod-filter" / "Dockerfile"
SERVICE = ROOT / "deploy" / "local-cd" / "systemd" / "watchlist-deploy.service"
TIMER = ROOT / "deploy" / "local-cd" / "systemd" / "watchlist-deploy.timer"
SHA = "a" * 40
PREVIOUS_SHA = "b" * 40


def git_bash() -> str:
    found = shutil.which("bash")
    if found and not (os.name == "nt" and found.lower().endswith("system32\\bash.exe")):
        return found
    candidate = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe"
    if candidate.exists():
        return str(candidate)
    raise AssertionError("bash is required for deployment contract tests")


def bash_path(path: Path) -> str:
    resolved = path.resolve()
    if os.name != "nt":
        return str(resolved)
    drive = resolved.drive.rstrip(":").lower()
    relative_parts = resolved.parts[1:]
    return f"/{drive}/" + "/".join(relative_parts)


def test_shell_and_systemd_security_contracts() -> None:
    text = SCRIPT.read_text(encoding="utf-8")
    compose = PRODUCTION_COMPOSE.read_text(encoding="utf-8")
    backend_dockerfile = BACKEND_DOCKERFILE.read_text(encoding="utf-8")
    worker_dockerfile = WORKER_DOCKERFILE.read_text(encoding="utf-8")
    service = SERVICE.read_text(encoding="utf-8")
    timer = TIMER.read_text(encoding="utf-8")

    assert "flock" in text
    assert "checkout --detach" in text
    assert "check-movie-ci.py" in text
    assert "WATCHLIST_RELEASE" in text
    assert "config --quiet" in text
    assert "last-successful.sha" in text
    assert "rollback" in text.lower()
    assert "builder prune" in text
    assert "WATCHLIST_RUNTIME_UID" in text
    assert "WATCHLIST_RUNTIME_GID" in text
    assert 'HEALTH_ATTEMPTS="${HEALTH_ATTEMPTS:-240}"' in text
    assert '[[ -s "$WORKER_HEARTBEAT_FILE" ]]' in text
    assert 'rm -f "$WORKER_HEARTBEAT_FILE"' in text
    assert 'user: "${WATCHLIST_RUNTIME_UID' in compose
    assert "${WATCHLIST_RUNTIME_GID" in compose
    assert "COPY --from=build --chown=app:app /app/publish ." in backend_dockerfile
    assert "COPY --from=builder --chown=watchlist:watchlist --chmod=0555" in worker_dockerfile
    assert worker_dockerfile.count("--chmod=0555") == 3
    assert "set -x" not in text
    assert "printenv" not in text
    assert "env |" not in text
    assert "User=watchlist" in service
    assert "Group=watchlist" in service
    assert "UMask=0077" in service
    assert "DOCKER_CONFIG=/opt/watchlist-prod/.docker" in service
    assert "RandomizedDelaySec=" in timer


def run_fake_deployer(
    tmp_path: Path,
    *,
    previous_sha: str = "",
    fail_first_healthcheck: bool = False,
    stale_worker_heartbeat: bool = False,
) -> tuple[subprocess.CompletedProcess[str], Path, Path]:
    deploy_root = tmp_path / "watchlist-prod"
    repository = deploy_root / "repository"
    config = deploy_root / "config"
    data = deploy_root / "data/worker"
    deployer = deploy_root / "deployer"
    boundary_config = tmp_path / "fake-boundaries.sh"
    command_log = tmp_path / "commands.log"
    health_state = tmp_path / "health-state"
    worker_heartbeat = data / "last-run.json"

    for directory in (repository / ".git", config, data, deployer):
        directory.mkdir(parents=True, exist_ok=True)
    (repository / "deploy/production").mkdir(parents=True)
    (repository / "scripts").mkdir(parents=True)
    shutil.copy2(ROOT / "deploy/production/compose.yaml", repository / "deploy/production/compose.yaml")
    shutil.copy2(SCRIPT, repository / "scripts/deploy-movie-sync.sh")
    shutil.copy2(ROOT / "scripts/check-movie-ci.py", repository / "scripts/check-movie-ci.py")
    (config / "backend.env").write_text("Sync__ApiKey=test-only\n", encoding="utf-8")
    (config / "worker.env").write_text("WATCHLIST_APP_SYNC_KEY=test-only\n", encoding="utf-8")
    if previous_sha:
        state = deploy_root / "state"
        state.mkdir()
        (state / "last-successful.sha").write_text(f"{previous_sha}\n", encoding="utf-8")
    if stale_worker_heartbeat:
        worker_heartbeat.write_text('{"status":"partial"}\n', encoding="utf-8")

    boundary_config.write_text(
        "git() {\n"
        "  printf 'git %s\\n' \"$*\" >> \"$COMMAND_LOG\"\n"
        f"  if [[ \"$*\" == *'rev-parse origin/main'* ]]; then printf '%s\\n' '{SHA}'; fi\n"
        "  return 0\n"
        "}\n"
        "docker() {\n"
        "printf 'docker %s\\n' \"$*\" >> \"$COMMAND_LOG\"\n"
        "if [[ \"$*\" == *'compose'*'up -d'* ]]; then\n"
        "  if [[ -s \"$WORKER_HEARTBEAT_FILE\" ]]; then\n"
        "    printf 'stale worker heartbeat reused\\n' >> \"$COMMAND_LOG\"\n"
        "  fi\n"
        "  mkdir -p \"$(dirname \"$WORKER_HEARTBEAT_FILE\")\"\n"
        "  printf '{\"status\":\"reconciliation\"}\\n' > \"$WORKER_HEARTBEAT_FILE\"\n"
        "fi\n"
        "if [[ \"$1\" == 'inspect' ]]; then\n"
        "  count=0\n"
        "  if [[ -s \"$HEALTH_STATE_FILE\" ]]; then read -r count < \"$HEALTH_STATE_FILE\"; fi\n"
        "  count=$((count + 1))\n"
        "  printf '%s\\n' \"$count\" > \"$HEALTH_STATE_FILE\"\n"
        "  if [[ \"${FAIL_FIRST_HEALTHCHECK:-false}\" == 'true' && \"$count\" -eq 1 ]]; then\n"
        "    printf 'unhealthy\\n'\n"
        "  else\n"
        "    printf 'healthy\\n'\n"
        "  fi\n"
        "fi\n"
        "return 0\n"
        "}\n"
        "curl() { return 0; }\n"
        "flock() { return 0; }\n"
        "python3() { return 0; }\n",
        encoding="utf-8",
        newline="\n",
    )

    bash = git_bash()
    environment = os.environ.copy()
    environment.update(
        {
            "BASH_ENV": bash_path(boundary_config),
            "COMMAND_LOG": bash_path(command_log),
            "HEALTH_STATE_FILE": bash_path(health_state),
            "FAIL_FIRST_HEALTHCHECK": str(fail_first_healthcheck).lower(),
            "WORKER_HEARTBEAT_FILE": bash_path(worker_heartbeat),
            "DEPLOY_ROOT": bash_path(deploy_root),
            "REPOSITORY_DIR": bash_path(repository),
            "CONFIG_DIR": bash_path(config),
            "DATA_DIR": bash_path(deploy_root / "data"),
            "DEPLOYER_DIR": bash_path(deployer),
            "GITHUB_REPOSITORY": "dlaczny/universal-watchlist-synchronizer",
            "REPOSITORY_URL": "https://github.com/dlaczny/universal-watchlist-synchronizer.git",
            "BACKEND_HEALTH_URL": "http://127.0.0.1:5000/healthz",
            "LEGACY_COMPOSE_FILE": "",
            "HEALTH_ATTEMPTS": "1",
            "HEALTH_SLEEP_SECONDS": "0",
        }
    )

    result: subprocess.CompletedProcess[str] = subprocess.run(
        [bash, str(SCRIPT)],
        cwd=ROOT,
        env=environment,
        text=True,
        capture_output=True,
        timeout=30,
        check=False,
    )
    return result, deploy_root, command_log


def test_deployer_runs_validated_release_with_fake_boundaries(tmp_path: Path) -> None:
    result, deploy_root, command_log = run_fake_deployer(tmp_path)

    assert result.returncode == 0, result.stderr
    assert (deploy_root / "state/last-successful.sha").read_text(encoding="utf-8").strip() == SHA
    assert (deploy_root / "data/backend/data-protection-keys").is_dir()
    log = command_log.read_text(encoding="utf-8")
    assert f"checkout --detach --force {SHA}" in log
    assert "compose" in log and "build --pull" in log
    assert "compose" in log and "up -d --no-build" in log
    assert "test-only" not in result.stdout
    assert "test-only" not in result.stderr


def test_deployer_discards_stale_worker_heartbeat_before_cutover(tmp_path: Path) -> None:
    result, _, command_log = run_fake_deployer(
        tmp_path,
        stale_worker_heartbeat=True,
    )

    assert result.returncode == 0, result.stderr
    assert "stale worker heartbeat reused" not in command_log.read_text(
        encoding="utf-8"
    )


def test_failed_release_rolls_back_previous_release(tmp_path: Path) -> None:
    result, deploy_root, command_log = run_fake_deployer(
        tmp_path,
        previous_sha=PREVIOUS_SHA,
        fail_first_healthcheck=True,
    )

    assert result.returncode != 0
    assert (deploy_root / "state/last-successful.sha").read_text(encoding="utf-8").strip() == PREVIOUS_SHA
    log = command_log.read_text(encoding="utf-8")
    assert "down --remove-orphans" in log
    assert f"checkout --detach --force {PREVIOUS_SHA}" in log
    assert f"Restored production release {PREVIOUS_SHA}." in result.stdout


def test_successful_release_records_current_and_previous_sha(tmp_path: Path) -> None:
    result, deploy_root, _ = run_fake_deployer(
        tmp_path,
        previous_sha=PREVIOUS_SHA,
    )

    assert result.returncode == 0, result.stderr
    assert (deploy_root / "state/last-successful.sha").read_text(encoding="utf-8").strip() == SHA
    assert (
        deploy_root / "state/previous-successful.sha"
    ).read_text(encoding="utf-8").strip() == PREVIOUS_SHA


def test_already_deployed_release_repairs_stable_deployer_copy(tmp_path: Path) -> None:
    result, deploy_root, _ = run_fake_deployer(
        tmp_path,
        previous_sha=SHA,
    )

    assert result.returncode == 0, result.stderr
    assert (deploy_root / "deployer/deploy-movie-sync.sh").is_file()
    assert (deploy_root / "deployer/check-movie-ci.py").is_file()
