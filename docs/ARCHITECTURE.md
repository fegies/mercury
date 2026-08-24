# Project Mercury — Architecture

Self-hosted auction platform for organization-internal auctions. Users browse and bid on auctions; admins create and manage them.

## Tech Stack

| Layer | Technology | Version |
|---|---|---|
| Frontend | SvelteKit + Svelte 5 (runes) | 2.16 / 5.0 |
| UI | Skeleton UI (Rocket theme) + Tailwind CSS 4 | 3.1 / 4.0 |
| Backend | ASP.NET Core (C#) | 10.0 |
| Database | PostgreSQL via Entity Framework Core | 10.0 |
| Auth | OIDC (cookie + OpenIdConnect) | — |
| API Client | @hey-api/openapi-ts (auto-generated) | 0.99 |
| Dev Env | Nix flakes + direnv | — |

## Project Layout

| Directory | Purpose |
|---|---|
| `backend/webshell/` | ASP.NET host — controllers, auth, services |
| `backend/appcore/` | Domain — entities, DbContext, migrations, infra |
| `frontend/src/lib/` | Shared code — API client, components, types |
| `frontend/src/routes/` | SvelteKit file-based routing |
| `nix/` | Nix packaging |

## Backend Architecture

### Projects

| Project | Type | Purpose |
|---|---|---|
| `webshell` | Web SDK | HTTP layer — controllers, OIDC auth, service registration |
| `appcore` | Class Library | Domain entities, EF DbContext, migrations, event-sourcing infra |

### Bootstrap (`Program.cs`)

Startup sequence:
1. Load and validate `BackendConfig` from appsettings
2. Register EF Core (`ApplicationDbContext` with Npgsql/PostgreSQL)
3. Register `UserProvisionService` and provider-specific `IUserProvisioner` (Zitadel or generic)
4. Configure cookie + OIDC authentication (openid, profile, email scopes)
5. `OnTicketReceived` triggers `UserProvisionService.ProvisionUser()` to create/update users
6. Authorization: `IsAdmin` policy + fallback `RequireAuthenticatedUser`
7. OpenAPI spec generation (dev mode only)
8. `WebStatusException` middleware for error handling
9. Auto-migrate database on real launch
10. Map controllers

### Controllers

| Controller | Routes | Auth | Description |
|---|---|---|---|
| `OidcController` | `GET /api/logout`, `GET /api/login`, `GET /signedout` | None (hidden from OpenAPI) | OIDC login/logout flow |
| `UserInfoController` | `GET /api/userinfo/me` | User | Returns current user info + admin flag |

### Services

| Service | Purpose |
|---|---|
| `UserProvisionService` | Finds or creates `UserEntity` from OIDC claims, adds `local_userid` claim |
| `ZitadelUserProvisioner` | Extracts profile picture from `picture` claim, maps Zitadel roles to `mercury.role=Admin` |
| `GeneriUserProvisioner` | No-op fallback for non-Zitadel providers |

### Domain Model

**Entities:**
- `UserEntity` — Internal user record (Id, OidIss, OidSub, Name, Email, ProfilePictureUrl). Unique on (OidIss, OidSub).
- `StoredEvent` — Abstract base for event sourcing (SequenceId, InsertionTime)
- `AuctionCreated` — Event: AuctionId, Title, ClosureTime

**Event Sourcing Infrastructure** (`OperationHandler.cs`) — [Dynamic Consistency Boundary](events/dynamic-consistency-boundary.md):
- `EventSelector` / `ConsistencyBoundary` — Declarative scopes: event types + payload constraints; a boundary is a disjunction of selectors plus `LastPosition`
- `IEventReader` / `EventContext` — Lock-free scoped reads; the context is the loaded snapshot plus its head
- `IDecisionFunction<TEvent, TResult>` / `DecisionStep` — Pure synchronous decisions that may request a wider scope (`NeedMoreContext`) or complete with a result and optional events
- `IEventStore` / `DbEventStore` — Conditional append under READ COMMITTED with advisory locks derived from payload dimensions; re-checks the scope at insert time
- `IncomingEventHandler` — Orchestrates gather → decide → append, retrying the whole pipeline on `ConcurrencyConflictException`

**Note:** Event types live in `appcore.Entities.Events`.

### Configuration

```csharp
BackendConfig
├── OidcConfig: OidcConfigurationValue
│   ├── AuthorityUrl (required)
│   ├── ClientId (required)
│   ├── ClientSecret (required)
│   └── ProviderType: Zitadel | Entra | Generic
```

### Authorization

- **Fallback policy:** All endpoints require authentication
- **`IsAdmin` policy:** Checks `mercury.role == "Admin"` claim (set during Zitadel provisioning)
- Admin-only endpoints: `/manage-auctions/*`

### Error Handling

Custom `WebStatusException` hierarchy (extends `Exception`):
- `ForbidException` (403)
- Caught by middleware in `Program.cs` which sets the HTTP status code

### Database Schema (PostgreSQL)

**Users (managed via EF migrations):**
- `Users` — Id (uuid PK), OidIss, OidSub, Name, Email, ProfilePictureUrl

**Event sourcing:**
- Single `auction_events` table; every event row carries a global `sequence_id` from one shared PostgreSQL sequence
- Events are never deleted
- Scoped reads and the append consistency check predicate over `event_type` and `payload` JSON properties; expression indexes can be added per hot path

## Frontend Architecture

### Routing

**Route groups:**
- `(authorized)/` — All protected pages (layout calls `locals.authorize('User')`)
- `admin/` — Admin config panel (cookie-based auth)
- `api/` — Server-side API routes
- `signedout/` — Post-logout page

**Routes:**

| Route | Auth | Type | Status |
|---|---|---|---|
| `/` → `/auctions` | User | Redirect | Done |
| `/auctions` | User | Page | WIP (listing commented out) |
| `/auctions/[id]` | User | Page | Done (detail + images) |
| `/auctions/[id]/liveticker` | — | SSE stream | Prototype (countdown demo) |
| `/mybids` | User | Page | WIP (query incomplete) |
| `/manage-auctions` | Admin | Page | Done (hub with link to create) |
| `/manage-auctions/new` | Admin | Page + Form Action | Done (create auction) |
| `/logout` | — | GET | Done |
| `/admin` | Cookie | Page | Done (config panel) |
| `/admin/login` | — | Page | Done (password form) |
| `/signedout` | — | Page | Done |
| `/api/users/picture/[user_id]` | User | GET | Done (profile pic proxy) |
| `/auction_images/[image_id]` | User | GET | Done (image proxy with ETag) |

### Components

**Common:**
- `UserAvatar` — Avatar with dropdown menu (render snippet)
- `DateCountdownBadge` — Live countdown to expiry date
- `Imageset` — Image gallery with thumbnail strip

**Admin:**
- `EditableAuction` — Form for creating/editing auctions (multipart/form-data)
- `OAuthConfigList` — OAuth provider management
- `OAuthProvider` — Individual OAuth provider form
- `UserConfiguration` — User role toggle

### API Client

Auto-generated from OpenAPI spec (`backend/openapi/backend.json`) via `@hey-api/openapi-ts`:
- Output: `src/lib/client/`
- Single `BackendClient` class with typed SDK methods
- Factory: `build_client(event)` in `src/lib/api.ts` creates SSR-compatible client

### Styling

- Tailwind CSS 4 with `@tailwindcss/forms` and `@tailwindcss/typography` plugins
- Skeleton UI with Rocket theme
- `.prettierrc`: tabs, single quotes, no trailing commas, 100 char width

## Auth Flow

```
User → Protected Route → OIDC Challenge → IdP (Zitadel/Entra/Generic)
                                                    ↓
                                          Auth code → Token exchange
                                                    ↓
                                          OnTicketReceived fires
                                                    ↓
                                          UserProvisionService.ProvisionUser()
                                          ├── Find/create UserEntity by (OidIss, OidSub)
                                          ├── Update email, name
                                          ├── Add local_userid claim
                                          └── Provider-specific: map roles, profile pic
                                                    ↓
                                          Cookie established → Subsequent requests
```

Frontend SSR: `locals.authorize('User' | 'Admin')` verifies session and returns user info.

## Dev Environment

**Nix flake** provides:
- `dotnet-sdk_10`, `dotnet-ef`, `nodejs_26`, `prettier`, `postgresql_18`, `jq`
- PostgreSQL service (database: `mercury`)
- `frontend` process: `cd frontend && npm run dev`

**Vite dev proxy:** `/api` → `http://localhost:5023` (backend)

**Key commands:**
```bash
# Frontend
cd frontend && npm run dev        # Dev server
cd frontend && npm run check      # Type check
cd frontend && npm run lint       # Prettier + ESLint
cd frontend && npm run build      # Production build
cd frontend && npm run openapi-ts # Regenerate API client

# Backend
cd backend && dotnet build        # Build
cd backend && dotnet run --project webshell  # Run backend
dotnet ef database update         # Apply migrations
```

## Current State

**Active early development.** Known gaps:
- Old API classes (`AdminApi`, `UserApi`, `OAuthApi`) referenced but removed — migration to generated client in progress
- Liveticker SSE is a demo prototype
- Admin panel uses separate cookie-based auth (not OIDC)
- `nix/backend_package.nix` references `rustPlatform` — leftover from previous Rust backend
