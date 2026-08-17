import type { AuctionSummary } from '$lib/types/auction.js';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ fetch, locals }) => {
	await locals.authorize('User');

	const resp = await fetch('/api/auctions');
	const auctions: AuctionSummary[] = resp.ok ? await resp.json() : [];

	return { auctions };
};
