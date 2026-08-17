# Event Sourcing — Project Mercury

This document describes the event-sourced backend of Project Mercury: a self-hosted auction platform for organization-internal auctions. Users browse and bid on auctions; admins create and manage them.

## Design Principles

- **Append-only event log.** Events are never deleted or mutated once persisted. The system state at any point in time is derived by replaying events.
- **One table per event type.** Each event type maps to its own PostgreSQL table, keeping schemas narrow and queryable.
- **Single shared sequence.** All event tables draw their `event_id` from a single PostgreSQL sequence (`event_id_seq`). This provides a global ordering across all event types.
- **Optimistic concurrency.** Every event inherits `SequenceId` from `StoredEvent`. Before committing a batch of new events, the system verifies that no referenced events have advanced past the `SequenceId` observed during the read phase. Conflicts raise `ContextInconsistentException` and the operation is retried.
- **State reconstruction.** Aggregate state is reconstructed on demand by replaying the relevant event stream for a given entity.

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

### Future Event Types

- **Bid events** — Placed, withdrawn, outbid notifications.
- **User provisioning events** — User created, role changed.

## Concurrency Model

The `DbEventStore` tracks the maximum `SequenceId` observed across all queries issued during a single operation. When the operation completes, it asserts that this maximum has not been exceeded by any intervening write. If it has, the entire operation is retried inside a serializable transaction.

```
Read phase:   observe SequenceId = N from queried events
Write phase:  assert max(SequenceId across referenced tables) == N
              if not → ContextInconsistentException → retry
              if yes → insert new events → commit
```

## Further Reading

- [Auction Lifecycle](auction-lifecycle.md) — Creation, updates, image management, closure invariants and flows.
