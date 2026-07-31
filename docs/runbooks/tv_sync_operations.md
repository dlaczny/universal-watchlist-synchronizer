---
type: Runbook
title: TV Sync Operations
description: Safely inspect TV generations and stage the report-first reversible Sonarr and Plex Watchlist workflow.
tags:
  - tv
  - trakt
  - sonarr
  - plex
  - operations
timestamp: 2026-07-31T00:00:00Z
version: 1.2.0
---

# Safety Boundary

The backend remains the only Trakt and TMDB client. The TV worker consumes one
immutable schema-v2 export and independently collects Sonarr, Plex TV-library,
Plex Watchlist, and SQLite state before it plans. Do not call Trakt from the
worker or infer a destination action from a missing export: `404` means no
published generation, not an empty desired state.

Only `watchlist-api` publishes TV generations to MongoDB. The worker has no
MongoDB generation-write path: it reads the backend export and writes only its
local SQLite audit plus destination reports/actions. It may remain running
during a generation backup after the API is stopped, provided no other backend
instance or manual sync writer exists.

Keep the committed and host defaults false:

```text
TV_SYNC_ENABLED=false
TV_SYNC_APPLY=false
TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false
TRAKT_HISTORY_SYNC_APPLY=false
TV_SYNC_ALLOW_SEASON_FILE_DELETION=false
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false
```

`TV_SYNC_ENABLED=true` enables TV collection and reporting only.
`TV_SYNC_APPLY=true` is only a host gate; an action also requires an explicit
`sync_tv.py --apply`. Adoption additionally requires
`TV_SYNC_ADOPT_EXISTING_DESTINATIONS=true`. Never set a history or cleanup
switch true for this release.

Only the following destination transitions are in scope: exact-TVDB Sonarr
lookup/add, series and selected-season monitoring, selected-season aired and
unwatched episode search, and exact Plex Watchlist add/remove. Plex library
media, Plex episode history, Trakt history, movie state, Android, and Sonarr
file, season, or whole-series removal are prohibited.

# Inspect The Backend Generation

Use the host-local sync key without printing it. Sync and integration routes
require `X-Watchlist-Sync-Key`; browse, status, and exports do not.

```bash
curl -fsS -H "X-Watchlist-Sync-Key: $SYNC_KEY" \
  http://127.0.0.1:5000/api/integrations/trakt/status
curl -fsS -X POST -H "X-Watchlist-Sync-Key: $SYNC_KEY" \
  http://127.0.0.1:5000/api/sync/tv
curl -fsS http://127.0.0.1:5000/api/sync/status
curl -fsS http://127.0.0.1:5000/api/export/tv/sync-state
```

Confirm the export has `schemaVersion: "2"` and a
`destinationSync` object. `destinationSync.capable=false`, a stale generation,
any duplicate identity, or a collection error is a stop condition. The retained
`mutationCapable=false` value is expected and is not a reason to change a
history or cleanup flag.

TVDB is mandatory for Sonarr. Missing, nonpositive, or conflicting TVDB IDs
are report blockers, not candidates for title/year matching. Plex needs an
exact TVDB GUID, or an exact TMDB/IMDb GUID verified by the backend as the same
TVDB show. An unknown or ambiguous Plex row must be reported and preserved.

The worker selects regular seasons only: Season 1 for an unstarted show;
otherwise Trakt's next-episode season, or the next aired numbered season after
the latest fully watched season. Its selected season must have its own current
Poland provider observation. `unknown` and `stale` block a new Sonarr action;
only `confirmed_unavailable` permits a Sonarr add/monitor/search. Plex
Watchlist is desired when the selected season is `available` or an exact Plex
TV-library episode exists, and an exact Plex Watchlist row may be removed only
when neither fact is true.

# Reversible destination rollout

Perform each stage only after the previous stage's redacted report has been
reviewed by a human. Run these commands on the deployment host from the clean,
exact-SHA checkout. The examples update protected host configuration without
printing a credential; retain every history and cleanup setting above as
`false` throughout.

Set the shared Compose variables once for the commands below:

```bash
cd /opt/watchlist-prod/repository
export WATCHLIST_CONFIG_DIR=/opt/watchlist-prod/config
export WATCHLIST_DATA_DIR=/opt/watchlist-prod/data
export WATCHLIST_RELEASE="$(cat /opt/watchlist-prod/state/last-successful.sha)"
```

## 1. Deploy Disabled

Leave `TV_SYNC_ENABLED=false` in `/opt/watchlist-prod/config/worker.env`,
deploy the exact-SHA release, and verify the movie workflow remains healthy.
Do not record this as an enabled TV stage.

```bash
sudo systemctl start watchlist-deploy.service
sudo systemctl status watchlist-deploy.service --no-pager
docker inspect --format '{{.State.Health.Status}}' watchlist-prod-worker
```

## 2. Report-Only Collection

In the protected `worker.env`, set `TV_SYNC_ENABLED=true`, retain
`TV_SYNC_APPLY=false` and `TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false`, then
recreate the worker and run one explicit report-only pass:

```bash
docker compose -f deploy/production/compose.yaml up -d --no-build --force-recreate movie-sync-worker
docker compose -f deploy/production/compose.yaml exec -T movie-sync-worker \
  python sync_tv.py --once --report-dir /app/data/reports
ls -lt /opt/watchlist-prod/data/worker/reports
```

Review the newest JSON and Markdown reports before any configuration change.
They must show one generation ID, redacted counts and stable reasons, exact
identity outcomes, selected seasons, provider states, action counts, and no
collection/policy blocker other than `tv_apply_disabled`. A blocked, stale, or
uncertain plan is not eligible for the next stage.

## 3. Supervised Adoption

This stage records origin only; it must not change Sonarr monitoring or create
a destination row. After approving the report-only plan, set both
`TV_SYNC_APPLY=true` and `TV_SYNC_ADOPT_EXISTING_DESTINATIONS=true` in the
protected `worker.env`, recreate the worker, then make one supervised request:

```bash
docker compose -f deploy/production/compose.yaml up -d --no-build --force-recreate movie-sync-worker
docker compose -f deploy/production/compose.yaml exec -T movie-sync-worker \
  python sync_tv.py --once --apply --report-dir /app/data/reports
```

Review the resulting report and SQLite audit before proceeding. Each adopted
exact Sonarr row must be recorded with `manual` origin. Stop if an identity,
provider, collection, threshold, or policy blocker appears.

## 4. Supervised Reversible Apply And Convergence

After adoption review, set `TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false` while
keeping `TV_SYNC_ENABLED=true` and `TV_SYNC_APPLY=true`. Recreate the worker,
run one supervised apply, and review its destination outcomes:

```bash
docker compose -f deploy/production/compose.yaml up -d --no-build --force-recreate movie-sync-worker
docker compose -f deploy/production/compose.yaml exec -T movie-sync-worker \
  python sync_tv.py --once --apply --report-dir /app/data/reports
```

Only after this review passes, run the same command a second time. The second
report should converge to `keep` or `skip` decisions. If it does not, leave
apply enabled only long enough to preserve evidence, return to report-only by
setting `TV_SYNC_APPLY=false`, and investigate from fresh collection rather
than replaying an old plan.

# Device Authorization And Key-Ring Recovery

Start device authorization only when the protected status says disconnected,
`refresh_required`, revoked, or unreadable. Complete the user interaction at
the returned verification URL before expiry. Never put a device/user code,
access/refresh token, client secret, protected ciphertext, sync key, Plex
token, or connection string in a ticket, report, shell history, or ledger.

```bash
curl -fsS -X POST -H "X-Watchlist-Sync-Key: $SYNC_KEY" \
  http://127.0.0.1:5000/api/integrations/trakt/device/start
```

For an unreadable connection, preserve
`/opt/watchlist-prod/data/backend/data-protection-keys` for recovery and
audit. Verify the configured `DataProtection__KeyRingPath`, application name,
ownership, and mounted volume. If the original keys cannot be restored,
disconnect the unusable singleton through the protected endpoint and complete a
new device authorization; do not delete the old key-ring as a first response.

Back up the key-ring through the host's protected backup process, retain old
keys through the maximum token lifetime plus rollback window, and restart one
API instance after validating it can read connection status. Replacing the
whole key-ring directory or application name is a destructive credential
migration and requires supervised reconnection.

# TV Generation Retention Rollout

Complete this procedure before the first retention-enabled production
deployment. Atlas backups are currently inactive, so a restricted local logical
dump is a mandatory rollback artifact. Local implementation and CI must not
perform production cleanup. Retention may run in production only through the
fully validated, deployed backend described here, and only after the dump
succeeds. This procedure follows the official MongoDB
[`mongodump` reference](https://www.mongodb.com/docs/database-tools/mongodump/)
and
[`mongodump` behavior](https://www.mongodb.com/docs/database-tools/mongodump/mongodump-behavior/)
and
[`mongorestore` behavior](https://www.mongodb.com/docs/database-tools/mongorestore/mongorestore-behavior-access-usage/)
guidance. Use MongoDB Database Tools exactly `100.17.0`: the official
compatibility table lists that release as supporting MongoDB Server 8.0, and
MongoDB requires the same Database Tools release for dump and restore. The
commands below reject a different tools release. The credential-bearing URI is
supplied through a protected YAML `--config` file rather than process
arguments. The disposable restore image is pinned to MongoDB Server 8.0.28 by
immutable multi-platform digest; do not substitute a floating `mongo:8` tag.

## 1. Record The Pre-Quiescence Inventory

Before quiescing any writer, load the production URI from its protected source
without echoing it. Connect through a protected `mongosh` workflow that does
not place the credential-bearing URI in shell history or an evidence file,
select `watchlist`, and record the output of these read-only operations outside
Git:

```javascript
db.runCommand({ atlasSize: 1 })
db.tv_sync_manifests.countDocuments({})
db.tv_shows.countDocuments({})
db.tv_lifecycle_events.countDocuments({})
db.tv_sync_manifests.findOne(
  { _id: "published-tv", documentKind: "pointer" },
  { generationId: 1, publishedAt: 1 }
)
```

This pre-quiescence inventory is capacity and comparison evidence, not the
logical dump's coherence baseline. The record must contain total Atlas
data-plus-index usage, total document counts for all three TV collections
(including legacy and noncurrent rows), and the current published pointer. Do
not record the connection URI, credentials, token material, or raw show
content. Also record the exact current release from
`/opt/watchlist-prod/state/last-successful.sha`; this is the release that must
be restarted after the dump and the exact previous release for the later
retention deployment.

## 2. Create And Verify The Restricted Logical Dump

Atlas M0/shared deployments do not support `mongodump --oplog`. Writes during a
dump would therefore prevent a point-in-time snapshot. Stop every TV-generation
writer before the first collection dump and hold that quiescence through BSON
checks and the isolated restore verification. With no writer, the three
separate collection dumps form one coherent application snapshot.

Run the following as one guarded Bash procedure. It records the pointer through
the public read export before quiescence, stops the Compose `watchlist-api`
service from the production checkout, and requires an operator confirmation
that no other backend container, host process, scheduled/manual
`POST /api/sync/tv`, or `POST /api/sync/all` writer remains. The worker can
remain running because it cannot publish a MongoDB generation.

```bash
set -Eeuo pipefail

WATCHLIST_PRIOR_UMASK="$(umask)"
WATCHLIST_MONGO_CONFIG=""
WATCHLIST_MONGO_URI=""
WATCHLIST_BACKUP_DIR=""
WATCHLIST_RESTORE_CONTAINER=""
WATCHLIST_RESTORE_IMAGE="mongo:8.0.28-noble@sha256:277f9152905bd1f32d3ece4526e0f90906dc238f7133b24ede8446ac9740b76d"
WATCHLIST_PRE_QUIESCENCE_POINTER=""
WATCHLIST_QUIESCED_POINTER=""
WATCHLIST_QUIESCED_TV_SHOWS_TOTAL=""
WATCHLIST_QUIESCED_TV_SYNC_MANIFESTS_TOTAL=""
WATCHLIST_QUIESCED_TV_LIFECYCLE_EVENTS_TOTAL=""
WATCHLIST_SOURCE_METADATA_FILE=""
WATCHLIST_API_RESTARTED=0

watchlist_retention_cleanup() {
  local status="${1:-$?}"
  trap - EXIT INT TERM
  if [[ "$status" -ne 0 && "${WATCHLIST_API_RESTARTED:-0}" -eq 1 ]]; then
    docker compose -f deploy/production/compose.yaml \
      stop watchlist-api || true
  fi
  unset WATCHLIST_MONGO_URI
  if [[ -n "${WATCHLIST_MONGO_CONFIG:-}" ]]; then
    rm -f -- "$WATCHLIST_MONGO_CONFIG"
  fi
  if [[ -n "${WATCHLIST_RESTORE_CONTAINER:-}" ]]; then
    docker rm -f "$WATCHLIST_RESTORE_CONTAINER" >/dev/null 2>&1 || true
  fi
  if [[ -n "${WATCHLIST_PRIOR_UMASK:-}" ]]; then
    umask "$WATCHLIST_PRIOR_UMASK"
  fi
  unset WATCHLIST_MONGO_CONFIG WATCHLIST_BACKUP_DIR
  unset WATCHLIST_RESTORE_CONTAINER WATCHLIST_RESTORE_IMAGE
  unset WATCHLIST_PRIOR_UMASK WATCHLIST_PRE_QUIESCENCE_POINTER
  unset WATCHLIST_QUIESCED_POINTER WATCHLIST_SOURCE_METADATA_FILE
  unset WATCHLIST_QUIESCED_TV_SHOWS_TOTAL
  unset WATCHLIST_QUIESCED_TV_SYNC_MANIFESTS_TOTAL
  unset WATCHLIST_QUIESCED_TV_LIFECYCLE_EVENTS_TOTAL
  unset WATCHLIST_API_RESTARTED
  exit "$status"
}

trap 'watchlist_retention_cleanup $?' EXIT
trap 'watchlist_retention_cleanup 130' INT
trap 'watchlist_retention_cleanup 143' TERM

umask 077
cd /opt/watchlist-prod/repository
export WATCHLIST_CONFIG_DIR=/opt/watchlist-prod/config
export WATCHLIST_DATA_DIR=/opt/watchlist-prod/data
export WATCHLIST_RELEASE
WATCHLIST_RELEASE="$(
  tr -d '[:space:]' </opt/watchlist-prod/state/last-successful.sha
)"
[[ "$WATCHLIST_RELEASE" =~ ^[0-9a-f]{40}$ ]]

for command in bsondump chmod curl date docker install mktemp mongodump \
  mongosh python3 sleep tr; do
  command -v "$command" >/dev/null
done

require_database_tools_100_17_0() {
  "$1" --version | python3 -c '
import re
import sys

match = re.search(r"version:\s*v?([0-9]+\.[0-9]+\.[0-9]+)", sys.stdin.read())
if match is None or match.group(1) != "100.17.0":
    raise SystemExit("MongoDB Database Tools exactly 100.17.0 are required")
'
}
require_database_tools_100_17_0 mongodump
require_database_tools_100_17_0 bsondump

docker pull "$WATCHLIST_RESTORE_IMAGE" >/dev/null
docker run --rm --network none "$WATCHLIST_RESTORE_IMAGE" \
  mongorestore --version |
  python3 -c '
import re
import sys

match = re.search(r"version:\s*v?([0-9]+\.[0-9]+\.[0-9]+)", sys.stdin.read())
if match is None or match.group(1) != "100.17.0":
    raise SystemExit("pinned restore image must contain mongorestore 100.17.0")
'
docker run --rm --network none "$WATCHLIST_RESTORE_IMAGE" \
  mongod --version |
  python3 -c '
import re
import sys

match = re.search(r"db version v([0-9]+\.[0-9]+\.[0-9]+)", sys.stdin.read())
if match is None or match.group(1) != "8.0.28":
    raise SystemExit("pinned restore image must contain MongoDB Server 8.0.28")
'

WATCHLIST_PRE_QUIESCENCE_POINTER="$(
  curl -fsS http://127.0.0.1:5000/api/export/tv/sync-state |
    python3 -c '
import json
import sys

value = json.load(sys.stdin).get("generationId")
if not isinstance(value, str) or not value:
    raise SystemExit("published generationId is missing")
print(value)
'
)"
printf 'Pre-quiescence published generation: %s\n' \
  "$WATCHLIST_PRE_QUIESCENCE_POINTER"

docker compose -f deploy/production/compose.yaml stop watchlist-api
[[ -z "$(
  docker compose -f deploy/production/compose.yaml \
    ps --status running -q watchlist-api
)" ]]
docker ps --format '{{.Names}} {{.Image}}'
read -rp \
  'Type QUIESCED after confirming no other backend or manual TV writer: ' \
  WATCHLIST_WRITER_CONFIRMATION
[[ "$WATCHLIST_WRITER_CONFIRMATION" == "QUIESCED" ]]
unset WATCHLIST_WRITER_CONFIRMATION

WATCHLIST_BACKUP_DIR="$(
  printf '/opt/watchlist-prod/backups/tv-retention-%s' \
    "$(date -u +%Y%m%dT%H%M%SZ)"
)"
install -d -m 0700 "$WATCHLIST_BACKUP_DIR"
WATCHLIST_MONGO_CONFIG="$(
  mktemp /opt/watchlist-prod/config/mongodump-tv-retention.XXXXXX.yml
)"
chmod 0600 "$WATCHLIST_MONGO_CONFIG"

read -rsp 'MongoDB URI: ' WATCHLIST_MONGO_URI
printf '\n'
[[ -n "$WATCHLIST_MONGO_URI" ]]
printf '%s' "$WATCHLIST_MONGO_URI" |
  python3 -c '
import json
import sys

sys.stdout.write("uri: " + json.dumps(sys.stdin.read()) + "\n")
' >"$WATCHLIST_MONGO_CONFIG"

WATCHLIST_SOURCE_METADATA_FILE="$WATCHLIST_BACKUP_DIR/source-metadata.json"
WATCHLIST_MONGO_URI="$WATCHLIST_MONGO_URI" \
  mongosh --nodb --quiet --eval '
const client = new Mongo(process.env.WATCHLIST_MONGO_URI);
const admin = client.getDB("admin");
const source = client.getDB("watchlist");
const buildInfo = admin.runCommand({ buildInfo: 1 });
if (buildInfo.ok !== 1 || !Array.isArray(buildInfo.versionArray)) {
  throw new Error("source build information is unavailable");
}
const serverMajor = buildInfo.versionArray[0];
if (serverMajor !== 8) {
  throw new Error("source MongoDB major version must be 8");
}
const pointer = source.tv_sync_manifests.findOne({
  _id: "published-tv",
  documentKind: "pointer"
});
if (!pointer || typeof pointer.generationId !== "string" ||
    pointer.generationId.length === 0) {
  throw new Error("quiesced published pointer is missing or invalid");
}
print(JSON.stringify({
  serverVersion: buildInfo.version,
  generationId: pointer.generationId,
  tvShowsTotal: source.tv_shows.countDocuments({}),
  tvSyncManifestsTotal: source.tv_sync_manifests.countDocuments({}),
  tvLifecycleEventsTotal: source.tv_lifecycle_events.countDocuments({}),
  manifestCount: source.tv_sync_manifests.countDocuments({
    documentKind: "manifest"
  }),
  generationRowCount: source.tv_shows.countDocuments({
    documentKind: "generation"
  }),
  lifecycleEventCount: source.tv_lifecycle_events.countDocuments({})
}));
client.close();
' >"$WATCHLIST_SOURCE_METADATA_FILE"
chmod 0600 "$WATCHLIST_SOURCE_METADATA_FILE"
{
  printf '%s\n' 'databaseToolsVersion=100.17.0'
  printf 'restoreImage=%s\n' "$WATCHLIST_RESTORE_IMAGE"
} >"$WATCHLIST_BACKUP_DIR/toolchain.txt"
chmod 0600 "$WATCHLIST_BACKUP_DIR/toolchain.txt"
WATCHLIST_QUIESCED_POINTER="$(
  python3 -c '
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    value = json.load(source).get("generationId")
if not isinstance(value, str) or not value:
    raise SystemExit("quiesced published generationId is missing")
print(value)
' "$WATCHLIST_SOURCE_METADATA_FILE"
)"
IFS=$'\t' read -r \
  WATCHLIST_QUIESCED_TV_SHOWS_TOTAL \
  WATCHLIST_QUIESCED_TV_SYNC_MANIFESTS_TOTAL \
  WATCHLIST_QUIESCED_TV_LIFECYCLE_EVENTS_TOTAL < <(
    python3 -c '
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    metadata = json.load(source)
keys = (
    "tvShowsTotal",
    "tvSyncManifestsTotal",
    "tvLifecycleEventsTotal",
)
values = []
for key in keys:
    value = metadata.get(key)
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        raise SystemExit(f"{key} must be a nonnegative integer")
    values.append(str(value))
print("\t".join(values))
' "$WATCHLIST_SOURCE_METADATA_FILE"
  )
unset WATCHLIST_MONGO_URI
printf 'Quiesced published generation: %s\n' "$WATCHLIST_QUIESCED_POINTER"
printf 'Quiesced total documents (shows/manifests/events): %s/%s/%s\n' \
  "$WATCHLIST_QUIESCED_TV_SHOWS_TOTAL" \
  "$WATCHLIST_QUIESCED_TV_SYNC_MANIFESTS_TOTAL" \
  "$WATCHLIST_QUIESCED_TV_LIFECYCLE_EVENTS_TOTAL"
if [[ "$WATCHLIST_PRE_QUIESCENCE_POINTER" != \
      "$WATCHLIST_QUIESCED_POINTER" ]]; then
  printf 'Pointer advanced while quiescing; dump baseline is: %s\n' \
    "$WATCHLIST_QUIESCED_POINTER"
fi

mongodump --config="$WATCHLIST_MONGO_CONFIG" \
  --db=watchlist --collection=tv_shows --out="$WATCHLIST_BACKUP_DIR"
mongodump --config="$WATCHLIST_MONGO_CONFIG" \
  --db=watchlist --collection=tv_sync_manifests --out="$WATCHLIST_BACKUP_DIR"
mongodump --config="$WATCHLIST_MONGO_CONFIG" \
  --db=watchlist --collection=tv_lifecycle_events --out="$WATCHLIST_BACKUP_DIR"

for collection in tv_shows tv_sync_manifests tv_lifecycle_events; do
  bson_file="$WATCHLIST_BACKUP_DIR/watchlist/$collection.bson"
  metadata_file="$WATCHLIST_BACKUP_DIR/watchlist/$collection.metadata.json"
  [[ -f "$bson_file" ]]
  [[ -s "$metadata_file" ]]
  bsondump --quiet "$bson_file" >/dev/null
done

WATCHLIST_RESTORE_CONTAINER="watchlist-tv-retention-restore-$$"
docker run -d --rm \
  --name "$WATCHLIST_RESTORE_CONTAINER" \
  --network none \
  --tmpfs /data/db:size=512m,mode=0700 \
  --tmpfs /restore:size=512m,mode=0700 \
  "$WATCHLIST_RESTORE_IMAGE" >/dev/null

for attempt in {1..60}; do
  if docker exec "$WATCHLIST_RESTORE_CONTAINER" \
    mongosh --quiet --eval 'quit(db.runCommand({ ping: 1 }).ok ? 0 : 1)' \
    >/dev/null 2>&1; then
    break
  fi
  [[ "$attempt" -lt 60 ]]
  sleep 1
done

docker cp "$WATCHLIST_BACKUP_DIR/watchlist/." \
  "$WATCHLIST_RESTORE_CONTAINER:/restore/watchlist"
docker exec "$WATCHLIST_RESTORE_CONTAINER" \
  mongorestore --stopOnError \
  --nsFrom='watchlist.*' --nsTo='watchlist_retention_restore.*' \
  /restore

docker exec \
  --env WATCHLIST_QUIESCED_POINTER="$WATCHLIST_QUIESCED_POINTER" \
  --env WATCHLIST_QUIESCED_TV_SHOWS_TOTAL="$WATCHLIST_QUIESCED_TV_SHOWS_TOTAL" \
  --env WATCHLIST_QUIESCED_TV_SYNC_MANIFESTS_TOTAL="$WATCHLIST_QUIESCED_TV_SYNC_MANIFESTS_TOTAL" \
  --env WATCHLIST_QUIESCED_TV_LIFECYCLE_EVENTS_TOTAL="$WATCHLIST_QUIESCED_TV_LIFECYCLE_EVENTS_TOTAL" \
  "$WATCHLIST_RESTORE_CONTAINER" \
  mongosh --quiet watchlist_retention_restore --eval '
const targetBuildInfo = db.adminCommand({ buildInfo: 1 });
const targetFcvResult = db.adminCommand({
  getParameter: 1,
  featureCompatibilityVersion: 1
});
if (targetBuildInfo.ok !== 1 ||
    !Array.isArray(targetBuildInfo.versionArray) ||
    targetBuildInfo.versionArray[0] !== 8 ||
    targetFcvResult.ok !== 1 ||
    targetFcvResult.featureCompatibilityVersion?.version !== "8.0") {
  throw new Error("restore target MongoDB major and FCV must both be 8.0");
}
function requireExpectedTotal(label, environmentName) {
  const raw = process.env[environmentName];
  if (typeof raw !== "string" || !/^(0|[1-9][0-9]*)$/.test(raw)) {
    throw new Error(label + " source total is missing or invalid");
  }
  const value = Number(raw);
  if (!Number.isSafeInteger(value)) {
    throw new Error(label + " source total exceeds the exact integer range");
  }
  return value;
}
const restoredTotals = {
  tvShows: db.tv_shows.countDocuments({}),
  tvSyncManifests: db.tv_sync_manifests.countDocuments({}),
  tvLifecycleEvents: db.tv_lifecycle_events.countDocuments({})
};
const expectedTotals = {
  tvShows: requireExpectedTotal(
    "tv_shows",
    "WATCHLIST_QUIESCED_TV_SHOWS_TOTAL"
  ),
  tvSyncManifests: requireExpectedTotal(
    "tv_sync_manifests",
    "WATCHLIST_QUIESCED_TV_SYNC_MANIFESTS_TOTAL"
  ),
  tvLifecycleEvents: requireExpectedTotal(
    "tv_lifecycle_events",
    "WATCHLIST_QUIESCED_TV_LIFECYCLE_EVENTS_TOTAL"
  )
};
for (const collection of Object.keys(expectedTotals)) {
  if (restoredTotals[collection] !== expectedTotals[collection]) {
    throw new Error(collection + " restored total does not match source");
  }
}
const expectedGenerationId = process.env.WATCHLIST_QUIESCED_POINTER;
if (typeof expectedGenerationId !== "string" ||
    expectedGenerationId.length === 0) {
  throw new Error("quiesced published generation is missing");
}
const pointer = db.tv_sync_manifests.findOne({
  _id: "published-tv",
  documentKind: "pointer"
});
if (!pointer || typeof pointer.generationId !== "string" ||
    pointer.generationId.length === 0) {
  throw new Error("published pointer is missing or invalid");
}
if (pointer.generationId !== expectedGenerationId) {
  throw new Error("dump pointer does not match the quiesced baseline");
}
if (typeof pointer.manifestId !== "string" ||
    pointer.manifestId !== "generation:" + pointer.generationId) {
  throw new Error("published pointer manifest reference is invalid");
}
const manifest = db.tv_sync_manifests.findOne({
  _id: pointer.manifestId,
  documentKind: "manifest",
  generationId: pointer.generationId
});
if (!manifest) {
  throw new Error("published pointer has no referenced manifest");
}
function requireNonnegativeInteger(label, value) {
  if (!Number.isInteger(value) || value < 0) {
    throw new Error(label + " must be a nonnegative integer");
  }
}
requireNonnegativeInteger("pointer showCount", pointer.showCount);
requireNonnegativeInteger(
  "pointer lifecycleEventCount",
  pointer.lifecycleEventCount
);
requireNonnegativeInteger("manifest showCount", manifest.showCount);
requireNonnegativeInteger(
  "manifest lifecycleEventCount",
  manifest.lifecycleEventCount
);
if (pointer.showCount !== manifest.showCount ||
    pointer.lifecycleEventCount !== manifest.lifecycleEventCount) {
  throw new Error("pointer and manifest counts do not match");
}
const showCount = db.tv_shows.countDocuments({
  documentKind: "generation",
  generationId: pointer.generationId
});
if (showCount !== pointer.showCount) {
  throw new Error("generation show rows do not match the recorded count");
}
const lifecycleEventCount = db.tv_lifecycle_events.countDocuments({
  generationId: pointer.generationId
});
if (lifecycleEventCount !== pointer.lifecycleEventCount) {
  throw new Error("lifecycle-event rows do not match the recorded count");
}
printjson({
  generationId: pointer.generationId,
  restoredTotals: restoredTotals,
  showCount: showCount,
  lifecycleEventCount: lifecycleEventCount
});
'

docker rm -f "$WATCHLIST_RESTORE_CONTAINER" >/dev/null
WATCHLIST_RESTORE_CONTAINER=""

printf 'Verified dump pointer matched quiesced baseline: %s\n' \
  "$WATCHLIST_QUIESCED_POINTER"

WATCHLIST_API_RESTARTED=1
docker compose -f deploy/production/compose.yaml \
  up -d --no-build --no-deps watchlist-api
for attempt in {1..60}; do
  if curl -fsS http://127.0.0.1:5000/healthz >/dev/null; then
    break
  fi
  [[ "$attempt" -lt 60 ]]
  sleep 1
done
WATCHLIST_API_RESTARTED=0

printf 'Verified same-release API health after restart: %s\n' \
  "$WATCHLIST_RELEASE"
printf 'Restricted verified dump: %s\n' "$WATCHLIST_BACKUP_DIR"
```

The disposable MongoDB has `--network none`, publishes no host port, and has no
route to production or any unrelated container. The dump is copied into its
private `/restore` tmpfs, `mongorestore` runs inside that container, and
namespace remapping restores into `watchlist_retention_restore`. The pinned
server image and its bundled `mongorestore` are checked before quiescence;
the source server's major version is checked and recorded after quiescence, and
the running restore target's major version and FCV are checked before data
assertions. Before dumping, the guarded procedure records exact whole-collection
totals for `tv_shows`, `tv_sync_manifests`, and `tv_lifecycle_events` while all
writers are quiesced. The isolated restore must match each source total exactly,
including legacy and noncurrent documents; zero is valid only when that
collection's quiesced source total was zero. These total-count checks are
independent of the semantic assertions, which still require the restored
pointer to match the quiesced baseline, require its referenced manifest,
require pointer and manifest counts to be nonnegative integers and equal, and
require the actual current-generation show and lifecycle-event rows to match
those recorded counts. The API remains stopped until every isolated assertion
succeeds; removing the container also destroys the private database and copied
restore tmpfs.

Because the guarded procedure uses `set -Eeuo pipefail`, any failed dump,
BSON existence check, nonempty metadata-file check, `bsondump`, source
compatibility check, restore, or assertion exits immediately. A valid empty
collection may produce a zero-byte BSON file, so BSON must exist (`-f`) while
its metadata must be nonempty (`-s`). The trap clears the URI, deletes the
temporary credential config, removes the disposable container (including its
private tmpfs artifacts), and restores the prior `umask`. It deliberately does
not restart `watchlist-api` after a dump or isolated-validation failure. After
validation, the restart flag is armed before the same release is started; any
startup or health failure re-quiesces the API before cleanup returns the
original failure status. A healthy restart does not assert an unchanged pointer
because hosted activity can legitimately publish a later generation. Keep
writers quiesced after failure, preserve the restricted evidence, determine
whether a safe retry is possible, and escalate rather than resuming
automatically.

## 3. Deploy The Validated Release

Deploy only a release that passed the complete gate in
[Validation](validation.md). Do not run a production cleanup from a local
worktree or manually invoke repository deletion logic. The first production
retention execution must be the tested backend's mandatory pre-sync pass.
Immediately before deployment, record outside Git the exact previous release
SHA already captured from `last-successful.sha`, the candidate SHA, the
restricted dump path, and proof that the dump pointer matched the quiesced dump
baseline while writers were quiesced. Do not use a branch name,
moving tag, or image name without its 40-character SHA as rollback authority.

## 4. Run One Protected TV Sync

Invoke one authenticated `POST /api/sync/tv`. Verify that it succeeds, that the
published pointer advances to its returned generation ID, and then inspect TV
browse, one detail response, public sync status, and the schema-v2 export:

```bash
curl -fsS -X POST -H "X-Watchlist-Sync-Key: $SYNC_KEY" http://127.0.0.1:5000/api/sync/tv
curl -fsS "http://127.0.0.1:5000/api/watchlist?collection=tv"
curl -fsS "http://127.0.0.1:5000/api/watchlist/<id>"
curl -fsS http://127.0.0.1:5000/api/sync/status
curl -fsS http://127.0.0.1:5000/api/export/tv/sync-state
```

Keep private browse/detail/export content local; operational evidence needs only
redacted counts, timestamps, stable codes, and non-secret generation IDs.
Publish-last tests prove that readers retain the previous generation until the
single pointer advance. Do not claim that exact transition as a sequential
production observation: an operator cannot observe both sides atomically
through separate HTTP reads.

## 5. Verify Retention Invariants

After successful cleanup, confirm there are no more than 48 generation
manifests, including current. The current generation and pointer must survive.
No noncurrent valid manifest may be older than the inclusive seven-day bound
when cleanup completed successfully. Confirm malformed or uncertain identities
and legacy `tv_shows` rows were preserved rather than coerced or deleted.

## 6. Observe Capacity And Cadence

Wait for Atlas metrics to refresh, run `db.runCommand({ atlasSize: 1 })` again,
and require total data-plus-index usage below 180 MiB. If this first
post-retention reading is **greater than or equal to 180 MiB**, the rollout is
incomplete: quiesce generation writers and escalate through the rollback
procedure below.

Observe the system for 24 hours. The expected baseline is approximately four
scheduled full generations from the six-hour interval, plus only genuine
activity-triggered full generations. Recheck the pointer, counts, retention
logs, read endpoints, and Atlas usage without recording private content. A
24-hour reading **greater than or equal to 180 MiB** is also a failed rollout;
quiesce writers and use the same rollback/escalation path.

## 7. Roll Back Safely

On any rollout failure, disable every external automation that holds the sync
key and stop `watchlist-api` before collecting further evidence. Stopping the
API is the only unconditional writer suppression: `TvSyncHostedService` is
always registered and there is no disable flag. Preserve the failed release
SHA, the pre-quiescence inventory, the quiesced dump pointer, current pointer
and counts, redacted retention logs, Atlas usage, and the restricted dump
outside Git. Do not include credentials or private show content.

After preserving evidence, use a rollback hold to recreate `watchlist-api`
from the exact previous image SHA recorded before deployment. The hold uses the
real `Trakt:ActivityPollInterval` option to delay the hosted service's first
poll for 24 hours, replaces the sync API key with an unshared random value so
manual mutation routes return `401`, and disables automatic container restart.
This is a bounded incident hold, not a scheduler-disable feature:

```bash
set -Eeuo pipefail

cd /opt/watchlist-prod/repository
export WATCHLIST_CONFIG_DIR=/opt/watchlist-prod/config
export WATCHLIST_DATA_DIR=/opt/watchlist-prod/data
export WATCHLIST_RELEASE="<recorded-previous-release-sha>"
[[ "$WATCHLIST_RELEASE" =~ ^[0-9a-f]{40}$ ]]

WATCHLIST_ROLLBACK_HOLD="$WATCHLIST_CONFIG_DIR/tv-retention-rollback-hold.yml"
[[ ! -e "$WATCHLIST_ROLLBACK_HOLD" ]]
umask 077
ROLLBACK_SYNC_KEY="$(
  python3 -c 'import secrets; print(secrets.token_urlsafe(48))'
)"
{
  printf '%s\n' 'services:' '  watchlist-api:' '    restart: "no"'
  printf '%s\n' '    environment:'
  printf '      Sync__ApiKey: "%s"\n' "$ROLLBACK_SYNC_KEY"
  printf '%s\n' '      Trakt__ActivityPollInterval: "1.00:00:00"'
} >"$WATCHLIST_ROLLBACK_HOLD"
chmod 0600 "$WATCHLIST_ROLLBACK_HOLD"
unset ROLLBACK_SYNC_KEY

rollback_hold_failure() {
  local status="${1:-$?}"
  trap - EXIT INT TERM
  docker compose -f deploy/production/compose.yaml \
    -f "$WATCHLIST_ROLLBACK_HOLD" stop watchlist-api || true
  rm -f -- "$WATCHLIST_ROLLBACK_HOLD"
  exit "$status"
}
trap 'rollback_hold_failure $?' EXIT
trap 'rollback_hold_failure 130' INT
trap 'rollback_hold_failure 143' TERM

docker compose -f deploy/production/compose.yaml stop watchlist-api
docker image inspect "watchlist-api:$WATCHLIST_RELEASE" >/dev/null
docker compose -f deploy/production/compose.yaml \
  -f "$WATCHLIST_ROLLBACK_HOLD" \
  up -d --no-build --no-deps --force-recreate watchlist-api
[[ "$(
  docker inspect --format '{{.HostConfig.RestartPolicy.Name}}' \
    watchlist-prod-api
)" == "no" ]]
[[ "$(
  docker exec watchlist-prod-api \
    printenv Trakt__ActivityPollInterval
)" == "1.00:00:00" ]]
curl -fsS http://127.0.0.1:5000/healthz
curl -fsS "http://127.0.0.1:5000/api/watchlist?collection=tv"
curl -fsS http://127.0.0.1:5000/api/sync/status
curl -fsS http://127.0.0.1:5000/api/export/tv/sync-state

trap - EXIT INT TERM
printf '%s\n' \
  'Rollback hold is active; keep its protected override file in place.'
```

Do not issue a sync POST or distribute the hold key. Validate the published
pointer and all read endpoints against the preserved evidence, inspect
redacted logs, and finish the incident decision within 24 hours. A process
restart resets the 24-hour first-poll delay but does not authorize extending
the hold indefinitely. If the previous binary or its reads are unhealthy, stop
`watchlist-api`, leave every caller disabled, and escalate; do not let Compose
restart it automatically. A binary rollback cannot undo successful or
partially completed retention deletes.

Only after an operator explicitly accepts the preserved data state may normal
hosted scheduling and the original protected sync key be restored. Stop the
held container first, delete the protected override, recreate from the base
Compose file, and monitor at least the first two five-minute hosted-service
poll opportunities. If a pointer change lacks a corresponding successful
redacted sync record, or any retention/read invariant fails, immediately stop
`watchlist-api` and return to incident escalation. Keep all external sync
callers disabled until the monitoring loop succeeds:

```bash
set -Eeuo pipefail

cd /opt/watchlist-prod/repository
export WATCHLIST_CONFIG_DIR=/opt/watchlist-prod/config
export WATCHLIST_DATA_DIR=/opt/watchlist-prod/data
export WATCHLIST_RELEASE="<recorded-previous-release-sha>"
[[ "$WATCHLIST_RELEASE" =~ ^[0-9a-f]{40}$ ]]
WATCHLIST_ROLLBACK_HOLD="$WATCHLIST_CONFIG_DIR/tv-retention-rollback-hold.yml"
[[ -f "$WATCHLIST_ROLLBACK_HOLD" ]]

normal_schedule_failure() {
  local status="${1:-$?}"
  trap - EXIT INT TERM
  docker compose -f deploy/production/compose.yaml \
    stop watchlist-api || true
  exit "$status"
}
trap 'normal_schedule_failure $?' EXIT
trap 'normal_schedule_failure 130' INT
trap 'normal_schedule_failure 143' TERM

docker compose -f deploy/production/compose.yaml \
  -f "$WATCHLIST_ROLLBACK_HOLD" stop watchlist-api
rm -f -- "$WATCHLIST_ROLLBACK_HOLD"
docker compose -f deploy/production/compose.yaml \
  up -d --no-build --no-deps --force-recreate watchlist-api
curl -fsS http://127.0.0.1:5000/healthz
for opportunity in 1 2; do
  sleep 310
  curl -fsS http://127.0.0.1:5000/healthz >/dev/null
  docker logs --since 6m watchlist-prod-api
  curl -fsS http://127.0.0.1:5000/api/export/tv/sync-state >/dev/null
done

trap - EXIT INT TERM
printf '%s\n' \
  'Normal hosted schedule survived two observed poll opportunities.'
```

Validate the dump in the disposable isolated database first, as required
above. Restoring any dump to production requires explicit supervised
authorization and a separate approved incident command. Never improvise a
`mongorestore`, use `--drop`, or overwrite production collections during this
rollout procedure.

At every stage, `deleteMany` against production, TTL conversion, index drop,
and `compact` are prohibited. Do not use an Atlas UI bulk delete or an
equivalent script. Production retention is authorized only through the tested
deployed backend after the restricted dump has been created and verified.

# Evidence

For each actual stage, record only commands, redacted response metadata,
generation ID, timestamps, counts, stable reason codes, report status, and
destination outcome totals in the [TV Integration Rollout](../reports/tv_integration_rollout.md).
Do not claim deployment, adoption, apply, or convergence from local or CI
output. A successful destination rollout does not authorize any history,
cleanup, library, or file operation.

# Links

- [Export Endpoints](../apis/export_endpoints.md)
- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
- [Homelab CD](homelab_cd.md)
