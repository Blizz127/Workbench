# Mobile dashboard deployment

How the read-only Workbench web dashboard (`src/Workbench.Web`) runs as a
persistent, private service on the OVH VPS, reachable only through Tailscale.

## Layout

| What | Where |
| --- | --- |
| Published binaries | `/home/blizz/apps/workbench-web/` |
| Local configuration (token) | `/home/blizz/.config/workbench-web/environment` — mode `0600`, never committed |
| systemd user unit | `/home/blizz/.config/systemd/user/workbench-web.service` |
| Phone URL helper | `/home/blizz/bin/show-workbench-mobile-url` |

## Publish

```bash
cd /home/blizz/dev/workbench
dotnet publish src/Workbench.Web/Workbench.Web.csproj -c Release -o /home/blizz/apps/workbench-web
```

## Configuration

`/home/blizz/.config/workbench-web/environment` (0600; generate the token with
`openssl rand -hex 32` if the file does not exist yet):

```ini
ASPNETCORE_URLS=http://100.104.53.1:8471
WORKBENCH_WEB_BindAddress=100.104.53.1
WORKBENCH_WEB_Port=8471
WORKBENCH_WEB_AuthToken=<64-char hex token>
```

`WORKBENCH_WEB_BindAddress`/`Port` drive the application's own binding;
`ASPNETCORE_URLS` documents the same address for standard tooling. The token
is required — with an empty token the host answers 503 on every route.

## systemd user service

`~/.config/systemd/user/workbench-web.service`:

```ini
[Unit]
Description=Workbench read-only mobile development dashboard (Tailscale only)
After=network.target

[Service]
Type=simple
WorkingDirectory=/home/blizz/dev/workbench
EnvironmentFile=/home/blizz/.config/workbench-web/environment
Restart=on-failure
RestartSec=5
# The user manager was started before 'blizz' joined the docker group, so its
# process tree lacks it; user units cannot set SupplementaryGroups. Launching
# through `sg docker` adds the group (blizz is a member in /etc/group). This
# becomes a no-op once the user manager restarts with the group included.
# NOTE: NoNewPrivileges is intentionally NOT set — it would block the setgid()
# `sg` needs.
ExecStart=/usr/bin/sg docker -c "/home/blizz/apps/workbench-web/Workbench.Web"

[Install]
WantedBy=default.target
```

Manage it without root:

```bash
systemctl --user daemon-reload
systemctl --user enable --now workbench-web
systemctl --user status workbench-web
```

Lingering keeps the service (and the user manager) alive across logouts and
starts it after reboot: `loginctl enable-linger` (blizz can run this for
itself; `loginctl show-user blizz --property=Linger` should say `Linger=yes`).

### The docker-group caveat

The dashboard inspects Docker through the same read-only
`IDockerComposeService` as the desktop app, which needs access to
`/var/run/docker.sock` (group `docker`, gid 986). The systemd *user manager*
captured its group list when it first started — before `blizz` was added to
the docker group — and with lingering enabled it does not pick the group up
again until it restarts (e.g. at the next reboot). Two consequences:

- User units **cannot** use `SupplementaryGroups=` (status 216/GROUP) — the
  user manager has no privilege to change groups.
- The unit therefore starts the app via `sg docker -c "..."`, which sets the
  process's effective gid to `docker`. `NoNewPrivileges=true` would block
  that setgid, so it is deliberately not set.

After any reboot the user manager starts fresh with the docker group
included and the `sg` wrapper is harmless.

## Network posture

- Listens only on the Tailscale address `100.104.53.1:8471` — never
  `0.0.0.0`, never the public OVH interface. `ss -tlnp | grep 8471` confirms.
- No firewall port is opened; Tailscale ACLs govern access.
- The application additionally warns at startup if configured with a
  non-private bind address.

## Validation checklist

```bash
ss -tlnp | grep 8471                       # LISTEN on 100.104.53.1:8471 only
curl -s -o /dev/null -w '%{http_code}\n' http://100.104.53.1:8471/          # 401
curl -s -o /dev/null -w '%{http_code}\n' "http://100.104.53.1:8471/?token=wrong"   # 401
curl -s -o /dev/null -w '%{http_code}\n' -X POST "http://100.104.53.1:8471/?token=$TOKEN"  # 405
curl -s -o /dev/null -w '%{http_code}\n' "http://100.104.53.1:8471/?token=$TOKEN"   # 200
```

`GET /api/snapshot` (header `X-Workbench-Token`) returns the same data as
JSON. Everything is read-only: Banshee containers, repositories, and tmux
sessions are inspected, never modified.

## Phone access

On the VPS:

```bash
~/bin/show-workbench-mobile-url
```

prints `http://100.104.53.1:8471/?token=<token>`. Open it on the phone while
Tailscale is connected, then add it to the home screen. On first load the
token is exchanged for an HttpOnly session cookie and the address bar is
redirected to a clean URL — the bookmark still holds the token (that is what
re-authenticates you after the session expires or the service restarts), so
treat it like a password and keep it out of screenshots and shared notes. If
it ever leaks, rotate it by editing `WORKBENCH_WEB_AuthToken` in
`~/.config/workbench-web/environment` and running
`systemctl --user restart workbench-web`.
