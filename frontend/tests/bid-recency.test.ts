import { describe, expect, it } from 'vitest';

import { BidRecency } from '../src/lib/bid_recency.svelte.js';

describe('bid recency', () => {
	it('prefers whichever snapshot arrived last', () => {
		const recency = new BidRecency();

		// A refetch completed before any bid: the live snapshot wins.
		recency.mark_live();
		expect(recency.live_is_fresher).toBe(true);

		// The user's own bid response is fresher than the pre-bid refetch.
		recency.mark_bid();
		expect(recency.live_is_fresher).toBe(false);

		// The ping refetch for that bid (or a later outbid) completes.
		recency.mark_live();
		expect(recency.live_is_fresher).toBe(true);
	});

	it('keeps preferring the bid response while the stream cannot refetch', () => {
		const recency = new BidRecency();

		recency.mark_live();
		recency.mark_bid();
		expect(recency.live_is_fresher).toBe(false);

		// A second bid with the stream still down stays authoritative.
		recency.mark_bid();
		expect(recency.live_is_fresher).toBe(false);
	});
});
