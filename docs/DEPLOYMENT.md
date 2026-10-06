# Deployment

Project Mercury ships as a **single, fully rootless OCI container image**
built with Nix. One image contains everything: the SvelteKit frontend, the
ASP.NET Core backend, and an nginx reverse proxy — started by a small
entrypoint script under **tini** (PID 1); every process runs as the
unprivileged user `mercury` (uid 1000). TLS is terminated upstream, uploaded
images live in a dedicated volume, and all configuration is passed via
environment variables.

## Overview

```
            ┌────────────────────────── container ───────────────────────────┐
            │                                                                │
 client ──▶ TLS terminator / ingress ══ plain HTTP ══▶ nginx :8080           │
            │ (must set X-Forwarded-Proto: https and Host)  │                 │
            │                          ┌────────────────────┴───────────────┐  │
            │   / (UI, static assets)  │            /api/, /signin-oidc      │  │
            │                          ▼                                     ▼  │
            │              SvelteKit node server                 ASP.NET Core │
            │              (127.0.0.1:3000)                      (127.0.0.1:5023)│
            │                                                                │
            │  tini (PID 1) + entrypoint: starts all three services;         │
            │  if one dies, the others are stopped and the container exits   │
            │                                                                │
            │  volume: /var/lib/mercury  ── uploaded images                  │
            └────────────────────────────────────────────────────────────────┘
```

- Everything inside runs as **uid 1000** — the image works under **rootless
  podman/docker** (with subordinate UID/GID ranges configured) and needs no
  capabilities. The container listens on the unprivileged port **8080**;
  publish it with `-p 8080:8080` (or `-p 80:8080` if the host side should be
  80).
- The upstream terminator **must** set `X-Forwarded-Proto: https` and the
  public `Host`; the container propagates forwarded headers on every level
  (see [TLS & proxying](#tls--proxying)).
- If one of the three services crashes, the entrypoint stops the remaining
  ones and the container exits — run it with a **restart policy**
  (`--restart=on-failure`, compose `restart: unless-stopped`, k8s default)
  so the stack comes back up as a whole.

## Build, load, publish

The image is produced by `nix/container.nix` and exposed as
`packages.<system>.container` — a docker-archive tarball:

```bash
nix build .#container
docker load -i ./result        # or: podman load -i ./result
```

To publish to a registry (docker-archive → OCI):

```bash
skopeo copy --dest-creds '<user>:<token>' \
  docker-archive:./result \
  docker://registry.example.org/mercury/mercury:0.0.1
```

The image is built for the host architecture (`x86_64-linux` by default).
Rebuild on an aarch64 Nix machine (or a cross-building builder) for arm64
images. The version/tag is `0.0.1` (see `version` in `nix/container.nix`).
OCI labels and a Docker `HEALTHCHECK` (probes the backend's anonymous
`/api/healthz` through the full proxy chain every 30 s) are part of the
image config.

A smoke test that loads the image into podman inside a NixOS VM and checks
frontend, backend, forwarded headers, volume ownership, healthcheck and
clean shutdown runs with:

```bash
nix build .#tests.<system>.container
```

## Running

**podman** (rootless, the tested path) and **docker** — a plain run, no
special flags:

```bash
podman run -d --restart=on-failure --name mercury \
  -p 8080:8080 \
  -v mercury-images:/var/lib/mercury \
  -e 'ConnectionStrings__DefaultConnection=Host=postgres.internal;Port=5432;Database=mercury;Username=mercury;Password=CHANGE_ME' \
  -e 'OidcConfig__AuthorityUrl=https://sso.example.org' \
  -e 'OidcConfig__ClientId=mercury' \
  -e 'OidcConfig__ClientSecret=CHANGE_ME' \
  mercury:0.0.1
```

`docker run` is identical (same flags). The restart policy is what brings the
stack back if one of the services dies — see [overview](#overview).

Rootless notes:

- Rootless podman/docker need **subordinate UID/GID ranges** for your user
  (the smoke test uses NixOS's `subUidRanges`/`subGidRanges`; on other
  distros `/etc/subuid` + `/etc/subgid`).
- Automatic HEALTHCHECK execution under rootless podman uses systemd user
  timers — enable lingering for the running user (`loginctl enable-linger
  <uid>`) or trigger checks on demand (`podman healthcheck run mercury`).
- For bind mounts under rootless podman, `--userns=keep-id` makes the
  container's uid 1000 your own host uid (then a host-side directory owned
  by you works directly).

`ConnectionStrings__DefaultConnection` and the `OidcConfig__*` variables are
the only required ones; everything else has sane defaults. The container has
no built-in file-based secret loading — inject secrets as plain environment
variables via your orchestrator's secret mechanism (docker secrets must be
materialised into env by the runner, e.g. `env_file` in compose, or
`--env-file`).

## Configuration reference

All variables are .NET-style `Section__Key` environment variables, read by
the backend (or, where noted, the frontend/infrastructure).

### PostgreSQL connection string (required)

`ConnectionStrings__DefaultConnection` — standard **Npgsql** connection
string format (`key=value` pairs separated by `;`):

| Parameter    | Meaning                                    | Notes                                          |
| ------------ | ------------------------------------------ | ---------------------------------------------- |
| `Host`       | Postgres hostname or IP                    | Use a **TCP** host from inside the container   |
| `Port`       | Postgres port                              | `5432` by default                              |
| `Database`   | Database name                              | Must already exist (create it before first run)|
| `Username`   | Role name                                  |                                                |
| `Password`   | Role password                              |                                                |
| `SSL Mode`   | TLS to the database                        | e.g. `Require` for remote databases            |

Examples:

```
Host=postgres.internal;Port=5432;Database=mercury;Username=mercury;Password=secret
Host=10.0.0.5;Port=5432;Database=mercury;Username=mercury;Password=secret;SSL Mode=Require
```

Notes:

- The database **user must exist and own (or be able to create tables in)
  the database**. At startup the backend ensures the `app_events` event-store
  table automatically — no manual migrations.
- If the variable is unset the backend refuses to start with a message
  naming the variable.
- The schema lives in a single event-sourcing table (`app_events`); bid data
  is derived from the event stream, uploaded images are the only blob data
  (see [volume](#data-volume)).

### OIDC / authentication (required)

| Variable                     | Meaning                                    | Default  |
| ---------------------------- | ------------------------------------------ | -------- |
| `OidcConfig__AuthorityUrl`   | OIDC issuer root URL (no `.well-known/…`)  | —        |
| `OidcConfig__ClientId`       | Client id of this app registration         | —        |
| `OidcConfig__ClientSecret`   | Client secret                              | —        |
| `OidcConfig__ProviderType`   | `Zitadel`, `Entra` or `Generic`            | `Generic`|
| `EntraConfig__AdminGroupIds` | JSON array of Entra group ids granting admin (Entra only) | `[]` |

The redirect URI registered with the provider must be
`https://<public-host>/signin-oidc`; the post-signout landing page is
`https://<public-host>/signedout`.

### Optional

| Variable                              | Meaning                                              | Default                    |
| ------------------------------------- | ---------------------------------------------------- | -------------------------- |
| `AuctionConfig__MinBidIncrement`      | Minimum over-bid amount                              | `0.50`                     |
| `ASPNETCORE_ENVIRONMENT`              | .NET environment (keep `Production` in deployment)   | `Production` (set by image)|
| `ForwardedConfig__TrustedProxies__0…` | Extra proxy IPs (one per index) whose `X-Forwarded-*` headers the backend trusts beyond the container's internal proxy | (loopback only) |
| `ImageStorage__BasePath`              | Image storage directory (advanced; see volume)       | `/var/lib/mercury/data/images` (set by image) |

Example — TLS terminator running on a fixed address in front of the
container:

```
ForwardedConfig__TrustedProxies__0=10.0.0.5
ForwardedConfig__TrustedProxies__1=10.0.0.6
```

One IP per indexed variable — a comma-separated single value (e.g.
`ForwardedConfig__TrustedProxies=10.0.0.5,10.0.0.6`) is not supported and
would silently bind to an empty list.

## Data volume

Uploaded images (auction photos under `data/images/auctions/`, profile
pictures under `data/images/users/`, `*.bin` files keyed by GUID) are stored
in the volume mounted at:

```
/var/lib/mercury
├── data/
│   └── images/
│       ├── auctions/<guid>.bin
│       └── users/<guid>.bin
└── tmp/          (service scratch space)
```

- `/var/lib/mercury` is declared as a docker/podman **VOLUME**, so a plain
  `docker run` gets an anonymous volume — attach a named volume or bind
  mount for persistence across container replacement:
  `-v mercury-images:/var/lib/mercury` (named) or
  `-v /srv/mercury:/var/lib/mercury` (bind).
- All processes run as **uid/gid 1000** (user `mercury`). Named volumes
  inherit the ownership from the image; **bind mounts must be owned by uid
  1000 on the host** (`chown 1000:1000 /srv/mercury`), or use
  `--userns=keep-id` under rootless podman.
- Backup: `podman run --rm -v mercury-images:/data alpine tar czf - -C /data .`

## TLS & proxying

The container speaks plain HTTP on port 8080 and must not be exposed publicly —
put a TLS terminator in front of it (your ingress, load balancer, or reverse
proxy). The contract:

1. **TLS terminator → container**: must forward to the container's port 8080
   and set `X-Forwarded-Proto: https` (and the public `Host`). Standard
   behaviour of nginx/traefik/caddy/HAProxy deployments.
2. **Container nginx**: appends to `X-Forwarded-For`
   (`$proxy_add_x_forwarded_for`), passes `X-Forwarded-Proto` through
   (falling back to `http` when absent), and sets `X-Forwarded-Host`.
3. **SvelteKit frontend**: derives the request origin dynamically from
   `X-Forwarded-Proto`/`X-Forwarded-Host` (`PROTOCOL_HEADER`/`HOST_HEADER`),
   so SSR URLs and redirects are correct on any public host.
4. **ASP.NET backend**: `ForwardedHeadersMiddleware` re-derives scheme, host
   and client IP, keeping OIDC redirect URIs and cookies correct. The
   container's nginx (loopback) is trusted out of the box; add upstream
   proxies via `ForwardedConfig__TrustedProxies__N` if you route through more
   hops.

Do **not** send `X-Forwarded-*` headers from untrusted clients directly to
the container port — only the TLS terminator should reach it.

## Health, logs, shutdown

- **HEALTHCHECK** (docker/podman-native): `GET /api/healthz` through the full
  proxy chain every 30 s; `docker ps`/`podman ps` shows `healthy`/
  `unhealthy`, orchestrators can use it as a readiness probe. Under rootless
  podman, automatic execution uses systemd user timers (enable lingering)
  and `podman healthcheck run mercury` triggers a check on demand.
- **Logs**: the entrypoint and all three services write straight to the
  container's stdout/stderr — `docker logs` / `podman logs` show everything,
  no flags needed.
- **Shutdown**: `docker stop`/`podman stop` sends SIGTERM to tini, which
  forwards it to the entrypoint; all three services are stopped gracefully
  (a few seconds) and the container exits cleanly.

## Constraints

- **Single instance.** Live bid notifications (SSE) and the event
  projection run in-process; run exactly one container per deployment and
  scale vertically.
- **Whole-container restarts.** There is no per-service supervisor inside
  the container: if one service crashes, the container exits and the
  runtime's restart policy brings the (whole) stack back — a few seconds of
  downtime. This keeps the image simple; use the restart policy rather than
  expecting partial recovery.
- Postgres must be reachable over TCP from the container.
- Session/auth state lives in the OIDC flow + backend cookies; no local
  state outside the database and the image volume.
