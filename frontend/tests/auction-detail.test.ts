import { describe, expect, it, vi, beforeEach, type Mock } from 'vitest';

vi.mock('$lib/api', () => ({
	build_client: vi.fn()
}));

import { build_client } from '$lib/api';
import type { ActionFailure } from '@sveltejs/kit';
import { actions } from '../src/routes/(authorized)/auctions/[id]/+page.server';

type Evt = Parameters<typeof actions.bid>[0];

function event(
	overrides: {
		params?: Record<string, string>;
		form?: FormData;
		fetch?: Mock;
	} = {}
): Evt {
	return {
		locals: { authorize: vi.fn().mockResolvedValue({}) },
		params: overrides.params ?? { id: 'auction-1' },
		request: { formData: () => Promise.resolve(overrides.form ?? new FormData()) },
		fetch: overrides.fetch ?? vi.fn()
	} as unknown as Evt;
}

function failure(result: unknown): ActionFailure<{ errors: string[] }> {
	return result as ActionFailure<{ errors: string[] }>;
}

function make_client(overrides: Record<string, Mock>): Mock {
	const client = overrides;
	(build_client as Mock).mockReturnValue(client);
	return build_client as Mock;
}

describe('auctions/[id] actions', () => {
	beforeEach(() => {
		vi.clearAllMocks();
	});

	describe('bid', () => {
		it('places a bid and returns the resulting bid state', async () => {
			const form = new FormData();
			form.set('maximum_amount', '25');

			const bid = vi.fn().mockResolvedValue({
				data: { currentBid: 20.5, myHighest: 25, isHighestBidder: true },
				error: undefined,
				response: new Response('', { status: 200 })
			});
			make_client({ postApiAuctionsByIdBid: bid });

			const result = await actions.bid(event({ form }));

			expect(bid).toHaveBeenCalledWith({
				path: { id: 'auction-1' },
				body: { maximumAmount: 25 }
			});
			expect(result).toEqual({
				bid: { currentBid: 20.5, myHighest: 25, isHighestBidder: true }
			});
		});

		it('rejects a missing maximum amount without calling the API', async () => {
			const bid = vi.fn();
			make_client({ postApiAuctionsByIdBid: bid });

			const result = failure(await actions.bid(event({ form: new FormData() })));

			expect(bid).not.toHaveBeenCalled();
			expect(result.status).toBe(400);
			expect(result.data.errors).toContain('maximum_amount must be present and a number');
		});

		it('surfaces an API error payload', async () => {
			const form = new FormData();
			form.set('maximum_amount', '5');

			const bid = vi.fn().mockResolvedValue({
				error: { status: 400, detail: 'Bid must exceed the current price.' },
				response: new Response(JSON.stringify({ message: 'Bid must exceed the current price.' }), {
					status: 400
				})
			});
			make_client({ postApiAuctionsByIdBid: bid });

			const result = failure(await actions.bid(event({ form })));

			expect(result.status).toBe(400);
			expect(result.data.errors).toEqual([
				{ status: 400, detail: 'Bid must exceed the current price.' }
			]);
		});
	});
});
