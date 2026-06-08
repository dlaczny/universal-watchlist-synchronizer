#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="${APP_ROOT:-/opt/watchlist-app}"
ANDROID_DIR="${ANDROID_DIR:-$APP_ROOT/android}"
BACKEND_COMPOSE_DIR="${BACKEND_COMPOSE_DIR:-$APP_ROOT/deploy/backend}"
BACKEND_HEALTH_URL="${BACKEND_HEALTH_URL:?BACKEND_HEALTH_URL is required}"
TV_ADB_SERIAL="${TV_ADB_SERIAL:?TV_ADB_SERIAL is required, for example 192.168.50.100:34763}"
WATCHLIST_API_BASE_URL="${WATCHLIST_API_BASE_URL:?WATCHLIST_API_BASE_URL is required}"
PACKAGE_NAME="${PACKAGE_NAME:-com.watchlist.tv}"
BUILD_TYPE="${BUILD_TYPE:-debug}"
SKIP_ANDROID_DEPLOY="${SKIP_ANDROID_DEPLOY:-false}"

cd "$APP_ROOT"

git fetch origin main
git checkout main

if [ -n "$(git status --porcelain)" ]; then
  echo "Refusing to deploy from a dirty checkout: $APP_ROOT" >&2
  git status --short >&2
  exit 1
fi

git pull --ff-only origin main

cd "$BACKEND_COMPOSE_DIR"
docker compose up -d --build

for attempt in $(seq 1 30); do
  if curl -fsS "$BACKEND_HEALTH_URL" >/dev/null; then
    break
  fi

  if [ "$attempt" -eq 30 ]; then
    echo "Backend health check failed: $BACKEND_HEALTH_URL" >&2
    docker compose ps >&2
    exit 1
  fi

  sleep 2
done

if [ "$SKIP_ANDROID_DEPLOY" = "true" ]; then
  echo "Backend deployed. Android deployment skipped."
  exit 0
fi

cd "$ANDROID_DIR"

if [ "$BUILD_TYPE" = "release" ]; then
  ./gradlew assembleRelease -PwatchlistApiBaseUrl="$WATCHLIST_API_BASE_URL"
  APK_PATH="$ANDROID_DIR/app/build/outputs/apk/release/app-release.apk"
else
  ./gradlew assembleDebug -PwatchlistApiBaseUrl="$WATCHLIST_API_BASE_URL"
  APK_PATH="$ANDROID_DIR/app/build/outputs/apk/debug/app-debug.apk"
fi

adb connect "$TV_ADB_SERIAL"
adb -s "$TV_ADB_SERIAL" install -r "$APK_PATH"
adb -s "$TV_ADB_SERIAL" shell monkey -p "$PACKAGE_NAME" 1
