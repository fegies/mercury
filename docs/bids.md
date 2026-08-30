# Bid Placement Flow

eBay-style proxy bidding for organization-internal auctions. Users place a *maximum* bid; the
displayed *current price* rises only as much as needed to beat the second-highest maximum plus the
minimum increment.

## Pricing model

- Global minimum increment configured via typed config `AuctionConfig.MinBidIncrement` (default `0.50`).
- Current price = `max(MinimumPrice, secondHighestMax + MinBidIncrement)`.
- With only one bidder, the current price floors at `MinimumPrice`.
- Different users with the **same maximum** resolve in favour of the **earlier** bidder (who keeps
  the lead); the later bidder becomes the second-highest.
- A maximum at or below the current price is **rejected** (HTTP 400).
- Any authenticated user may bid on any published, open auction.
- On close, the winner and winning price are recorded on the `AuctionClosed` event.

## Events

**New:** `BidPlaced`

- `AuctionId` (guid)
- `BidderId` (guid, the user placing the maximum)
- `MaximumAmount` (decimal, the user's maximum)
- `Timestamp`

**Modified:** `AuctionClosed`

- adds `Guid? WinnerUserId`
- adds `decimal? WinningPrice`

## Given / When / Then

### G1 — Place first bid
- **Given** an auction exists that is published and open (not closed)
- **And** no bids have been placed
- **When** an authenticated user submits a maximum bid `M ≥ MinimumPrice`
- **Then** the bid is recorded
- **And** the current price becomes `MinimumPrice`
- **And** that user is the highest bidder

### G2 — Place bid at or below current price (rejected)
- **Given** an auction with a current price `P`
- **When** a user submits a maximum bid `M ≤ P`
- **Then** the request is rejected with a 400 error
- **And** no bid event is appended

### G3 — Outbid an existing higher maximum
- **Given** an auction where the current highest max is `H` by bidder A
- **When** bidder B submits a maximum `M > H`
- **Then** bidder B becomes the highest bidder
- **And** the current price becomes `max(MinimumPrice, H + increment)`

### G4 — Raise own maximum (still behind leader)
- **Given** bidder B holds max `M_B`
- **And** bidder A holds a higher max `M_A`
- **When** bidder B raises their max to `M_B'` where `M_B' ≤ M_A`
- **Then** bidder B's maximum is updated to `M_B'`
- **And** bidder A remains the highest bidder
- **And** the current price rises to `max(MinimumPrice, M_B' + increment)` (the second-highest max went up)

### G5 — Equal maximum, earlier bidder wins
- **Given** bidder A holds the lead with max `M` and the current price equals the floor `MinimumPrice`
- **When** bidder B submits an equal maximum `M`
- **Then** bidder A (earlier) remains the highest bidder
- **And** bidder B becomes the second-highest bidder
- **And** the current price rises to `max(MinimumPrice, M + increment)`

### G6 — Current price display (two bidders)
- **Given** an auction with minimum price `MinimumPrice`
- **And** highest max `M_A` with second-highest max `M_B` (`M_B < M_A`)
- **When** the auction summary is retrieved
- **Then** the current price shown is `max(MinimumPrice, M_B + increment)`
- **And** it equals the full `M_A` only if `M_A` is itself the second-highest or lower than `M_B + increment`

### G7 — Close records winner
- **Given** an auction with at least one bid
- **When** the auction is closed
- **Then** the `AuctionClosed` event records `WinnerUserId` = the highest bidder
- **And** `WinningPrice` = the final current price
- **And** no further bids are accepted (auction is regard as closed)

### G8 — No bids on close
- **Given** an auction with zero bids
- **When** the auction is closed
- **Then** `WinnerUserId` is null (no winner is recorded)

### G9 — Bid on a closed / unpublished / nonexistent auction (rejected)
- **Given** an auction that is closed (or unpublished, or nonexistent)
- **When** a user submits a bid
- **Then** the request is rejected (404 for nonexistent, 400 for closed/unpublished)

### G10 — Self-visibility in the summary
- **Given** the signed-in user has placed the highest bid on an auction
- **When** they retrieve that auction's summary
- **Then** the response includes:
  - the current price (`currentBid`)
  - the current user's maximum (`myHighest`)
  - a flag `isHighestBidder = true`
- **And** for any other signed-in viewer, the response shows only the current price,
  `myHighest = null` and `isHighestBidder = false` — no other bidder's identity or maximum is exposed

## API

- `POST /api/auctions/{id}/bid` — body `{ "maximumAmount": decimal }` → responds with
  `{ "currentBid", "myHighest", "isHighestBidder" }`. Authenticated.
- `GET /api/auctions/{id}` — the summary includes `currentBid`, and, resolved against the
  signed-in user, `myHighest` and `isHighestBidder`.

## Constraints

- The increment is a typed config value bound to `AuctionConfig` in `BackendConfig` and injected via
  DI; it is never reads an arbitrary string key at runtime.
- Controllers stay slim: parse the request, call the injected handler, map the result.
- `BidPlaced` is folded into `AuctionState` and registered in `EventTypeNames` / `EventSerializer`.
