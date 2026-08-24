# Project Mercury — Agent Guide

## Project Overview

Self-hosted auction platform for organization-internal auctions. SvelteKit frontend + ASP.NET Core backend + PostgreSQL.

**Read `docs/ARCHITECTURE.md` first** for detailed architecture, routing, and data model.

## Important Rules

- **Always create a PR** for any changes made to the codebase. Commit, push to a feature branch, and open a pull request.
- **Always sync from upstream** before starting work. Run `git fetch upstream && git merge upstream/main` on your branch to stay up to date.
- **No warnings before you are done.** `dotnet build` and `dotnet test` must produce zero warnings. Frontend `npm run check` must produce zero new errors. Fix or suppress all warnings before considering work complete.
- **Controllers stay slim.** Never construct handlers, event stores, or services manually inside controllers — register them with DI and inject them via the constructor (prefer the two-parameter `AddScoped<Interface, ImplementingType>()` variant). A controller should only parse the request, call injected collaborators, and map results/errors.

## Commands

### Frontend (`frontend/`)

```bash
npm run dev          # Dev server (Vite, proxies /api to localhost:5023)
npm run build        # Production build
npm run check        # Type check (svelte-check)
npm run lint         # Prettier + ESLint
npm run format       # Auto-format with Prettier
npm run openapi-ts   # Regenerate API client from backend OpenAPI spec
```

### Backend (`backend/`)

```bash
dotnet build                              # Build solution
dotnet run --project webshell             # Run backend (port 5023)
dotnet test                               # Run all tests
dotnet ef database update                 # Apply EF migrations
dotnet ef migrations add <Name>           # Add new migration (in appcore dir)
dotnet tool run openapi spec --output openapi/backend.json  # Export OpenAPI spec
```

### Dev Environment

```bash
devenv up       # Start PostgreSQL service
nix develop     # Enter dev shell
```

## Code Style

### Frontend (TypeScript/Svelte)

- **Svelte 5 runes:** Use `$props()`, `$state()`, `$derived`, `{@render children()}` — not Svelte 4 syntax
- **Indentation:** Tabs (Prettier enforced)
- **Quotes:** Single quotes
- **Print width:** 100 characters
- **No trailing commas**
- **TypeScript:** Strict mode, use `type` imports where possible
- **Components:** `.svelte` files in `src/lib/components/{admin,common}/`
- **File naming:** `snake_case.svelte` for components (existing convention)

### Backend (C#)

- **Primary constructors** for DI (C# 12+ pattern used throughout)
- **File-scoped namespaces:** `namespace backend.Controllers;`
- **Required properties:** `public required string Name { get; set; }`
- **Nullable:** Enabled globally
- **.NET 10** target framework

### General

- No comments unless explicitly asked
- Prefer editing existing files over creating new ones
- Follow existing patterns — look at neighboring files before writing new code
- Never commit secrets, keys, or credentials

## Architecture Patterns

### Auth

- **Frontend SSR:** `locals.authorize('User')` or `locals.authorize('Admin')` in `+page.server.ts` / `+layout.server.ts`
- **Backend:** ASP.NET auth with cookie + OIDC. Claims: `local_userid`, `mercury.role`
- **Admin check:** `mercury.role == "Admin"` claim (set during Zitadel provisioning)

### API Client

- Auto-generated from OpenAPI spec via `@hey-api/openapi-ts`
- Source: `backend/openapi/backend.json` → output: `frontend/src/lib/client/`
- After backend controller changes: export OpenAPI spec, then run `npm run openapi-ts` in frontend
- Client factory: `build_client(event)` in `src/lib/api.ts`

### Database

- **EF Core** for `Users` table (migrations in `appcore/Migrations/`)
- **Raw SQL** (postgres-js) for auctions, bids, images in frontend server routes
- Database: PostgreSQL, named `mercury`

### Event Sourcing

- `StoredEvent` base class with global `SequenceId`
- `OperationHandler.cs` provides the DCB query language and orchestrator: `EventSelector`/`ConsistencyBoundary`, `IDecisionFunction`, `IncomingEventHandler` with retry logic
- `EventStore.cs` holds the contracts: `EventContext`, `IEventReader`, `IEventStore`, `ConcurrencyConflictException`; `DbEventStore.cs` is the PostgreSQL-backed implementation (advisory locks, SHA1 lock keys)
- Handlers and evaluators are registered in DI (`StartupExtension.RegisterAppcoreServices`) and injected into controllers — never constructed manually
- Commits run under READ COMMITTED with advisory locks derived from payload dimensions; `IncomingEventHandler` retries on `ConcurrencyConflictException` (sequential consistency by retry)
- Full scheme: `docs/events/dynamic-consistency-boundary.md`

## File Locations

| What | Where |
|---|---|
| Route pages | `frontend/src/routes/` |
| Svelte components | `frontend/src/lib/components/` |
| Generated API client | `frontend/src/lib/client/` |
| Domain types | `frontend/src/lib/types/` |
| Backend controllers | `backend/webshell/Controllers/` |
| Backend services | `backend/webshell/Services/impl/` |
| Domain entities | `backend/appcore/Entities/` |
| EF migrations | `backend/appcore/Migrations/` |
| Event sourcing infra | `backend/appcore/Infra/` |
| Tests | `backend/appcore.Tests/` |
| Config model | `backend/webshell/Configuration/` |
| Auth/authorization | `backend/webshell/Auth/` |
| Nix packaging | `nix/` |

## Known Issues / WIP

- Old API classes (`AdminApi`, `UserApi`) were removed; use generated `BackendClient` instead
- `frontend/src/lib/types/users.ts` — `User` type is missing `export` keyword
- `default_auction()` in `types/auction.ts` has a bug: `new Date(new Date().getDate() + ...)` should be `new Date().getTime() + ...`
- Auction/bids/images tables are not EF-managed; schema is only in frontend raw SQL
- `nix/backend_package.nix` references Rust tooling — stale/ignore
- **OpenAPI spec generation:** The `Microsoft.Extensions.ApiDescription.Server` build-time target generates `webshell/openapi/backend.json`, but it may not pick up newly added controllers. After adding a new controller, run `dotnet clean && dotnet build` to force regeneration. If that still doesn't work, start the server and curl `/openapi/v1.json`.
