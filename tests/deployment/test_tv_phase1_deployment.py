from __future__ import annotations

import json
from pathlib import Path
import re

import yaml


ROOT = Path(__file__).resolve().parents[2]
BACKEND_COMPOSE = ROOT / "deploy" / "backend" / "compose.yaml"
PRODUCTION_COMPOSE = ROOT / "deploy" / "production" / "compose.yaml"
APPSETTINGS = ROOT / "backend" / "src" / "Watchlist.Api" / "appsettings.json"
LOCAL_APPSETTINGS = (
    ROOT / "backend" / "src" / "Watchlist.Api" / "appsettings.Development.Local.example.json"
)
DOCKERFILE = ROOT / "backend" / "src" / "Watchlist.Api" / "Dockerfile"
DEPLOY_SCRIPT = ROOT / "scripts" / "deploy-movie-sync.sh"
CI_WORKFLOW = ROOT / ".github" / "workflows" / "movie-ci.yml"
EXAMPLES = (
    ROOT / "deploy" / "backend" / "watchlist-backend.env.example",
    ROOT / "deploy" / "production" / "backend.env.example",
    ROOT / "deploy" / "production" / "worker.env.example",
    ROOT / "deploy" / "local-cd" / "watchlist-deploy.env.example",
)
TV_GATES = (
    "TRAKT_HISTORY_SYNC_APPLY",
    "TV_SYNC_APPLY",
    "TV_SYNC_ADOPT_EXISTING_DESTINATIONS",
    "TV_SYNC_ALLOW_SEASON_FILE_DELETION",
    "TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION",
    "TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE",
)


def env_values(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if "=" in line and not line.lstrip().startswith("#"):
            key, value = line.split("=", 1)
            values[key] = value
    return values


def nested_values(value: object, prefix: str = "") -> dict[str, object]:
    if not isinstance(value, dict):
        return {prefix: value}

    return {
        key: nested
        for name, child in value.items()
        for key, nested in nested_values(child, f"{prefix}{name}").items()
    }


def read_compose(path: Path) -> dict[str, object]:
    content = yaml.safe_load(path.read_text(encoding="utf-8"))
    assert isinstance(content, dict)
    return content


def test_tv_phase_one_deployment_contract_is_secret_safe_and_mutation_locked() -> None:
    backend_compose = read_compose(BACKEND_COMPOSE)
    production_compose = read_compose(PRODUCTION_COMPOSE)
    dockerfile = DOCKERFILE.read_text(encoding="utf-8")
    deploy_script = DEPLOY_SCRIPT.read_text(encoding="utf-8")
    workflow = CI_WORKFLOW.read_text(encoding="utf-8")
    settings = json.loads(APPSETTINGS.read_text(encoding="utf-8"))
    local_settings = json.loads(LOCAL_APPSETTINGS.read_text(encoding="utf-8"))

    backend_api = backend_compose["services"]["watchlist-api"]
    production_api = production_compose["services"]["watchlist-api"]
    production_worker = production_compose["services"]["movie-sync-worker"]
    assert backend_api["env_file"] == ["${WATCHLIST_BACKEND_ENV_FILE:-./watchlist-backend.env}"]
    assert backend_api["volumes"] == ["watchlist_backend_keyring:/var/lib/watchlist/keyring"]
    assert backend_compose["volumes"] == {"watchlist_backend_keyring": None}
    assert production_api["volumes"] == [
        "${WATCHLIST_DATA_DIR:-./data}/backend/data-protection-keys:/var/lib/watchlist/keyring"
    ]
    assert production_api["user"] == "${WATCHLIST_RUNTIME_UID:-10001}:${WATCHLIST_RUNTIME_GID:-10001}"
    for service in (backend_api, production_api, production_worker):
        assert service["environment"].items() >= {gate: "false" for gate in TV_GATES}.items()
    assert "install -d -o app -g app -m 0700 /var/lib/watchlist/keyring" in dockerfile
    assert "$DATA_DIR/backend/data-protection-keys" in deploy_script
    assert "chown \"$WATCHLIST_RUNTIME_UID:$WATCHLIST_RUNTIME_GID\"" in deploy_script
    assert 'chmod 700 "$KEYRING_DIR"' in deploy_script

    assert settings["DataProtection"] == {
        "KeyRingPath": "/var/lib/watchlist/keyring",
        "ApplicationName": "watchlist-api",
    }
    assert settings["Trakt"]["ClientId"] == ""
    assert settings["Trakt"]["ClientSecret"] == ""
    assert settings["Trakt"]["ActivityPollInterval"] == "00:05:00"
    assert settings["Trakt"]["FullSyncInterval"] == "01:00:00"
    assert settings["Tmdb"]["ProviderRegion"] == "PL"
    assert settings["Tmdb"]["OwnedProviderIds"] == [119, 1899, 1773]
    assert settings["Tmdb"]["ProviderCacheLifetime"] == "1.00:00:00"
    assert local_settings["DataProtection"]["KeyRingPath"] == "../../../../.artifacts/data-protection-keys"
    assert settings["MongoDb"].items() >= {
        "TraktConnectionsCollectionName": "trakt_connections",
        "TvShowsCollectionName": "tv_shows",
        "TvLifecycleEventsCollectionName": "tv_lifecycle_events",
        "TvSyncManifestsCollectionName": "tv_sync_manifests",
    }.items()

    backend_env = env_values(EXAMPLES[0])
    production_backend_env = env_values(EXAMPLES[1])
    worker_env = env_values(EXAMPLES[2])
    for env in (backend_env, production_backend_env):
        assert env["DataProtection__KeyRingPath"] == "/var/lib/watchlist/keyring"
        assert env["Tmdb__ProviderRegion"] == "PL"
        assert [env[f"Tmdb__OwnedProviderIds__{index}"] for index in range(3)] == ["119", "1899", "1773"]
        assert env["Tmdb__ProviderCacheLifetime"] == "1.00:00:00"
        assert env["Trakt__ClientId"] == ""
        assert env["Trakt__ClientSecret"] == ""
        for gate in TV_GATES:
            assert env[gate] == "false"
    for gate in TV_GATES:
        assert worker_env[gate] == "false"

    sensitive_keys = re.compile(r"(?:token|api[_-]?key|sync[_-]?key|clientsecret)", re.IGNORECASE)
    for example in EXAMPLES:
        for key, value in env_values(example).items():
            if sensitive_keys.search(key):
                assert value == "", f"{example.name} commits a usable value for {key}"
    for key, value in nested_values(local_settings).items():
        if sensitive_keys.search(key):
            assert value == "", f"{LOCAL_APPSETTINGS.name} commits a usable value for {key}"

    assert "dotnet test backend/Watchlist.sln --configuration Release --no-restore" in workflow
    assert "working-directory: workers/vod-filter" in workflow
    assert "python -m pytest -q" in workflow
    assert 'python -m pip install "pytest>=8.0.0" "PyYAML>=6.0.0"' in workflow
    assert "python -m pytest tests/deployment/test_tv_phase1_deployment.py -q" in workflow
