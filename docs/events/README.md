# Event Sourcing — Project Mercury

This document describes the event-sourced backend of Project Mercury: a self-hosted auction platform for organization-internal auctions. Users browse and bid on auctions; admins create and manage them.

## Design Principles

- **Append-only event log.** Events are never deleted or mutated once persisted. The system state at any point in time is derived by replaying events.
- **Single event table.** All events live in `app_events` with a shared global `sequence_id`. This provides a global ordering across all event types.
- **Dynamic Consistency Boundary (DCB).** Write operations do not target fixed aggregates. Each command declares its own consistency boundary — a query over event types and payload properties — and the result is committed only if that boundary is unchanged since the decision's context was loaded. Conflicts are retried with fresh context (sequential consistency by retry). See [Dynamic Consistency Boundary](dynamic-consistency-boundary.md).
- **State reconstruction.** Aggregate state is reconstructed on demand by replaying the events selected by a scoped query for a given entity.

## StoredEvent Base Class

All domain events extend `StoredEvent`:

| Column | Type | Description |
|---|---|---|
| `SequenceId` | `long` | Auto-assigned via `event_id_seq`. Used for optimistic concurrency checks and global ordering. |
| `InsertionTime` | `DateTime` | UTC timestamp of when the event was persisted. |

## Event Catalog

### Auction Lifecycle Events

| Event | Purpose | Key Fields |
|---|---|---|
| `AuctionCreated` | A new auction is created by an admin. | `AuctionId`, `Title`, `Description`, `MinimumPrice`, `ClosureTime` |
| `AuctionUpdated` | An admin modifies auction metadata. | `AuctionId`, optional `Title?`, `Description?`, `MinimumPrice?`, `ClosureTime?` |
| `AuctionImagesAdded` | One or more images are attached to an auction. | `AuctionId`, list of `{ Id, FilePath, Hash }` |
| `AuctionImagesRemoved` | One or more images are detached from an auction. | `AuctionId`, list of image Guids |
| `AuctionClosed` | An auction is closed (manually or by expiry). | `AuctionId`, `Reason` (Manual \| Expired) |

### User Provisioning Events

`UserCreated` is the identity→user link record and carries the `oidIss`/`oidSub` dimensions; all other user events key on `userId` alone. This keeps profile and role history attached to the *user*, not to whichever identity performed the login, so multiple IdP identities can be linked to one user in the future (via additional link events). Roles are managed on the IdP side; role events record observed transitions at login time (authorization itself uses the freshly stamped `mercury.role` claim).

| Event | Purpose | Key Fields |
|---|---|---|
| `UserCreated` | First login of an OIDC identity provisions the user. | `UserId`, `OidIss`, `OidSub`, `Name`, `Email?`, `ProfilePictureUrl?`, `Role?` |
| `UserUpdated` | Profile deltas detected at login. | `UserId`, optional `Name?`, `Email?`, `ProfilePictureUrl?` |
| `UserRoleChanged` | The observed IdP role differs from the recorded one. | `UserId`, `Role?` (null clears) |

Find-or-create is keyed on the `(oidIss, oidSub)` dimensions of `UserCreated`: concurrent first logins serialize on their advisory locks, the loser receives `ConcurrencyConflictException` and its retry adopts the winner's user id. Provisioning resolves identity→userId with a small lookup read up front, then scopes the consistency boundary to `ForUser(userId)`.

### Future Event Types

- **Bid events** — Placed, withdrawn, outbid notifications.

## Concurrency Model

Write operations use a **Dynamic Consistency Boundary** (see [the full scheme](dynamic-consistency-boundary.md)):

- Information gathering is lock-free and declarative: the decision function returns an initial `EventSelector` (event types + payload constraints), the handler reads the context, and the decision may request a wider scope, which triggers a full reload.
- The commit is guarded by a `ConsistencyBoundary` (the accumulated selectors plus `LastPosition` — the global `MAX(sequence_id)` at the last reload).
- Appends run in a short READ COMMITTED transaction. Transaction-scoped advisory locks derived from the payload dimensions the operation reads and writes (acquired in sorted order) prevent write skew. The scope predicate is re-evaluated at insert time; a matched event with `sequence_id > LastPosition` raises `ConcurrencyConflictException`.
- On conflict the entire pipeline is retried with a freshly loaded context. `InvariantViolation` failures abort immediately and are never retried.

```
Gather phase:  read boundary = DNF of selectors, observe LastPosition = MAX(sequence_id)
Decide phase:  pure decision over the snapshot; may widen scope → full reload
Commit phase:  advisory locks on (property, value) keys → re-check scope at insert
               if stale → ConcurrencyConflictException → retry from Gather
               if clean → insert events → commit
```

## Further Reading

- [Auction Lifecycle](auction-lifecycle.md) — Creation, updates, image management, closure invariants and flows.
