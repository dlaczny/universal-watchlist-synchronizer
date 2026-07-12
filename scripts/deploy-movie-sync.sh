#!/usr/bin/env bash
set -Eeuo pipefail

umask 077

DEPLOY_ROOT="${DEPLOY_ROOT:-/opt/watchlist-prod}"
REPOSITORY_DIR="${REPOSITORY_DIR:-$DEPLOY_ROOT/repository}"
CONFIG_DIR="${CONFIG_DIR:-$DEPLOY_ROOT/config}"
DATA_DIR="${DATA_DIR:-$DEPLOY_ROOT/data}"
STATE_DIR="${STATE_DIR:-$DEPLOY_ROOT/state}"
DEPLOYER_DIR="${DEPLOYER_DIR:-$DEPLOY_ROOT/deployer}"
LOCK_FILE="${LOCK_FILE:-$DEPLOY_ROOT/deploy.lock}"
LAST_SUCCESSFUL_SHA_FILE="${LAST_SUCCESSFUL_SHA_FILE:-$STATE_DIR/last-successful.sha}"
PREVIOUS_SUCCESSFUL_SHA_FILE="${PREVIOUS_SUCCESSFUL_SHA_FILE:-$STATE_DIR/previous-successful.sha}"

GITHUB_REPOSITORY="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"
REPOSITORY_URL="${REPOSITORY_URL:?REPOSITORY_URL is required}"
DEPLOY_BRANCH="${DEPLOY_BRANCH:-main}"
MOVIE_CI_WORKFLOW="${MOVIE_CI_WORKFLOW:-movie-ci.yml}"
BACKEND_HEALTH_URL="${BACKEND_HEALTH_URL:-http://127.0.0.1:5000/healthz}"
LEGACY_COMPOSE_FILE="${LEGACY_COMPOSE_FILE:-/opt/watchlist-app/deploy/backend/compose.yaml}"
HEALTH_ATTEMPTS="${HEALTH_ATTEMPTS:-90}"
HEALTH_SLEEP_SECONDS="${HEALTH_SLEEP_SECONDS:-5}"
WATCHLIST_RUNTIME_UID="${WATCHLIST_RUNTIME_UID:-$(id -u)}"
WATCHLIST_RUNTIME_GID="${WATCHLIST_RUNTIME_GID:-$(id -g)}"

COMPOSE_RELATIVE_PATH="deploy/production/compose.yaml"
BACKEND_ENV_FILE="$CONFIG_DIR/backend.env"
WORKER_ENV_FILE="$CONFIG_DIR/worker.env"
WORKER_CONTAINER="watchlist-prod-worker"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
CI_CHECKER_PATH="${CI_CHECKER_PATH:-$DEPLOYER_DIR/check-movie-ci.py}"
if [[ ! -f "$CI_CHECKER_PATH" ]]; then
  CI_CHECKER_PATH="$SCRIPT_DIR/check-movie-ci.py"
fi

previous_sha=""
target_sha=""
legacy_was_running=false
cutover_started=false
rollback_in_progress=false

log() {
  printf '%s\n' "$*"
}

fail() {
  printf 'ERROR: %s\n' "$*" >&2
  return 1
}

is_sha() {
  [[ "$1" =~ ^[0-9a-f]{40}$ ]]
}

is_numeric_id() {
  [[ "$1" =~ ^[0-9]+$ ]]
}

compose_file() {
  printf '%s/%s\n' "$REPOSITORY_DIR" "$COMPOSE_RELATIVE_PATH"
}

update_stable_deployer() {
  [[ -f "$REPOSITORY_DIR/scripts/deploy-movie-sync.sh" ]] || return 1
  [[ -f "$REPOSITORY_DIR/scripts/check-movie-ci.py" ]] || return 1

  install -m 0750 "$REPOSITORY_DIR/scripts/deploy-movie-sync.sh" \
    "$DEPLOYER_DIR/deploy-movie-sync.sh.next" || return 1
  install -m 0640 "$REPOSITORY_DIR/scripts/check-movie-ci.py" \
    "$DEPLOYER_DIR/check-movie-ci.py.next" || return 1
  mv -f "$DEPLOYER_DIR/deploy-movie-sync.sh.next" "$DEPLOYER_DIR/deploy-movie-sync.sh"
  mv -f "$DEPLOYER_DIR/check-movie-ci.py.next" "$DEPLOYER_DIR/check-movie-ci.py"
}

wait_for_backend() {
  local attempt
  for ((attempt = 1; attempt <= HEALTH_ATTEMPTS; attempt++)); do
    if curl -fsS --max-time 5 "$BACKEND_HEALTH_URL" >/dev/null; then
      return 0
    fi
    sleep "$HEALTH_SLEEP_SECONDS"
  done
  return 1
}

wait_for_release() {
  local attempt worker_health
  for ((attempt = 1; attempt <= HEALTH_ATTEMPTS; attempt++)); do
    worker_health="$(
      docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}missing{{end}}' \
        "$WORKER_CONTAINER" 2>/dev/null || true
    )"
    if curl -fsS --max-time 5 "$BACKEND_HEALTH_URL" >/dev/null \
      && [[ "$worker_health" == "healthy" ]]; then
      return 0
    fi
    sleep "$HEALTH_SLEEP_SECONDS"
  done

  docker compose -f "$(compose_file)" ps >&2 || true
  return 1
}

restore_repository_checkout() {
  if is_sha "$previous_sha"; then
    git -C "$REPOSITORY_DIR" checkout --detach --force "$previous_sha" >/dev/null 2>&1 || true
  fi
}

rollback_release() {
  local failed_compose previous_compose
  rollback_in_progress=true
  failed_compose="$(compose_file)"
  log "Rolling back failed release $target_sha."

  WATCHLIST_RELEASE="$target_sha" docker compose -f "$failed_compose" down --remove-orphans || true

  if is_sha "$previous_sha"; then
    git -C "$REPOSITORY_DIR" checkout --detach --force "$previous_sha"
    previous_compose="$(compose_file)"
    export WATCHLIST_RELEASE="$previous_sha"
    docker compose -f "$previous_compose" config --quiet
    docker compose -f "$previous_compose" up -d --no-build --remove-orphans
    wait_for_release || fail "Previous production release did not recover."
    log "Restored production release $previous_sha."
    return 0
  fi

  if [[ "$legacy_was_running" == "true" && -f "$LEGACY_COMPOSE_FILE" ]]; then
    docker compose -f "$LEGACY_COMPOSE_FILE" up -d --no-build
    wait_for_backend || fail "Legacy backend did not recover."
    log "Restored the legacy backend deployment."
    return 0
  fi

  fail "No previous release was available for rollback."
}

handle_error() {
  local exit_code="$?" line_number="$1"
  trap - ERR
  if [[ "$rollback_in_progress" == "false" ]]; then
    if [[ "$cutover_started" == "true" ]]; then
      rollback_release || true
    else
      restore_repository_checkout
    fi
  fi
  printf 'Deployment failed at line %s.\n' "$line_number" >&2
  exit "$exit_code"
}

trap 'handle_error $LINENO' ERR

for command in git docker curl python3 flock install; do
  command -v "$command" >/dev/null || fail "Required command is unavailable: $command"
done

mkdir -p \
  "$DEPLOY_ROOT" \
  "$DEPLOY_ROOT/.docker" \
  "$CONFIG_DIR" \
  "$DATA_DIR/worker" \
  "$STATE_DIR" \
  "$DEPLOYER_DIR"
exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  log "Another deployment is already running."
  exit 0
fi

[[ -s "$BACKEND_ENV_FILE" ]] || fail "Missing backend environment file: $BACKEND_ENV_FILE"
[[ -s "$WORKER_ENV_FILE" ]] || fail "Missing worker environment file: $WORKER_ENV_FILE"
chmod 600 "$BACKEND_ENV_FILE" "$WORKER_ENV_FILE"
is_numeric_id "$WATCHLIST_RUNTIME_UID" || fail "WATCHLIST_RUNTIME_UID must be numeric."
is_numeric_id "$WATCHLIST_RUNTIME_GID" || fail "WATCHLIST_RUNTIME_GID must be numeric."

if [[ ! -d "$REPOSITORY_DIR/.git" ]]; then
  [[ ! -e "$REPOSITORY_DIR" || -z "$(ls -A "$REPOSITORY_DIR" 2>/dev/null)" ]] \
    || fail "Repository path exists and is not an empty Git checkout."
  git clone --no-checkout "$REPOSITORY_URL" "$REPOSITORY_DIR"
fi

git -C "$REPOSITORY_DIR" fetch --prune origin "$DEPLOY_BRANCH"
target_sha="$(git -C "$REPOSITORY_DIR" rev-parse "origin/$DEPLOY_BRANCH")"
is_sha "$target_sha" || fail "Remote branch did not resolve to a full commit SHA."
git -C "$REPOSITORY_DIR" cat-file -e "$target_sha^{commit}"

if [[ -s "$LAST_SUCCESSFUL_SHA_FILE" ]]; then
  previous_sha="$(tr -d '[:space:]' <"$LAST_SUCCESSFUL_SHA_FILE")"
  is_sha "$previous_sha" || fail "Last-successful release state is invalid."
fi

if [[ "$target_sha" == "$previous_sha" ]]; then
  if ! update_stable_deployer; then
    log "Release is already recorded; stable deployer repair will be retried on the next poll."
  fi
  log "Release $target_sha is already deployed."
  exit 0
fi

if python3 "$CI_CHECKER_PATH" \
  --repository "$GITHUB_REPOSITORY" \
  --workflow "$MOVIE_CI_WORKFLOW" \
  --sha "$target_sha"; then
  :
else
  checker_exit="$?"
  case "$checker_exit" in
    2 | 3 | 4)
      log "Release $target_sha is not eligible for deployment."
      exit 0
      ;;
    *)
      fail "Movie CI eligibility could not be verified."
      ;;
  esac
fi

git -C "$REPOSITORY_DIR" checkout --detach --force "$target_sha"
[[ -z "$(git -C "$REPOSITORY_DIR" status --porcelain)" ]] \
  || fail "Validated deployment checkout is not clean."

COMPOSE_FILE="$(compose_file)"
[[ -f "$COMPOSE_FILE" ]] || fail "Validated release has no production Compose file."

export WATCHLIST_CONFIG_DIR="$CONFIG_DIR"
export WATCHLIST_DATA_DIR="$DATA_DIR"
export WATCHLIST_RELEASE="$target_sha"
export WATCHLIST_RUNTIME_UID
export WATCHLIST_RUNTIME_GID

docker compose -f "$COMPOSE_FILE" config --quiet
docker builder prune -f --filter until=24h >/dev/null || true
docker compose -f "$COMPOSE_FILE" build --pull

if [[ -n "$LEGACY_COMPOSE_FILE" && -f "$LEGACY_COMPOSE_FILE" ]]; then
  if docker compose -f "$LEGACY_COMPOSE_FILE" ps --status running -q | grep -q .; then
    legacy_was_running=true
  fi
fi

cutover_started=true
if [[ "$legacy_was_running" == "true" ]]; then
  docker compose -f "$LEGACY_COMPOSE_FILE" down
fi

docker compose -f "$COMPOSE_FILE" up -d --no-build --remove-orphans
wait_for_release || fail "New backend or movie worker did not become healthy."

if is_sha "$previous_sha"; then
  previous_state_tmp="$PREVIOUS_SUCCESSFUL_SHA_FILE.tmp.$$"
  printf '%s\n' "$previous_sha" >"$previous_state_tmp"
  chmod 600 "$previous_state_tmp"
  mv -f "$previous_state_tmp" "$PREVIOUS_SUCCESSFUL_SHA_FILE"
fi

state_tmp="$LAST_SUCCESSFUL_SHA_FILE.tmp.$$"
printf '%s\n' "$target_sha" >"$state_tmp"
chmod 600 "$state_tmp"
mv -f "$state_tmp" "$LAST_SUCCESSFUL_SHA_FILE"
cutover_started=false

if ! update_stable_deployer; then
  log "Validated release is running; stable deployer update will be retried later."
fi

for image_name in watchlist-api watchlist-worker; do
  while IFS= read -r image_tag; do
    if is_sha "$image_tag" \
      && [[ "$image_tag" != "$target_sha" ]] \
      && [[ -z "$previous_sha" || "$image_tag" != "$previous_sha" ]]; then
      docker image rm "$image_name:$image_tag" >/dev/null 2>&1 || true
    fi
  done < <(docker image ls "$image_name" --format '{{.Tag}}')
done
docker builder prune -f --filter until=24h >/dev/null || true

log "Deployed movie release $target_sha successfully."
