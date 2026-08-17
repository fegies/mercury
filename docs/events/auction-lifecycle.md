# Auction Lifecycle

This document covers the full lifecycle of an auction in Project Mercury: creation, updates, image management, and closure. Each section describes the relevant events, invariants, user stories, and flow.

---

## Auction Creation

### Event

`AuctionCreated`

| Field | Type | Description |
|---|---|---|
| `AuctionId` | `Guid` | Unique identifier for the auction. Generated server-side. |
| `Title` | `string` | Display name of the auction. |
| `Description` | `string` | Longer description of the item or auction terms. |
| `MinimumPrice` | `decimal` | Lowest acceptable bid. |
| `ClosureTime` | `DateTime` | UTC timestamp after which no new bids are accepted. |

### Invariants

- `Title` must be non-empty.
- `MinimumPrice` must be greater than or equal to 0.
- `ClosureTime` must be in the future at the time of creation.
- Only users with the Admin role may create auctions.

### User Story

As an admin, I want to create a new auction with a title, description, minimum price, and closure time so that organization members can begin bidding.

### Flow

1. Admin submits the auction form via the `/manage-auctions/new` route.
2. The backend validates all fields against the invariants above.
3. An `AuctionCreated` event is persisted.
4. The frontend redirects to the newly created auction detail page.

---

## Auction Updates

### Event

`AuctionUpdated`

| Field | Type | Description |
|---|---|---|
| `AuctionId` | `Guid` | The auction being modified. |
| `Title` | `string?` | New title, or null if unchanged. |
| `Description` | `string?` | New description, or null if unchanged. |
| `MinimumPrice` | `decimal?` | New minimum price, or null if unchanged. |
| `ClosureTime` | `DateTime?` | New closure time, or null if unchanged. |

All optional fields are nullable. Only the fields present (non-null) in the event represent changes. This keeps events minimal and allows consumers to distinguish intentional changes from unchanged values.

### Invariants

- The target auction must not already be closed.
- If `Title` is provided, it must be non-empty.
- If `MinimumPrice` is provided, it must be greater than or equal to 0.
- If `ClosureTime` is provided, it must be in the future.
- Only users with the Admin role may update auctions.

### User Story

As an admin, I want to edit the title, description, minimum price, or closure time of an open auction so that I can correct mistakes or adjust terms before bidding ends.

### Flow

1. Admin opens the edit form for an existing auction.
2. The backend resolves which fields have actually changed.
3. An `AuctionUpdated` event is persisted with only the changed fields set.
4. The frontend reflects the updated state on the auction detail page.

---

## Image Management

### Adding Images

#### Event

`AuctionImagesAdded`

| Field | Type | Description |
|---|---|---|
| `AuctionId` | `Guid` | The auction receiving images. |
| `Images` | `List<ImageRecord>` | List of added images. |

Each `ImageRecord`:

| Field | Type | Description |
|---|---|---|
| `Id` | `Guid` | Unique identifier for the image. |
| `FilePath` | `string` | Path to the stored image file on disk. |
| `Hash` | `string` | Content hash for deduplication and integrity checks. |

#### Invariants

- The target auction must not be closed.
- Only users with the Admin role may add images.

#### User Story

As an admin, I want to upload one or more images to an auction so that bidders can see what is being auctioned.

#### Flow

1. Admin submits images via the auction edit form.
2. Images are stored on disk and their metadata is recorded.
3. An `AuctionImagesAdded` event is persisted with the list of image records.
4. The frontend updates the auction's image gallery.

### Removing Images

#### Event

`AuctionImagesRemoved`

| Field | Type | Description |
|---|---|---|
| `AuctionId` | `Guid` | The auction losing images. |
| `ImageIds` | `List<Guid>` | Identifiers of the images to remove. |

#### Invariants

- The target auction must not be closed.
- Only users with the Admin role may remove images.
- The referenced image IDs must exist on the target auction.

#### User Story

As an admin, I want to remove images from an auction so that incorrect or outdated photos are no longer shown to bidders.

#### Flow

1. Admin selects images to remove from the auction.
2. An `AuctionImagesRemoved` event is persisted with the list of image IDs.
3. The frontend removes the corresponding images from the gallery.

---

## Auction Closure

### Event

`AuctionClosed`

| Field | Type | Description |
|---|---|---|
| `AuctionId` | `Guid` | The auction being closed. |
| `Reason` | `enum` | `Manual` — admin closed the auction early. `Expired` — the closure time was reached. |

### Invariants

- The target auction must not already be closed.
- Only users with the Admin role may manually close an auction. Expired closure is triggered by the system.

### User Story

As an admin, I want to close an auction before its scheduled end time so that bidding stops immediately. As the system, I want to automatically close auctions when their closure time expires.

### Flow

#### Manual Closure

1. Admin triggers closure on an open auction.
2. An `AuctionClosed` event is persisted with `Reason = Manual`.
3. The frontend marks the auction as closed and disables further bidding.

#### Automatic Closure

1. A background process checks for auctions past their `ClosureTime`.
2. An `AuctionClosed` event is persisted with `Reason = Expired`.
3. The auction is marked as closed in all subsequent queries.

---

## State Reconstruction

Current auction state is derived by replaying events in `SequenceId` order:

```
State = fold over [AuctionCreated, AuctionUpdated..., AuctionImagesAdded..., AuctionImagesRemoved..., AuctionClosed]
```

- After `AuctionCreated`: auction exists with initial field values.
- After each `AuctionUpdated`: only the non-null fields are merged into the current state.
- After `AuctionImagesAdded`: new images are appended to the image list.
- After `AuctionImagesRemoved`: referenced images are removed from the image list.
- After `AuctionClosed`: the auction is marked as closed and immutably frozen — no further updates, image changes, or bids are allowed.

An auction that has received an `AuctionClosed` event is considered terminal. All mutation operations must check for this state and reject changes with an appropriate error.
