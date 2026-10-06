# Project Mercury

A self-hosted **auction platform** for organization-internal auctions.
Members bid in real time, admins run the show, everything is event-sourced
and auditable — and the whole thing ships as a single container image.

## What it does

- **Live auctions** — place bids, watch the current price update in real
  time (SSE), get outbid/won/cancelled notifications without refreshing.
- **Photos** — admins upload auction images (including straight from a
  phone camera), users get profile pictures from their identity provider.
- **OIDC sign-in** — works with any OpenID Connect provider (Zitadel,
  Microsoft Entra ID, or a generic one); admins are provisioned from
  provider roles/groups.
- **Auditable by design** — every state change is an event in a
  dynamic-consistency-boundary event store (`app_events`), derived state is
  rebuilt from it.
- **Mobile-friendly** — the UI is fully responsive, down to 360 px.

## Architecture

```
browser ──▶ nginx ──┬──▶ SvelteKit frontend (SSR, stateless proxy)
                    └──▶ ASP.NET Core backend ──▶ PostgreSQL (event store)
```

| Piece     | Tech                                                              |
| --------- | ----------------------------------------------------------------- |
| Frontend  | SvelteKit 2 + Svelte 5, Tailwind 4, generated API client          |
| Backend   | ASP.NET Core (.NET 10), Npgsql, DCB event sourcing                |
| Database  | PostgreSQL (single `app_events` event table, auto-created)        |
| Container | Nix-built rootless OCI image: nginx + frontend + backend, tini as PID 1 |

## Quickstart (development)

You need [Nix](https://nixos.org/download) with flakes enabled.

```bash
nix develop          # enter the dev shell (dotnet, node, postgres, …)
devenv up            # start the dev PostgreSQL (database "mercury")
```

The dev shell exports `ConnectionStrings__DefaultConnection`, pointing at
the devenv PostgreSQL socket (trust auth as your OS user); the backend
reads it from the environment — `appsettings.json` ships no credentials.
Without the devenv shell, set the variable yourself (`backend/.env` is
gitignored).

For sign-in, provide the OIDC settings the same way (also via
`backend/.env` or your shell): `OidcConfig__AuthorityUrl`,
`OidcConfig__ClientId`, `OidcConfig__ClientSecret`, and optionally
`OidcConfig__ProviderType` (`Zitadel`, `Entra` or `Generic`). The backend
fails fast at startup if they are missing — never commit them.

Then, in two terminals:

```bash
# terminal 1 — backend on http://localhost:5023
cd backend && dotnet run --project webshell

# terminal 2 — frontend on http://localhost:5173 (proxies /api to :5023)
cd frontend && npm run dev
```

The full contributor guide (conventions, commands, test rules) lives in
[`agents.md`](agents.md).

## Run in production

```bash
nix build .#container          # build the OCI image
podman load -i ./result        # or: docker load -i ./result

podman run -d --restart=on-failure --name mercury \
  -p 8080:8080 \
  -v mercury-images:/var/lib/mercury \
  -e 'ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=mercury;Username=mercury;Password=secret' \
  -e 'OidcConfig__AuthorityUrl=https://sso.example.org' \
  -e 'OidcConfig__ClientId=mercury' \
  -e 'OidcConfig__ClientSecret=secret' \
  mercury:0.0.1
```

The image is **fully rootless** — every process runs as uid 1000, no
capabilities required, so rootless podman/docker work as-is. TLS is
terminated upstream (the container speaks plain HTTP on port 8080 and
understands `X-Forwarded-*`), uploaded images are persisted in the
`/var/lib/mercury` volume, and the Postgres connection string is configured
via `ConnectionStrings__DefaultConnection`.

**→ The complete deployment guide is in [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md).**

## Documentation

| Document                                        | Contents                                     |
| ----------------------------------------------- | -------------------------------------------- |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)  | System architecture, routing, data model     |
| [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md)      | Container deployment, env vars, volume, TLS  |
| [`docs/bids.md`](docs/bids.md)                  | Bidding rules and semantics                  |
| [`docs/events/`](docs/events/README.md)         | Event store and auction lifecycle internals  |
| [`agents.md`](agents.md)                        | Contributor/agent guide (style, commands)    |

## Testing

```bash
cd backend  && dotnet test                          # backend unit tests
cd frontend && npm run test                         # frontend unit tests
nix build .#tests.x86_64-linux.integration          # full e2e: browser, OIDC, bids, SSE
nix build .#tests.x86_64-linux.container            # container smoke test (podman)
```
