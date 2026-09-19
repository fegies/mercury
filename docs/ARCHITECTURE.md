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
| `appcore` | Class Library | Domain events, DCB event-sourcing infra, PostgreSQL access |

### Bootstrap (`Program.cs`)

Startup sequence:
1. Load and validate `BackendConfig` from appsettings
2. Register event-sourcing infra (`NpgsqlDataSource`, `DbEventStore`, evaluators, handlers)
3. Register `UserProvisionService` and provider-specific `IUserProvisioner` (Zitadel or generic)
4. Configure cookie + OIDC authentication (openid, profile, email scopes)
5. `OnTicketReceived` triggers `UserProvisionService.ProvisionUser()` to provision users as domain events
6. Authorization: `IsAdmin` policy + fallback `RequireAuthenticatedUser`
7. OpenAPI spec generation (dev mode only)
8. `WebStatusException` middleware for error handling
9. Ensure the `app_events` table exists on real launch (`EventStoreSchema.EnsureCreatedAsync`)
10. Map controllers

### Controllers

| Controller | Routes | Auth | Description |
|---|---|---|---|
| `OidcController` | `GET /api/logout`, `GET /api/login`, `GET /signedout` | None (hidden from OpenAPI) | OIDC login/logout flow |
| `UserInfoController` | `GET /api/userinfo/me` | User | Returns current user info + admin flag |
| `ProfilePictureController` | `GET /api/profilepictures/{id}` | User | Serves locally-stored profile pictures (e.g. Entra photos fetched at login) |
| `AuctionController` | `GET/POST/PATCH /api/auctions...` | User / Admin | Auction CRUD, image upload/serve/removal |
| `LiveController` | `GET /api/events/stream` | User | Server-sent-events stream: targeted notifications (outbid, won, cancelled) + broadcast auction-change pings |

### Services

| Service | Purpose |
|---|---|
| `UserProvisionService` | Provisions the user via `IncomingEventHandler<ProvisionUserInput, UserProvisionResult>` (find-or-create + profile deltas as events), adds `local_userid` claim |
| `ZitadelUserProvisioner` | Builds provisioning input from `picture`/roles claims, maps Zitadel roles to `mercury.role=Admin` claim |
| `EntraUserProvisioner` | Builds provisioning input for Microsoft Entra ID: maps groups to `mercury.role=Admin`, downloads the profile photo from Microsoft Graph at login (via the access token) and stores it locally through `IImageStorage`, setting `ProfilePictureUrl` to the local `/api/profilepictures/{id}` endpoint |
| `GenericUserProvisioner` | Core-claims-only fallback for non-Zitadel providers |
| `ImageStorageService` | Reusable local-disk file storage (`data/images/`, namespaced by area + id) used by auction images and profile pictures |

### Domain Model

**Entities:**
- `StoredEvent` — Abstract base for event sourcing (SequenceId, InsertionTime)
- `AuctionCreated` — Event: AuctionId, Title, ClosureTime
- `UserCreated` — Event: UserId, OidIss, OidSub, Name, Email, ProfilePictureUrl, Role. Acts as the identity→user link record; unique per (OidIss, OidSub), enforced by DCB conflict/retry
- `UserUpdated` — Event: profile deltas keyed on UserId only (Name?, Email?, ProfilePictureUrl?)
- `UserRoleChanged` — Event: observed role transition keyed on UserId only (`Role?`, null clears)

**Event Sourcing Infrastructure** — [Dynamic Consistency Boundary](events/dynamic-consistency-boundary.md):
- `OperationHandler.cs` — DCB query language and orchestrator: `EventSelector`/`ConsistencyBoundary`, `IDecisionFunction`/`DecisionStep`, `IncomingEventHandler`
- `EventStore.cs` — Contracts: `EventContext`, `IEventReader`, `IEventStore`, `ConcurrencyConflictException`
- `DbEventStore.cs` — PostgreSQL-backed implementation: conditional append under READ COMMITTED with advisory locks derived from payload dimensions; re-checks the scope at insert time
- `IncomingEventHandler` — Orchestrates gather → decide → append, retrying the whole pipeline on `ConcurrencyConflictException`
- Handlers, evaluators, and event stores are registered in DI and injected into controllers (controllers never construct them manually)

**Note:** Event types live in `appcore.Entities.Events`.

### Configuration

```csharp
BackendConfig
├── OidcConfig: OidcConfigurationValue
│   ├── AuthorityUrl (required)
│   ├── ClientId (required)
│   ├── ClientSecret (required)
│   └── ProviderType: Zitadel | Entra | Generic
└── EntraConfig: EntraConfigurationValue
    └── AdminGroupIds: string[] (Entra group object ids granting Admin)
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

**Event sourcing:**
- Single `app_events` table; every event row carries a global `sequence_id` from one shared PostgreSQL sequence
- Events are never deleted
- Scoped reads and the append consistency check predicate over `event_type` and `payload` JSON properties; expression indexes can be added per hot path

### Live Events (SSE)

Non-persistent, in-process fan-out of committed domain events to connected
browsers over `GET /api/events/stream`. Built on the in-memory event bus
(`IAppBus`) that `DbEventStore` already feeds after every commit.

- `LiveEventHub` (`appcore/Infra/Live/`) — registry of per-connection bounded
  channels (drop-oldest: a stalled client can never block the bus pump);
  several connections per user (multiple tabs) are supported.
- `LiveEventBridge` — hosted service subscribed to `BidPlaced`,
  `AuctionClosed`, `AuctionCancelled`, `AuctionCloseExtended`. Broadcasts an
  `AuctionUpdated` ping on every event and pushes targeted `Notification`
  toasts: outbid (to the prior leader), won (to the winner), cancelled (to
  every distinct bidder of the auction). Skips all reads while no client is
  connected.
- `OutbidDetection` — state-free fold over the auction's log that
  reconstructs the previous leader on demand (no in-memory mirror, no event
  enrichment). The outbid user is the prior leader only when the incoming
  bid actually took the lead away from them; first bids, self-raises, and
  equal-max raises notify nobody.
- Delivery is transient by design: only events committed while the stream is
  open are delivered; the browser reconnects automatically. The stream sends
  keep-alive comments and sets `X-Accel-Buffering: no` so nginx does not
  buffer it in production deployments.
- Frontend: `src/lib/live.ts` consumes the generated typed SSE method
  (`BackendClient.getApiEventsStream`, reconnection with exponential backoff
  built into the generated client) and routes notifications to Skeleton
  toasts (mounted in the authorized layout) and pings to per-auction
  subscribers; the detail and list pages refetch via `build_browser_client()`
  so prices and highest-bidder badges update live.
- Single-instance only: the fan-out lives inside one backend process, same
  as the in-memory event bus.

## Frontend Architecture

### Routing

**Route groups:**
- `(authorized)/` — All protected pages (layout calls `locals.authorize('User')`)
- `api/` — Server-side API routes
- `signedout/` — Post-logout page

**Routes:**

| Route | Auth | Type | Status |
|---|---|---|---|
| `/` → `/auctions` | User | Redirect | Done |
| `/auctions` | User | Page | Done (listing) |
| `/auctions/[id]` | User | Page | Done (detail + images) |
| `/auctions/[id]/liveticker` | — | SSE stream | Prototype (countdown demo) |
| `/manage-auctions` | Admin | Page | Done (hub with listing + link to create) |
| `/manage-auctions/new` | Admin | Page + Form Action | Done (create auction) |
| `/manage-auctions/[id]` | Admin | Page + Form Actions | Done (edit auction, images, close) |
| `/logout` | — | GET | Done |
| `/signedout` | — | Page | Done |

SvelteKit is stateless: no database access, no server-side session state. Pages/actions only proxy the backend API (`locals.authorize()` for auth redirects); all data and files live in the backend.

### Components

**Common:**
- `UserAvatar` — Avatar with dropdown menu (render snippet)
- `DateCountdownBadge` — Live countdown to expiry date
- `Imageset` — Image gallery with thumbnail strip

**Admin:**
- `EditableAuction` — Form for creating/editing auctions (multipart/form-data)

### API Client

Auto-generated from OpenAPI spec (`backend/openapi/backend.json`) via `@hey-api/openapi-ts`:
- Output: `src/lib/client/`
- Single `BackendClient` class with typed SDK methods
- Factory: `build_client(event)` in `src/lib/api.ts` creates SSR-compatible client
- Factory: `build_browser_client()` in `src/lib/api.ts` creates a
  browser-side client (native fetch, same-origin cookies) for live refetches
  and the SSE stream (`getApiEventsStream`)

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
                                          ├── Provider builds ProvisionUserInput from claims
                                          ├── UserService resolves identity → userId
                                          ├── IncomingEventHandler: find-or-create by (OidIss, OidSub),
                                          │   append UserCreated / UserUpdated / UserRoleChanged deltas
                                          ├── Add local_userid claim from result
                                          └── Provider-specific: stamp mercury.role claim
                                                     ↓
                                          Cookie established → Subsequent requests
```

Frontend SSR: `locals.authorize('User' | 'Admin')` verifies session and returns user info.

## Dev Environment

**Nix flake** provides:
- `dotnet-sdk_10`, `nodejs_26`, `prettier`, `postgresql_18`, `jq`
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
```
