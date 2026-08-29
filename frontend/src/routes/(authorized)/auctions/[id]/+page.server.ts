import { error } from '@sveltejs/kit';
import { build_client } from '$lib/api';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async (event) => {
	await event.locals.authorize('User');

	const client = build_client(event);
	const { data, error: apiError } = await client.getApiAuctionsById({
		path: { id: event.params.id }
	});

	if (apiError) {
		throw error(404, 'No such auction found');
	}

	return { auction: data };
};
