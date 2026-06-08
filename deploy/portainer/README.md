# Portainer Migration Notes

The first working continuous delivery path uses a Proxmox VM with systemd.

Portainer is deferred because the local deployer needs to pull trusted `main`, run `docker compose up -d --build` for the backend, run Gradle for Android, keep persistent ADB authorization, and install the APK over LAN.

Running that exact deployer inside a container would require Docker socket access or Docker-in-Docker. That adds avoidable complexity and a larger local privilege boundary.

After the VM/systemd path works, migrate only the backend runtime to Portainer first:

```text
Portainer manages:
  watchlist-api
  watchlist-mongo

systemd still manages:
  git pull
  backend health check
  Android debug APK build
  ADB install
```
