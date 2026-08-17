import { error } from '@sveltejs/kit';
import type { PageServerLoad } from './$types';

export const load: PageServerLoad = async ({ fetch, locals, params }) => {
	await locals.authorize('User');

	const resp = await fetch(`/api/auctions/${params.id}`);

	if (!resp.ok) {
		error(404, 'No such auction found');
	}

	const auction = await resp.json();

	return { auction };
};
