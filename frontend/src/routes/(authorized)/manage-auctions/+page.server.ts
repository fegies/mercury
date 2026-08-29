import { build_client } from '$lib/api';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async (event) => {
	await event.locals.authorize('Admin');

	const client = build_client(event);
	const { data, error: apiError, response } = await client.getApiAuctions();

	if (apiError) {
		throw new Error(`Failed to load auctions (${response?.status})`);
	}

	return { auctions: data ?? [] };
};
