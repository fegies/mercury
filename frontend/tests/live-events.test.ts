import { describe, expect, it, vi, beforeEach, type Mock } from 'vitest';

vi.mock('$lib/api', () => ({
	build_browser_client: vi.fn()
}));

vi.mock('$app/environment', () => ({
	browser: true
}));

import { build_browser_client } from '$lib/api';
import type { LiveEvent } from '$lib/client/types.gen';
import { LiveStream } from '$lib/live';

function notification(type: 'Outbid' | 'Won' | 'Cancelled', auctionId = 'auction-1'): LiveEvent {
	return { kind: 'Notification', type, auctionId, title: 'Test Auction', price: 12.5 };
}

function ping(
	type: 'BidPlaced' | 'Closed' | 'Cancelled' | 'Extended',
	auctionId = 'auction-1'
): LiveEvent {
	return { kind: 'AuctionUpdated', type, auctionId, title: null, price: null };
}

describe('live event routing', () => {
	beforeEach(() => {
		vi.clearAllMocks();
	});

	it('routes notifications to notification listeners only', () => {
		const stream = new LiveStream();
		const notifications: LiveEvent[] = [];
		const updates: LiveEvent[] = [];
		const off_notification = stream.on_notification((event) => notifications.push(event));
		const off_updates = stream.on_any_auction_update((event) => updates.push(event));

		stream.handle(notification('Outbid'));
		stream.handle(ping('BidPlaced'));

		expect(notifications).toHaveLength(1);
		expect(notifications[0].type).toBe('Outbid');
		expect(updates).toHaveLength(1);
		expect(updates[0].kind).toBe('AuctionUpdated');

		off_notification();
		off_updates();
		stream.handle(notification('Won'));

		expect(notifications).toHaveLength(1);
	});

	it('routes auction updates to wildcard and matching per-auction listeners', () => {
		const stream = new LiveStream();
		const wildcard: string[] = [];
		const auction1: string[] = [];
		const auction2: string[] = [];
		const off_wildcard = stream.on_any_auction_update((event) => wildcard.push(event.auctionId));
		const off_auction1 = stream.on_auction_update('auction-1', (event) =>
			auction1.push(event.type)
		);
		const off_auction2 = stream.on_auction_update('auction-2', (event) =>
			auction2.push(event.type)
		);

		stream.handle(ping('Closed', 'auction-1'));
		stream.handle(ping('Extended', 'auction-2'));

		expect(wildcard).toEqual(['auction-1', 'auction-2']);
		expect(auction1).toEqual(['Closed']);
		expect(auction2).toEqual(['Extended']);

		off_auction1();
		stream.handle(ping('BidPlaced', 'auction-1'));

		expect(auction1).toEqual(['Closed']);

		off_wildcard();
		off_auction2();
	});

	it('keeps instances independent when several open a stream', () => {
		const first = new LiveStream();
		const second = new LiveStream();
		const first_seen: string[] = [];
		const second_seen: string[] = [];
		first.on_notification((event) => first_seen.push(event.type));
		second.on_notification((event) => second_seen.push(event.type));

		first.handle(notification('Outbid'));

		expect(first_seen).toEqual(['Outbid']);
		expect(second_seen).toEqual([]);
	});
});

describe('live stream lifecycle', () => {
	it('opens the stream once while running and drains it through the router', async () => {
		const stream = new LiveStream();
		const seen: LiveEvent[] = [];
		let finish_stream: () => void = () => {};
		const drained = new Promise<void>((resolve) => {
			finish_stream = resolve;
		});

		async function* fake_stream() {
			yield ping('BidPlaced');
			yield notification('Won');
			finish_stream();
		}

		const getApiEventsStream = vi.fn(async () => ({ stream: fake_stream() }));
		(build_browser_client as Mock).mockReturnValue({ getApiEventsStream });

		stream.on_any_auction_update((event) => seen.push(event));
		stream.on_notification((event) => seen.push(event));

		stream.start();
		stream.start();
		await drained;

		expect(build_browser_client).toHaveBeenCalledTimes(1);
		expect(getApiEventsStream).toHaveBeenCalledTimes(1);
		expect(getApiEventsStream).toHaveBeenCalledWith(
			expect.objectContaining({ credentials: 'same-origin' })
		);
		expect(seen.map((event) => event.kind)).toEqual(['AuctionUpdated', 'Notification']);
	});

	it('aborts the stream on stop and opens a fresh one on restart', async () => {
		const stream = new LiveStream();
		let abort_first: () => void = () => {};
		const first_drained = new Promise<void>((resolve) => {
			abort_first = resolve;
		});

		function hanging_stream(signal: AbortSignal) {
			return (async function* () {
				try {
					yield ping('BidPlaced');
					await new Promise<void>((resolve) => {
						// An already-aborted signal never fires the event (same
						// semantics the generated SSE client relies on).
						if (signal.aborted) {
							resolve();
							return;
						}
						signal.addEventListener('abort', () => resolve());
					});
				} finally {
					// Runs on abort completion and on the pump's early return,
					// mirroring the generated client's cleanup.
					abort_first();
				}
			})();
		}

		const getApiEventsStream = vi.fn(async (options: { signal: AbortSignal }) => ({
			stream: hanging_stream(options.signal)
		}));
		(build_browser_client as Mock).mockReturnValue({ getApiEventsStream });

		stream.start();
		expect(getApiEventsStream).toHaveBeenCalledTimes(1);

		stream.stop();
		await first_drained;

		stream.start();
		expect(getApiEventsStream).toHaveBeenCalledTimes(2);
		expect(getApiEventsStream).toHaveBeenLastCalledWith(
			expect.objectContaining({ signal: expect.any(AbortSignal) })
		);
		stream.stop();
	});

	it('stops one instance without touching another', async () => {
		let finish_first: () => void = () => {};
		const first_done = new Promise<void>((resolve) => {
			finish_first = resolve;
		});

		async function* endless_stream() {
			while (true) {
				yield ping('BidPlaced');
			}
		}

		async function* abortable_stream() {
			yield ping('BidPlaced');
			finish_first();
		}

		const first = new LiveStream();
		const second = new LiveStream();
		const getApiEventsStream = vi
			.fn()
			.mockResolvedValueOnce({ stream: endless_stream() })
			.mockResolvedValueOnce({ stream: abortable_stream() });
		(build_browser_client as Mock).mockReturnValue({ getApiEventsStream });

		first.start();
		second.start();
		expect(getApiEventsStream).toHaveBeenCalledTimes(2);

		// Stopping the first must not disturb the second's pump.
		first.stop();
		await first_done;

		expect(getApiEventsStream).toHaveBeenCalledTimes(2);
		second.stop();
	});
});
