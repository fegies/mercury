import type { AuctionSummary } from '$lib/types/auction.js';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ fetch, locals }) => {
	await locals.authorize('Admin');

	const resp = await fetch('/api/auctions');
	if (!resp.ok) {
		throw new Error(`Failed to load auctions (${resp.status})`);
	}
	const auctions: AuctionSummary[] = await resp.json();

	return { auctions };
};
