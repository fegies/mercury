import type { AuctionSummary } from '$lib/client/types.gen';

export type { AuctionSummary } from '$lib/client/types.gen';

export function default_auction(): AuctionSummary {
	return {
		id: '',
		title: '',
		description: '',
		minimumPrice: 0,
		closureTime: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
		isClosed: false,
		imageUrls: [],
		currentBid: null
	};
}
