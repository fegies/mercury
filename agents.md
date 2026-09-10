# Project Mercury — Agent Guide

## Project Overview

Self-hosted auction platform for organization-internal auctions. SvelteKit frontend + ASP.NET Core backend + PostgreSQL.

**Read `docs/ARCHITECTURE.md` first** for detailed architecture, routing, and data model.

## Important Rules

- **Branch, commit, PR.** Create a feature branch, commit changes, push to `origin` (the hive fork), and open a PR against upstream `fegies/projectMercury` (`main` base). Always sync from upstream first: `git fetch upstream && git merge upstream/main`.
- **No warnings before you are done.** `dotnet build` and `dotnet test` must produce zero warnings. Frontend `npm run check` must produce zero new errors. Fix or suppress all warnings before considering work complete.
- **Frontend tests must be clean for all PRs.** Run `npm run test` (Vitest) in `frontend/` — all tests must pass. New UI/server-side logic should come with tests where practical (pure helpers in `src/lib/*.test.ts`, SvelteKit form-action/loader tests in `tests/`, never inside `src/routes/` since SvelteKit reserves `+`-prefixed files there).
- **Controllers stay slim.** Never construct handlers, event stores, or services manually inside controllers — register them with DI and inject them via the constructor (prefer the two-parameter `AddScoped<Interface, ImplementingType>()` variant). A controller should only parse the request, call injected collaborators, and map results/errors.
- **Configuration is structured and typed.** Never read arbitrary string keys from `IConfiguration` at runtime. All configuration must be bound to a typed config object at startup (via `builder.Configuration.Bind(config)` on `BackendConfig`) and injected through DI. `BackendConfig` aggregates typed sections (`OidcConfig`, `EntraConfig`, `AuctionConfig`); each section is its own `*ConfigurationValue` class. Provider-specific settings must live in a dedicated config class, not be scraped ad-hoc from config.

## Commands

### Frontend (`frontend/`)

```bash
npm run dev          # Dev server (Vite, proxies /api to localhost:5023)
npm run build        # Production build
npm run check        # Type check (svelte-check)
npm run lint         # Prettier + ESLint
npm run format       # Auto-format with Prettier
npm run test         # Unit tests (Vitest)
npm run test:watch   # Vitest watch mode
npm run coverage     # Vitest with coverage report
npm run openapi-ts   # Regenerate API client from backend OpenAPI spec
```

### Backend (`backend/`)

```bash
dotnet build                              # Build solution
dotnet run --project webshell             # Run backend (port 5023)
dotnet test                               # Run all tests
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
- **Favor concrete return types over `IActionResult`:** For endpoints that return a body, the action returns the concrete type (e.g. `Task<AuctionSummary>`) and errors are expressed by throwing exceptions — `InvariantViolation` (→ 400) and `WebStatusException` subclasses (`BadRequestException`, `NotFoundException`, `ForbidException`) are handled by the request-pipeline middleware in `Program.cs`, so controllers need no try/catch around handlers. For no-body responses (`NoContent`), `ActionResult`/`ActionResult<T>` is fine as long as the `[ProducesResponseType(...)]` annotation is present. Always annotate actions with `[ProducesResponseType]` so the OpenAPI spec reflects the concrete schemas and status codes.

### General

- No comments unless explicitly asked
- Prefer editing existing files over creating new ones
- Follow existing patterns — look at neighboring files before writing new code
- Never commit secrets, keys, or credentials

## Architecture Patterns

### Auth

- **Frontend SSR:** `locals.authorize('User')` or `locals.authorize('Admin')` in `+page.server.ts` / `+layout.server.ts`
- **Backend:** ASP.NET auth with cookie + OIDC. Claims: `local_userid`, `mercury.role`
- **Admin check:** `mercury.role == "Admin"` claim (set during provider-specific provisioning: Zitadel from roles claim, Entra from config-driven group mapping, generic never)
- **Entra ID provider:** selects `EntraUserProvisioner`. Config field `EntraConfig.AdminGroupIds` (group object ids), matched against the `groups` claim. Profile photo is fetched from Microsoft Graph (`/me/photo/$value`) on login using the OIDC access token — the Entra app registration must pre-authorize the Graph `User.Read` scope so the token is valid for Graph, and `groupMembershipClaims` must be enabled (`SecurityGroup`/`ApplicationGroup`) for the `groups` claim to appear.

### API Client

- **All API calls MUST use the generated client.** Never call the backend with raw `fetch`/`event.fetch`/`createClient` directly — always go through `build_client(event)` and one of the `BackendClient` methods (e.g. `client.getApiAuctions`). The only exceptions are the OIDC login/logout flow (`/api/login`, `/api/logout`), which are browser redirects, not data API calls.
- Auto-generated from OpenAPI spec via `@hey-api/openapi-ts`
- Source: `backend/openapi/backend.json` → output: `frontend/src/lib/client/`
- After backend controller changes: export OpenAPI spec, then run `npm run openapi-ts` in frontend
- Client factory: `build_client(event)` in `src/lib/api.ts`
- **No duplicate frontend types.** Use the generated `AuctionSummary`/request types from `src/lib/client/types.gen.ts` directly (re-exported via `src/lib/types/auction.ts` if convenient). To keep generated types "nice" (required vs optional fields), mark always-present C# record properties as `required` so the OpenAPI `required` array is populated.
- Auction creation (`CreateAuction`) takes a JSON `CreateAuctionRequest` without images; upload images afterwards via `UploadImages` (`postApiAuctionsByIdImages`), which is `multipart/form-data` — array-of-file bodies must use the generated `Files` property name.

### Database

- **PostgreSQL** via raw Npgsql (`NpgsqlDataSource`); no ORM
- **Event sourcing** for auctions and users (single `app_events` table); table is ensured at startup (`EventStoreSchema.EnsureCreatedAsync`)
- Database: PostgreSQL, named `mercury`

### Event Sourcing

- `StoredEvent` base class with global `SequenceId`
- `OperationHandler.cs` provides the DCB query language and orchestrator: `EventSelector`/`ConsistencyBoundary`, `IDecisionFunction`, `IncomingEventHandler` with retry logic
- `EventStore.cs` holds the contracts: `EventContext`, `IEventReader`, `IEventStore`, `ConcurrencyConflictException`; `DbEventStore.cs` is the PostgreSQL-backed implementation (advisory locks, SHA1 lock keys)
- User provisioning is event-sourced: `UserCreated` links an OIDC identity `(oidIss, oidSub)` to a `UserId`; `UserUpdated`/`UserRoleChanged` are keyed on `userId` alone so a user can hold multiple linked IdP identities. Roles are IdP-managed, the `mercury.role` claim is stamped from the live IdP claim at login
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
| Domain events | `backend/appcore/Entities/Events/` |
| Event sourcing infra | `backend/appcore/Infra/` |
| Tests | `backend/appcore.Tests/` |
| Config model | `backend/webshell/Configuration/` |
| Auth/authorization | `backend/webshell/Auth/` |
| Nix packaging | `nix/` |

## Notes

- The frontend is stateless: no database access and no server-side session state — pages/actions only proxy the backend API for SSR
- **OpenAPI spec generation:** `dotnet build` generates `webshell/openapi/backend.json` but may miss newly added controllers. Run `dotnet clean && dotnet build` to force regeneration. Fallback: start the server and curl `/openapi/v1.json`.
