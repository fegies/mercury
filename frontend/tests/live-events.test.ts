import { describe, expect, it, vi, beforeEach, type Mock } from 'vitest';

vi.mock('$lib/api', () => ({
	build_browser_client: vi.fn()
}));

vi.mock('$app/environment', () => ({
	browser: true
}));

import { build_browser_client } from '$lib/api';
import type { LiveEvent } from '$lib/client/types.gen';
import {
	handle_live_event,
	on_auction_update,
	on_any_auction_update,
	on_notification,
	start_live_stream
} from '$lib/live';

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
		const notifications: LiveEvent[] = [];
		const updates: LiveEvent[] = [];
		const off_notification = on_notification((event) => notifications.push(event));
		const off_updates = on_any_auction_update((event) => updates.push(event));

		handle_live_event(notification('Outbid'));
		handle_live_event(ping('BidPlaced'));

		expect(notifications).toHaveLength(1);
		expect(notifications[0].type).toBe('Outbid');
		expect(updates).toHaveLength(1);
		expect(updates[0].kind).toBe('AuctionUpdated');

		off_notification();
		off_updates();
		handle_live_event(notification('Won'));

		expect(notifications).toHaveLength(1);
	});

	it('routes auction updates to wildcard and matching per-auction listeners', () => {
		const wildcard: string[] = [];
		const auction1: string[] = [];
		const auction2: string[] = [];
		const off_wildcard = on_any_auction_update((event) => wildcard.push(event.auctionId));
		const off_auction1 = on_auction_update('auction-1', (event) => auction1.push(event.type));
		const off_auction2 = on_auction_update('auction-2', (event) => auction2.push(event.type));

		handle_live_event(ping('Closed', 'auction-1'));
		handle_live_event(ping('Extended', 'auction-2'));

		expect(wildcard).toEqual(['auction-1', 'auction-2']);
		expect(auction1).toEqual(['Closed']);
		expect(auction2).toEqual(['Extended']);

		off_auction1();
		handle_live_event(ping('BidPlaced', 'auction-1'));

		expect(auction1).toEqual(['Closed']);

		off_wildcard();
		off_auction2();
	});

	it('drops per-auction listeners when the auction id is reused after unsubscribe', () => {
		const seen: string[] = [];
		const off = on_auction_update('auction-1', (event) => seen.push(event.type));
		off();

		const second = on_auction_update('auction-1', (event) => seen.push(event.type));
		handle_live_event(ping('BidPlaced'));

		expect(seen).toEqual(['BidPlaced']);
		second();
	});
});

describe('live stream', () => {
	it('opens the stream once and drains it through the router', async () => {
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

		const off = on_any_auction_update((event) => seen.push(event));
		on_notification((event) => seen.push(event));

		start_live_stream();
		start_live_stream();
		await drained;

		expect(build_browser_client).toHaveBeenCalledTimes(1);
		expect(getApiEventsStream).toHaveBeenCalledTimes(1);
		expect(getApiEventsStream).toHaveBeenCalledWith({ credentials: 'same-origin' });
		expect(seen.map((event) => event.kind)).toEqual(['AuctionUpdated', 'Notification']);

		off();
	});
});
