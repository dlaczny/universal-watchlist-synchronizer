# Watchlist Continuous Delivery Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically deploy the personal Watchlist backend and update the Android TV app on the owner's TV without Play Store publishing or manual APK copy/install.

**Architecture:** Use GitHub Actions for public, non-secret CI validation. Use one Proxmox VM as the trusted local CD host for backend Docker Compose deployment, backend health checks, Android debug APK builds, and ADB install to the TV. Use MongoDB Atlas Free Tier for persistence because the Proxmox VM CPU does not expose AVX required by modern MongoDB containers. Do not put backend secrets, Plex tokens, TMDB tokens, Letterboxd credentials, MongoDB credentials, keystores, or signing passwords in the repository or in a public APK.

**Tech Stack:** GitHub Actions, .NET 10, Docker Compose, MongoDB Atlas, Android Gradle Plugin, Java 17, ADB over LAN, Proxmox VM/systemd, optional Portainer later.

---

## Current Repository Facts

- Android project root: `android/`
- Backend solution: `backend/Watchlist.sln`
- Backend API project: `backend/src/Watchlist.Api/Watchlist.Api.csproj`
- Backend target framework: `net10.0`
- Local development MongoDB compose file: `compose.yaml`
- Deployment MongoDB target: MongoDB Atlas Free Tier
- Android package name: `com.watchlist.tv`
- Main launch package for ADB: `com.watchlist.tv`
- Gradle build file: `android/app/build.gradle`
- Config source today: `BuildConfig.WATCHLIST_API_BASE_URL`
- Current API default: `http://10.0.2.2:5000`
- `10.0.2.2` is emulator-only and will not work from a real Android TV.
- Observed Android TV wireless debugging screen: `192.168.50.100:34763`
- Treat the observed TV port as temporary until verified from the Proxmox VM. Android wireless debugging can expose separate pairing and connection ports.
- GitHub Actions workflows exist for backend and Android CI.

## Current Working Homelab Setup

- Deployer VM: Ubuntu Server on Proxmox.
- Deployer VM LAN IP observed during setup: `192.168.50.163`.
- Backend URL for Android TV: `http://192.168.50.163:5000`.
- Backend deploy compose path: `deploy/backend/compose.yaml`.
- Do not run deployment from the repository root with `docker compose up`; the root `compose.yaml` is for local development.
- Runtime database: MongoDB Atlas Free Tier.
- Local MongoDB container was abandoned because the VM CPU did not expose AVX required by MongoDB 5.0+ containers.
- Android TV wireless debugging address observed during setup: `192.168.50.100:34763`.
- Android TV pairing used modern Google platform-tools because Ubuntu's `adb` package was too old for `adb pair`.
- Android SDK path on VM: `/opt/android-sdk`.
- Android SDK required packages: `platform-tools`, `platforms;android-36`, `build-tools;35.0.0`, and `build-tools;36.0.0`.
- Android SDK licenses must be accepted after making `/opt/android-sdk` writable by the `watchlist` user.

## Recommended Design

Use this flow first:

```text
Push to main
  -> GitHub Actions validates Android build with safe default config
  -> GitHub Actions validates backend build/tests with safe default config
  -> Proxmox VM pulls trusted main
  -> Proxmox VM deploys backend with Docker Compose and VM-local secrets
  -> Proxmox VM health-checks backend over LAN
  -> Proxmox VM injects personal non-secret Android config
  -> Proxmox VM builds Android debug APK locally
  -> Proxmox VM installs APK to Android TV with adb
```

This keeps all sensitive backend configuration on the Proxmox VM. It is also safer than a public GitHub Release containing a personal APK because the current app bakes `WATCHLIST_API_BASE_URL` into the APK at build time. A LAN backend URL is not a strong secret, but publishing a public APK with personal configuration is unnecessary.

Later, if the app supports first-run editable backend URL or private release assets, we can switch to:

```text
GitHub Actions builds APK
  -> GitHub Release artifact
  -> local deployer downloads APK
  -> local deployer installs APK
```

Do not use a self-hosted GitHub runner for deployment unless you explicitly accept the public-repo risk.

## Secret Rules

Never commit these:

```text
android/local.properties
android/keystore.properties
*.keystore
*.jks
*.apk
*.aab
.env
.env.*
appsettings.*.Local.json
secrets.json
```

Never put these inside the APK:

```text
Plex token
TMDB token
Letterboxd credentials
MongoDB connection string
Backend admin token
Long-lived cloud token
Signing key or signing password
```

Acceptable APK config for this personal app:

```text
Backend base URL, for example http://192.168.50.10:5000
Environment label, for example Home
Non-sensitive UI flags
```

The Android app must call only the backend API. The backend remains the only place for third-party credentials.

## Grill Questions

Answer these before implementation. Recommended answers are included.

1. Is the backend reachable from the TV at a stable LAN address?
   - Recommended: Yes, reserve a DHCP lease for the backend host and use an address like `http://192.168.50.10:5000`.
   - Current answer: Check later.
   - If no: fix backend networking before Android TV CD.

2. Is the Android TV reachable over ADB from the homelab deployer?
   - Recommended: Yes, reserve a DHCP lease for the TV and enable network debugging.
   - Current observation: TV wireless debugging is enabled and currently shows `192.168.50.100:34763`.
   - Current answer: Verify later from the Proxmox VM with `adb pair` and `adb connect`.
   - If no: CD cannot install automatically.

3. Do you accept that anything baked into the APK can be extracted?
   - Recommended: Yes, but only bake non-sensitive config.
   - If no: the app needs runtime configuration from a protected backend or local device storage.

4. Should GitHub build the deployable personal APK?
   - Recommended: No for now. GitHub should validate the build; the homelab should build the personal APK locally.
   - Reason: the current app uses build-time config.
   - Current answer: Agree.

5. Should the homelab execute code from the public repo?
   - Recommended: Yes, but only from the protected `main` branch after you personally merge changes.
   - Hard rule: never auto-deploy pull request code.

6. Should release signing be configured immediately?
   - Recommended: Not for the first proof of concept. Start with debug install to prove the loop, then add a stable local release keystore.
   - Reason: signing mistakes can block updates and force manual uninstall.
   - Current answer: Debug is OK.

7. Should the deployer run in Portainer?
   - Recommended: Use a Proxmox VM with systemd first. Add Portainer after the loop works.
   - Current answer: Agree.
   - If network ADB is flaky from a container later: keep deployment on the VM.

8. Should deploy happen on every push to any branch?
   - Recommended: No. Deploy only `main`.

9. Should deploy happen from GitHub webhooks?
   - Recommended: Later. Start with a local polling loop or cron/systemd timer.
   - Reason: simpler and avoids exposing a webhook endpoint into the homelab.

10. Is the TV allowed to install from ADB without confirmation after initial authorization?
    - Recommended: Confirm once manually on the TV when pairing ADB. After that, `adb install -r` should be unattended.

11. Should backend and Android CD run from the same Proxmox VM?
    - Recommended: Yes.
    - Current answer: Agree.
    - Reason: one trusted local CD host can deploy backend, health-check it, build Android with the correct backend URL, and install to the TV.

12. Should backend deploy before Android deploy?
    - Recommended: Yes.
    - Reason: the Android app should be built and installed only after the backend container is running and reachable.

13. Should backend secrets live in GitHub Actions secrets?
    - Recommended: No for the first local-only setup.
    - Reason: GitHub does not need secrets to validate code. Runtime secrets belong only on the Proxmox VM.

## Ownership

Codex can do:

- Add GitHub Actions CI workflow for safe Android build validation.
- Add GitHub Actions CI workflow for backend build/test validation.
- Add a backend Dockerfile.
- Add a production-style Docker Compose file that reads secrets from a VM-local `.env`.
- Add backend deployment and health-check scripts.
- Modify Gradle so `WATCHLIST_API_BASE_URL` can come from a Gradle property or environment variable.
- Add local deploy scripts.
- Add a Portainer-compatible container definition later if desired.
- Add documentation and command checklists.
- Add `.gitignore` entries for signing/config artifacts.
- Run local backend tests, Android unit tests, and Gradle checks available on this machine.

You must do manually:

- Create or choose the Proxmox VM that will be the trusted CD host.
- Install Docker, Docker Compose, Git, Java 17, and Android platform tools on that VM.
- Enable Android TV Developer Options.
- Enable ADB or Network Debugging on the TV.
- Accept the ADB debugging authorization prompt on the TV.
- Reserve static DHCP leases for the TV and backend host.
- Decide the real backend LAN URL.
- Store the backend `.env` only on the Proxmox VM.
- Create and protect any release keystore if/when release signing is enabled.
- Enter any GitHub repository settings, branch protection, or secrets that require account access.

## Implementation Plan

### Task 1: Confirm Manual Network Prerequisites

**Files:**
- No repository file changes.

- [ ] **Step 0: Install deployer VM prerequisites**

Run on the Proxmox VM:

```bash
sudo apt update
sudo apt install -y ca-certificates curl gnupg git unzip openjdk-17-jdk
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
  | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
  | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker
sudo usermod -aG docker "$USER"
```

Install modern Android platform-tools because Ubuntu 22.04's `adb` package does not support `adb pair`:

```bash
cd /tmp
curl -LO https://dl.google.com/android/repository/platform-tools-latest-linux.zip
unzip -o platform-tools-latest-linux.zip
sudo mkdir -p /opt/android-sdk
sudo rm -rf /opt/android-sdk/platform-tools
sudo mv platform-tools /opt/android-sdk/platform-tools
echo 'export PATH=/opt/android-sdk/platform-tools:$PATH' >> ~/.bashrc
source ~/.bashrc
```

Install Android command-line tools and SDK packages after cloning the repository to `/opt/watchlist-app`:

```bash
sudo mkdir -p /opt/android-sdk/cmdline-tools
cd /tmp
curl -LO https://dl.google.com/android/repository/commandlinetools-linux-13114758_latest.zip
unzip -o commandlinetools-linux-13114758_latest.zip
sudo rm -rf /opt/android-sdk/cmdline-tools/latest
sudo mkdir -p /opt/android-sdk/cmdline-tools/latest
sudo mv cmdline-tools/* /opt/android-sdk/cmdline-tools/latest/
cat <<'EOF' >> ~/.bashrc
export ANDROID_HOME=/opt/android-sdk
export ANDROID_SDK_ROOT=/opt/android-sdk
export PATH=/opt/android-sdk/cmdline-tools/latest/bin:/opt/android-sdk/platform-tools:$PATH
EOF
source ~/.bashrc
sudo chown -R "$USER:$USER" /opt/android-sdk
yes | sdkmanager --licenses
sdkmanager "platform-tools" "platforms;android-36" "build-tools;35.0.0" "build-tools;36.0.0"
cat > /opt/watchlist-app/android/local.properties <<'EOF'
sdk.dir=/opt/android-sdk
EOF
```

- [ ] **Step 1: Reserve backend LAN address**

In the router or DHCP server, reserve a stable IP for the backend host.

Example:

```text
Backend host: 192.168.50.10
Backend URL:  http://192.168.50.10:5000
```

- [ ] **Step 2: Reserve Android TV LAN address**

In the router or DHCP server, reserve a stable IP for the TV.

Example:

```text
Android TV: 192.168.50.100
```

- [ ] **Step 3: Enable Android TV wireless debugging**

On Android TV:

```text
Settings
  -> Device Preferences
  -> About
  -> Build
  -> press Build several times
```

Then enable:

```text
Developer Options
  -> Wireless debugging
  -> Enabled
```

- [ ] **Step 4: Pair ADB from the future deployer host**

Run on the homelab host, VM, or container host:

```bash
adb pair 192.168.50.100:PAIRING_PORT
```

Use the pairing port and pairing code shown after selecting:

```text
Pair device with pairing code
```

Expected:

```text
Successfully paired to 192.168.50.100:PAIRING_PORT
```

- [ ] **Step 5: Connect ADB from the future deployer host**

Run on the homelab host, VM, or container host:

```bash
adb connect 192.168.50.100:CONNECT_PORT
adb devices
```

Use the IP and port shown on the main wireless debugging screen. In the current photo, that screen shows:

```text
192.168.50.100:34763
```

Expected:

```text
192.168.50.100:CONNECT_PORT device
```

If the TV shows an authorization prompt, accept it.

### Task 2: Add Backend Health Endpoint

**Files:**
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Test: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Add a lightweight health endpoint**

Add this endpoint near the other `app.MapGet` calls in `backend/src/Watchlist.Api/Program.cs`:

```csharp
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
```

This endpoint must not require MongoDB, Plex, TMDB, Letterboxd, or seeded data. It exists so the deployer can confirm the backend process is listening before building and installing the Android app.

- [ ] **Step 2: Add API test**

Add a test in `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`:

```csharp
[Fact]
public async Task Healthz_ReturnsOk()
{
    await using SeededApiFactory factory = new();
    using HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.GetAsync("/healthz");

    response.EnsureSuccessStatusCode();
}
```

- [ ] **Step 3: Run backend API tests**

Run:

```powershell
Set-Location C:\Users\laczn\Documents\watchlist-app
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj
```

Expected:

```text
Passed!
```

- [ ] **Step 4: Commit**

Run:

```powershell
git add backend/src/Watchlist.Api/Program.cs backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs
git commit -m "api: add deployment health endpoint"
```

### Task 3: Add Backend CI

**Files:**
- Create: `.github/workflows/backend-ci.yml`

- [ ] **Step 1: Create backend CI workflow**

Create `.github/workflows/backend-ci.yml`:

```yaml
name: Backend CI

on:
  pull_request:
    paths:
      - "backend/**"
      - ".github/workflows/backend-ci.yml"
  push:
    branches:
      - main
    paths:
      - "backend/**"
      - ".github/workflows/backend-ci.yml"
  workflow_dispatch:

permissions:
  contents: read

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Restore
        run: dotnet restore backend/Watchlist.sln

      - name: Build
        run: dotnet build backend/Watchlist.sln --configuration Release --no-restore

      - name: Test
        run: dotnet test backend/Watchlist.sln --configuration Release --no-build
```

- [ ] **Step 2: Verify locally**

Run:

```powershell
Set-Location C:\Users\laczn\Documents\watchlist-app
dotnet test backend\Watchlist.sln
```

Expected:

```text
Passed!
```

- [ ] **Step 3: Commit**

Run:

```powershell
git add .github/workflows/backend-ci.yml
git commit -m "ci: add backend validation workflow"
```

### Task 4: Add Safe Android CI

**Files:**
- Create: `.github/workflows/android-ci.yml`

- [ ] **Step 1: Create CI workflow**

Create `.github/workflows/android-ci.yml`:

```yaml
name: Android CI

on:
  pull_request:
    paths:
      - "android/**"
      - ".github/workflows/android-ci.yml"
  push:
    branches:
      - main
    paths:
      - "android/**"
      - ".github/workflows/android-ci.yml"
  workflow_dispatch:

permissions:
  contents: read

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Set up JDK
        uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: 17

      - name: Build debug APK
        working-directory: android
        run: ./gradlew assembleDebug testDebugUnitTest
```

- [ ] **Step 2: Verify locally**

Run:

```powershell
Set-Location C:\Users\laczn\Documents\watchlist-app\android
.\gradlew.bat assembleDebug testDebugUnitTest
```

Expected:

```text
BUILD SUCCESSFUL
```

- [ ] **Step 3: Commit**

Run:

```powershell
git add .github/workflows/android-ci.yml
git commit -m "ci: add android validation workflow"
```

### Task 5: Make Android API URL Injectable

**Files:**
- Modify: `android/app/build.gradle`
- Test: `android/app/src/test/java/com/watchlist/tv/WatchlistConfigTest.java`

- [ ] **Step 1: Inspect existing config tests**

Run:

```powershell
Get-Content C:\Users\laczn\Documents\watchlist-app\android\app\src\test\java\com\watchlist\tv\WatchlistConfigTest.java
```

- [ ] **Step 2: Modify Gradle config source**

In `android/app/build.gradle`, replace the hardcoded `WATCHLIST_API_BASE_URL` build config field with a value read from Gradle property or environment variable:

```groovy
def watchlistApiBaseUrl = providers
        .gradleProperty("watchlistApiBaseUrl")
        .orElse(providers.environmentVariable("WATCHLIST_API_BASE_URL"))
        .orElse("http://10.0.2.2:5000")

android {
    namespace "com.watchlist.tv"
    compileSdk 36

    buildFeatures {
        buildConfig true
    }

    defaultConfig {
        applicationId "com.watchlist.tv"
        minSdk 23
        targetSdk 36
        versionCode 1
        versionName "0.1.0"
        testInstrumentationRunner "androidx.test.runner.AndroidJUnitRunner"
        buildConfigField "String", "WATCHLIST_API_BASE_URL", "\"${watchlistApiBaseUrl.get()}\""
        buildConfigField "int", "WATCHLIST_GRID_COLUMNS", "7"
    }

    compileOptions {
        sourceCompatibility JavaVersion.VERSION_17
        targetCompatibility JavaVersion.VERSION_17
    }
}
```

- [ ] **Step 3: Verify default build still works**

Run:

```powershell
Set-Location C:\Users\laczn\Documents\watchlist-app\android
.\gradlew.bat assembleDebug testDebugUnitTest
```

Expected:

```text
BUILD SUCCESSFUL
```

- [ ] **Step 4: Verify injected URL build works**

Run:

```powershell
Set-Location C:\Users\laczn\Documents\watchlist-app\android
.\gradlew.bat assembleDebug -PwatchlistApiBaseUrl=http://192.168.50.10:5000
```

Expected:

```text
BUILD SUCCESSFUL
```

- [ ] **Step 5: Commit**

Run:

```powershell
git add android/app/build.gradle android/app/src/test/java/com/watchlist/tv/WatchlistConfigTest.java
git commit -m "build: allow android api url injection"
```

### Task 6: Add Backend Container Deployment

**Files:**
- Modify: `.gitignore`
- Create: `backend/src/Watchlist.Api/Dockerfile`
- Create: `deploy/backend/compose.yaml`
- Create: `deploy/backend/watchlist-backend.env.example`

- [ ] **Step 1: Add deployment secret ignore rules**

Add these lines to `.gitignore`:

```gitignore
android/keystore.properties
*.keystore
*.jks
deploy/backend/watchlist-backend.env
deploy/local-cd/watchlist-deploy.env
```

- [ ] **Step 2: Create backend Dockerfile**

Create `backend/src/Watchlist.Api/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Watchlist.sln backend/Watchlist.sln
COPY backend/src/Watchlist.Domain/Watchlist.Domain.csproj backend/src/Watchlist.Domain/
COPY backend/src/Watchlist.Application/Watchlist.Application.csproj backend/src/Watchlist.Application/
COPY backend/src/Watchlist.Infrastructure/Watchlist.Infrastructure.csproj backend/src/Watchlist.Infrastructure/
COPY backend/src/Watchlist.Api/Watchlist.Api.csproj backend/src/Watchlist.Api/
RUN dotnet restore backend/src/Watchlist.Api/Watchlist.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/src/Watchlist.Api/Watchlist.Api.csproj --configuration Release --output /app/publish --no-restore

FROM runtime AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Watchlist.Api.dll"]
```

- [ ] **Step 3: Create backend environment example**

Create `deploy/backend/watchlist-backend.env.example`:

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
MongoDb__ConnectionString=CHANGE_ME_ATLAS_CONNECTION_STRING
MongoDb__DatabaseName=watchlist
MongoDb__WatchlistItemsCollectionName=watchlist_items
MongoDb__SyncRunsCollectionName=sync_runs
MongoDb__PlexLibraryItemsCollectionName=plex_library_items
Letterboxd__WatchlistUrl=https://letterboxd-list-radarr.onrender.com/example-user/watchlist
Tmdb__AccessToken=replace-on-proxmox-vm
Tmdb__BaseUrl=https://api.themoviedb.org/3
Tmdb__ImageBaseUrl=https://image.tmdb.org/t/p
Plex__BaseUrl=http://192.168.50.20:32400
Plex__Token=replace-on-proxmox-vm
```

The committed example must contain fake token values only. The real file must be named `deploy/backend/watchlist-backend.env` on the Proxmox VM and must not be committed.

- [ ] **Step 4: Create backend compose file**

Create `deploy/backend/compose.yaml`:

```yaml
services:
  watchlist-api:
    build:
      context: ../..
      dockerfile: backend/src/Watchlist.Api/Dockerfile
    container_name: watchlist-api
    restart: unless-stopped
    env_file:
      - ./watchlist-backend.env
    ports:
      - "5000:8080"
```

- [ ] **Step 5: Install real env file manually on Proxmox VM**

Run on the VM:

```bash
cd /opt/watchlist-app
cp deploy/backend/watchlist-backend.env.example deploy/backend/watchlist-backend.env
nano deploy/backend/watchlist-backend.env
chmod 600 deploy/backend/watchlist-backend.env
```

Replace only values in the VM-local file. Do not commit `deploy/backend/watchlist-backend.env`.

- [ ] **Step 6: Build and start backend manually**

Run on the VM:

```bash
cd /opt/watchlist-app/deploy/backend
docker compose up -d --build
curl -fsS http://localhost:5000/healthz
```

Expected:

```json
{"status":"ok"}
```

- [ ] **Step 7: Commit**

Run:

```powershell
git add .gitignore backend/src/Watchlist.Api/Dockerfile deploy/backend/compose.yaml deploy/backend/watchlist-backend.env.example
git commit -m "deploy: add backend compose deployment"
```

### Task 7: Add Combined Local Deployer Script

**Files:**
- Create: `scripts/deploy-watchlist-local.sh`

- [ ] **Step 1: Create deploy script**

Create `scripts/deploy-watchlist-local.sh`:

```bash
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
```

- [ ] **Step 2: Make executable on Linux deployer**

Run on the deployer:

```bash
chmod +x /opt/watchlist-app/scripts/deploy-watchlist-local.sh
```

- [ ] **Step 3: Run one manual deployment from the deployer**

Run on the deployer:

```bash
BACKEND_HEALTH_URL=http://127.0.0.1:5000/healthz \
TV_ADB_SERIAL=192.168.50.100:34763 \
WATCHLIST_API_BASE_URL=http://192.168.50.10:5000 \
/opt/watchlist-app/scripts/deploy-watchlist-local.sh
```

Expected:

```text
Success
```

The app should launch on the TV.

- [ ] **Step 4: Commit**

Run:

```powershell
git add scripts/deploy-watchlist-local.sh
git commit -m "deploy: add local watchlist deploy script"
```

### Task 8: Add Homelab Scheduler

**Files:**
- Create: `deploy/local-cd/systemd/watchlist-deploy.service`
- Create: `deploy/local-cd/systemd/watchlist-deploy.timer`
- Create: `deploy/local-cd/watchlist-deploy.env.example`

- [ ] **Step 1: Create env example**

Create `deploy/local-cd/watchlist-deploy.env.example`:

```bash
APP_ROOT=/opt/watchlist-app
ANDROID_DIR=/opt/watchlist-app/android
BACKEND_COMPOSE_DIR=/opt/watchlist-app/deploy/backend
BACKEND_HEALTH_URL=http://127.0.0.1:5000/healthz
TV_ADB_SERIAL=192.168.50.100:34763
WATCHLIST_API_BASE_URL=http://192.168.50.10:5000
PACKAGE_NAME=com.watchlist.tv
BUILD_TYPE=debug
SKIP_ANDROID_DEPLOY=false
```

- [ ] **Step 2: Create systemd service**

Create `deploy/local-cd/systemd/watchlist-deploy.service`:

```ini
[Unit]
Description=Deploy Watchlist backend and Android TV app
Wants=network-online.target
After=network-online.target

[Service]
Type=oneshot
EnvironmentFile=/opt/watchlist-app/deploy/local-cd/watchlist-deploy.env
ExecStart=/opt/watchlist-app/scripts/deploy-watchlist-local.sh
WorkingDirectory=/opt/watchlist-app
```

- [ ] **Step 3: Create systemd timer**

Create `deploy/local-cd/systemd/watchlist-deploy.timer`:

```ini
[Unit]
Description=Poll and deploy Watchlist backend and Android TV app

[Timer]
OnBootSec=2min
OnUnitActiveSec=5min
Unit=watchlist-deploy.service

[Install]
WantedBy=timers.target
```

- [ ] **Step 4: Install manually on deployer**

Run on the deployer:

```bash
cp /opt/watchlist-app/deploy/local-cd/watchlist-deploy.env.example /opt/watchlist-app/deploy/local-cd/watchlist-deploy.env
nano /opt/watchlist-app/deploy/local-cd/watchlist-deploy.env
sudo cp /opt/watchlist-app/deploy/local-cd/systemd/watchlist-deploy.service /etc/systemd/system/
sudo cp /opt/watchlist-app/deploy/local-cd/systemd/watchlist-deploy.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now watchlist-deploy.timer
```

- [ ] **Step 5: Check timer**

Run:

```bash
systemctl list-timers watchlist-deploy.timer
journalctl -u watchlist-deploy.service -n 100 --no-pager
```

Expected:

```text
Deployer runs without authentication, Gradle, or ADB errors.
```

- [ ] **Step 6: Commit**

Run:

```powershell
git add deploy/local-cd scripts/deploy-watchlist-local.sh
git commit -m "deploy: add homelab watchlist scheduler"
```

### Task 9: Add Portainer Option Later

**Files:**
- Create: `deploy/portainer/README.md`

- [ ] **Step 1: Document why Portainer is deferred**

Create `deploy/portainer/README.md`:

````markdown
# Portainer Migration Notes

The first working continuous delivery path uses a Proxmox VM with systemd.

Portainer is deferred because the local deployer needs to pull trusted `main`, run `docker compose up -d --build` for the backend, run Gradle for Android, keep persistent ADB authorization, and install the APK over LAN.

Running that exact deployer inside a container would require Docker socket access or Docker-in-Docker. That adds avoidable complexity and a larger local privilege boundary.

After the VM/systemd path works, migrate only the backend runtime to Portainer first:

```text
Portainer manages:
  watchlist-api
  no MongoDB container; persistence is MongoDB Atlas

systemd still manages:
  git pull
  backend health check
  Android debug APK build
  ADB install
````
```

- [ ] **Step 2: Commit**

Run:

```powershell
git add deploy/portainer/README.md
git commit -m "docs: document portainer deployment boundary"
```

### Task 10: Add Release Signing Later

**Files:**
- Modify: `android/app/build.gradle`
- Create: `android/keystore.properties.example`

- [ ] **Step 1: Create local keystore manually**

Run on the deployer, not in the repository:

```bash
mkdir -p /opt/watchlist-signing
keytool -genkeypair \
  -v \
  -keystore /opt/watchlist-signing/watchlist-release.jks \
  -alias watchlist \
  -keyalg RSA \
  -keysize 2048 \
  -validity 10000
```

- [ ] **Step 2: Create local uncommitted keystore properties**

Create on the deployer:

```text
/opt/watchlist-app/android/keystore.properties
```

Content:

```properties
storeFile=/opt/watchlist-signing/watchlist-release.jks
storePassword=REPLACE_WITH_LOCAL_PASSWORD
keyAlias=watchlist
keyPassword=REPLACE_WITH_LOCAL_PASSWORD
```

- [ ] **Step 3: Create committed example file**

Create `android/keystore.properties.example`:

```properties
storeFile=/opt/watchlist-signing/watchlist-release.jks
storePassword=change-me
keyAlias=watchlist
keyPassword=change-me
```

- [ ] **Step 4: Modify Gradle release signing**

Add release signing to `android/app/build.gradle` only after debug CD works. The signing config must load `keystore.properties` only when the file exists.

- [ ] **Step 5: Verify release install**

Run on the deployer:

```bash
BACKEND_HEALTH_URL=http://127.0.0.1:5000/healthz \
TV_ADB_SERIAL=192.168.50.100:34763 \
WATCHLIST_API_BASE_URL=http://192.168.50.10:5000 \
BUILD_TYPE=release \
/opt/watchlist-app/scripts/deploy-watchlist-local.sh
```

Expected:

```text
Success
```

- [ ] **Step 6: Commit**

Run:

```powershell
git add android/app/build.gradle android/keystore.properties.example
git commit -m "build: add local release signing support"
```

## Verification Checklist

- [ ] Pull requests run backend and Android CI only.
- [ ] Pull requests never deploy backend or TV.
- [ ] `main` can be pulled locally by the Proxmox VM deployer.
- [ ] Backend deploys with Docker Compose from the Proxmox VM.
- [ ] Backend responds to `/healthz` before Android build/install starts.
- [ ] The TV receives APK updates with `adb install -r`.
- [ ] The app launches after install.
- [ ] The app talks to the LAN backend URL, not `10.0.2.2`.
- [ ] No tokens or passwords are present in the APK config.
- [ ] Backend secrets are present only in `deploy/backend/watchlist-backend.env` on the Proxmox VM.
- [ ] No keystore or local properties are committed.
- [ ] The local deployer has the only copy of local config.

## Stop Conditions

Stop and redesign if any of these become true:

- The Android app needs direct Plex, TMDB, Letterboxd, or MongoDB credentials.
- The deployer must deploy unreviewed pull request code.
- The public APK would include anything more sensitive than a LAN backend URL.
- The TV cannot keep a stable ADB authorization.
- The backend cannot be reached from the TV over the LAN.
