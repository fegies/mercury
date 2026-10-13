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
            │   / (UI, static assets)  │          /api/ (incl. signin-oidc)  │  │
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
- The upstream terminator **must** set `X-Forwarded-Proto: https`, the
  public `Host` and `X-Forwarded-For` (from the connection peer, discarding
  client-supplied values); setting `X-Forwarded-Host` as well keeps the
  contract self-contained at the boundary. The container propagates
  forwarded headers on every level (see [TLS & proxying](#tls--proxying)).
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

A one-click release pipeline (GitHub Actions, runs on the GitHub push mirror)
is provided in `.github/workflows/release.yml`: dispatching it from the
Actions tab builds the image with Nix on a hosted runner and publishes it to
the GitHub Container Registry — tags `latest` and
`<version>-<short-commit-sha>` (version from `nix/container.nix`, e.g.
`ghcr.io/<owner>/<repo>:0.0.1-a1b2c3d4e5`). It authenticates with the
built-in `GITHUB_TOKEN` and publishes right after the build; the podman smoke
test below is not part of the pipeline — run it locally before dispatching a
release.

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
  -e 'OidcConfig__ProviderType=Zitadel|Entra|Generic' \
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
| `OidcConfig__ProviderType`   | `Zitadel`, `Entra` or `Generic`            | — (required choice, no default) |
| `EntraConfig__AdminGroupIds` | JSON array of Entra group ids granting admin (Entra only) | `[]` |

The redirect URI registered with the provider must be
`https://<public-host>/api/signin-oidc` — the backend's OIDC callback path,
which the container routes to the backend via its `/api/` location. Register
`https://<public-host>/signedout` as a **post-logout redirect URI** as
well: the UI's "sign out of SSO" action performs RP-initiated logout
against that address (a plain logout only clears the local cookie).

The authority must use **HTTPS**: outside the `Development` environment the
backend refuses to start with a plain-HTTP `OidcConfig__AuthorityUrl` (this
is the supported escape hatch for test environments with a local mock IdP),
and OIDC metadata over TLS is always enforced there.

#### Sessions

Sessions are the backend's encrypted auth cookie with an **absolute 12-hour
expiry** (no sliding). There is no background token refresh; when the cookie
expires, the next request simply redirects through the standard OIDC
sign-in flow. That round-trip is **invisible** while the user still holds
an SSO session at the identity provider (the default for Zitadel/Entra
users), so users who are actively working rarely notice it. Consequences:

- A role change at the provider takes effect at the user's next sign-in, at
  most 12 hours after their last full login.
- A user who also ended their IdP session must sign in again explicitly —
  the challenge becomes the regular login prompt.
- A request that arrives after the cookie expired (e.g. a bid submitted
  exactly then) fails with 401 instead of being silently refreshed; the
  live-notification stream recovers on its own by re-running the sign-in
  flow.

### Optional

| Variable                              | Meaning                                              | Default                    |
| ------------------------------------- | ---------------------------------------------------- | -------------------------- |
| `AuctionConfig__MinBidIncrement`      | Minimum over-bid amount                              | `0.50`                     |
| `ASPNETCORE_ENVIRONMENT`              | .NET environment (keep `Production` in deployment)   | `Production` (set by image)|
| `ForwardedConfig__TrustedProxies__0…` | Extra proxy IPs (one per index) whose `X-Forwarded-*` headers the backend trusts beyond the container's internal proxy | (loopback only) |
| `ImageStorage__BasePath`              | Image storage directory (advanced; see volume)       | `/var/lib/mercury/data/images` (set by image) |
| `MERCURY_BACKEND_URL`                 | Where the frontend's SSR code reaches the backend API. The image sets it to the container-internal backend, so SSR never dials the public origin | `http://127.0.0.1:5023` (set by image) |

Example — TLS terminator running on a fixed address in front of the
container:

```
ForwardedConfig__TrustedProxies__0=10.0.0.5
ForwardedConfig__TrustedProxies__1=10.0.0.6
```

One IP per indexed variable — a comma-separated single value (e.g.
`ForwardedConfig__TrustedProxies=10.0.0.5,10.0.0.6`) is not supported and
would silently bind to an empty list.

**Rate limiting does not depend on this list.** Authenticated requests are
throttled per user (a fixed-window budget keyed on the user's id, shared
across all endpoints); anonymous requests are not throttled.
`ForwardedConfig__TrustedProxies__N` only restores the real client IP (plus
scheme/host) for redirects, OIDC and logging — the throttle buckets are
keyed on the authenticated user, not on the client address.

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
   and set `X-Forwarded-Proto: https`, the public `Host` (with a matching
   `X-Forwarded-Host`), and `X-Forwarded-For` **from the connection peer** —
   discarding whatever the client sent. Overwriting at the boundary is what
   keeps clients from spoofing their address through the chain, which passes
   `X-Forwarded-For` through untouched. Standard behaviour of
   nginx/traefik/caddy/HAProxy deployments.
2. **Container nginx**: passes `X-Forwarded-For` through untouched (the
   terminator owns it), passes `X-Forwarded-Proto` through (falling back to
   `http` when absent), and sets `X-Forwarded-Host`.
3. **SvelteKit frontend**: derives the request origin dynamically from
   `X-Forwarded-Proto`/`X-Forwarded-Host` (`PROTOCOL_HEADER`/`HOST_HEADER`),
   so SSR URLs and redirects are correct on any public host. Its server-side
   API calls go straight to the backend over the container-internal loopback
   (`MERCURY_BACKEND_URL`, set by the image), so the container does **not**
   need to be able to reach its own public hostname, and they preserve the
   client's `X-Forwarded-For` so the backend sees the real client address.
4. **ASP.NET backend**: `ForwardedHeadersMiddleware` re-derives scheme, host
   and client IP, keeping OIDC redirect URIs and cookies correct. The
   container's nginx (loopback) is trusted out of the box. With the
   pass-through contract above, no further configuration is needed for a
   single TLS terminator; `ForwardedConfig__TrustedProxies__N` is only
   required when an upstream hop *appends* its own address to
   `X-Forwarded-For` (e.g. a managed load balancer you cannot configure to
   strip client-supplied values).

Do **not** send `X-Forwarded-*` headers from untrusted clients directly to
the container port — only the TLS terminator should reach it.

**Publish only on the standard port 443.** The SvelteKit frontend checks
form-POST origins against the request origin it derives from the forwarded
headers (without a port); a browser always includes non-default ports in
its `Origin` header, so a deployment on e.g. `:8443` would have every form
POST rejected. If a non-443 port is unavoidable, forward `X-Forwarded-Port`
from the TLS terminator through the container's nginx and set
`PORT_HEADER=x-forwarded-port` in the container environment so the frontend
compares against the full public origin instead.

### Security headers

The container's nginx stamps a baseline on every response:
`X-Content-Type-Options: nosniff`, `Referrer-Policy:
strict-origin-when-cross-origin`, and `X-Frame-Options: DENY`. What it
deliberately does **not** set is `Strict-Transport-Security` (HSTS): the
container speaks plain HTTP, so HSTS is only meaningful over TLS and belongs
at the terminator. Send e.g. `Strict-Transport-Security:
max-age=31536000; includeSubDomains` from your ingress — the complete
example below sets HSTS and the non-script `Content-Security-Policy`
directives. Script and style protection is sent by the **app itself** as a
per-request nonce CSP (`csp` in the `sveltekit()` plugin options in
`frontend/vite.config.ts`), because
SvelteKit's production bootstrap is an inline script: a static
`script-src` at the terminator would block it, and browsers enforce every
CSP header as an intersection. The integration test runs the real browser
journey behind exactly these headers. The container will pass your headers
through untouched.

### Example TLS terminator (nginx)

Copy-paste starting point for a deployment where this proxy terminates TLS
in front of the container. Adjust `server_name`, the certificate paths and
the upstream address; everything else can stay as-is.

```nginx
# /etc/nginx/conf.d/mercury.conf — TLS terminator for Project Mercury.
upstream mercury {
    # The container is published as e.g. `podman run -p 8080:8080`.
    # Point this at the published port on the same host, or at the
    # container host's address.
    server 127.0.0.1:8080;
    keepalive 16;
}

# Redirect plain-HTTP traffic to HTTPS; keep the ACME challenge local.
server {
    listen 80;
    listen [::]:80;
    server_name auctions.example.org;

    location /.well-known/acme-challenge/ {
        root /var/www/acme;
    }
    location / {
        return 301 https://$host$request_uri;
    }
}

server {
    listen 443 ssl;
    listen [::]:443 ssl;
    # nginx >= 1.25.1; on older releases use: listen 443 ssl http2;
    http2 on;
    server_name auctions.example.org;

    # ---- TLS ------------------------------------------------------------
    ssl_certificate     /etc/letsencrypt/live/auctions.example.org/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/auctions.example.org/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_prefer_server_ciphers off;
    ssl_session_timeout 1d;
    ssl_session_cache shared:mercury_tls:10m;
    ssl_session_tickets off;

    # ---- Security headers -------------------------------------------------
    # HSTS is cached aggressively by browsers: start with a small max-age
    # (e.g. 300) if reverting to HTTP must stay possible, then raise it.
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-Frame-Options "DENY" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;
    add_header Permissions-Policy "camera=(), microphone=(), geolocation=()" always;
    # Everything except script/style policies: the app itself sends those as
    # a per-request nonce CSP (csp in the sveltekit() plugin options in
    # frontend/vite.config.ts — its production bootstrap is an inline script).
    # Browsers intersect ALL CSP headers, so do NOT add
    # script-src, style-src or default-src here: a nonce-less script-src
    # would block the app's bootstrap, and only the app knows the nonces.
    add_header Content-Security-Policy "img-src 'self' data: https:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'" always;

    server_tokens off;

    # ---- Uploads ------------------------------------------------------------
    # Image uploads traverse this proxy as well; keep in sync with the
    # container (nginx 30m, SvelteKit BODY_SIZE_LIMIT=30M, ASP.NET 30 MB).
    client_max_body_size 30m;

    # ---- Proxying ------------------------------------------------------------
    location / {
        proxy_pass http://mercury;
        proxy_http_version 1.1;
        proxy_set_header Connection "";

        # Forwarded-header contract: the container must learn the *public*
        # scheme, host and client address. These directives overwrite
        # anything a client sent — X-Forwarded-For is set from the
        # connection peer ($remote_addr), discarding any client-supplied
        # value, so no forged X-Forwarded-* header reaches the container.
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;

        # Server-sent events (live bid notifications) are long-lived and
        # unbuffered; the backend also sends X-Accel-Buffering: no.
        proxy_read_timeout 1h;
        proxy_buffering off;
    }

    access_log /var/log/nginx/mercury.access.log;
    error_log  /var/log/nginx/mercury.error.log warn;
}
```

Notes:

- Keep the container's port 8080 unreachable from untrusted networks
  (firewall / dedicated podman network): this proxy is the only intended
  client, and the forwarded-header contract above is what makes spoofed
  `X-Forwarded-*` headers harmless.
- A deployment on a non-default port (e.g. `:8443`) breaks SvelteKit's
  form-POST origin check — publish on 443, or forward `X-Forwarded-Port`
  from the TLS terminator and set `PORT_HEADER=x-forwarded-port` in the
  container environment.
- The CSP's `img-src https:` allows IdP-hosted profile pictures; tighten it
  to your identity provider's host if you prefer.

### Read-only root filesystem

The image is prepared to run with a read-only root filesystem — the
recommended hardening:

```bash
podman run -d --restart=on-failure --name mercury \
  --read-only --tmpfs /tmp \
  -p 8080:8080 \
  -v mercury-images:/var/lib/mercury \
  ...
```

Everything stateful already lives outside the root filesystem: nginx keeps
its temp files under `/tmp` (the `--tmpfs /tmp` mount), ASP.NET's scratch
space and DataProtection keys live in the `/var/lib/mercury` volume, and so
do the uploaded images. The smoke test
(`nix build .#tests.<system>.container`) runs this exact configuration.

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
