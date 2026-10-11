import { build_client } from '#lib/api.js';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async (event) => {
	await event.locals.authorize('User');

	const client = build_client(event);
	const { data } = await client.getApiAuctions();

	return { auctions: data ?? [] };
};
