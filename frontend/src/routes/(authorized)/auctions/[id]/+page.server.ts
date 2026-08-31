import { error, fail } from '@sveltejs/kit';
import { build_client } from '$lib/api';
import { parse_bid_form } from '$lib/bid_form';
import type { Actions, PageServerLoad, RequestEvent } from './$types';

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

export const actions = {
	bid: async (event: RequestEvent) => {
		await event.locals.authorize('User');

		const formData = await event.request.formData();
		const { values, errors } = parse_bid_form(formData);

		if (!values || errors.length > 0) {
			return fail(400, { errors });
		}

		const client = build_client(event);
		const {
			data,
			error: apiError,
		} = await client.postApiAuctionsByIdBid({
			path: { id: event.params.id },
			body: { maximumAmount: values.maximumAmount }
		});

		if (apiError) {
			return fail(400, { errors: [apiError] });
		}

		return { bid: data };
	}
} satisfies Actions;
