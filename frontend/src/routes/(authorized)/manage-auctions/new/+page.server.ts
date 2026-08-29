import { fail, redirect } from '@sveltejs/kit';
import { parse_auction_form, read_error_message, to_auction } from '$lib/auction_form';
import { build_client } from '$lib/api';
import type { AuctionSummary } from '$lib/types/auction.js';
import type { Actions, RequestEvent } from './$types';

export const actions = {
	default: async (event: RequestEvent) => {
		await event.locals.authorize('Admin');

		const formData = await event.request.formData();
		const errors: string[] = [];

		const { values } = parse_auction_form(formData);
		const auction: AuctionSummary = values
			? to_auction(values)
			: {
					id: '',
					title: '',
					description: '',
					minimumPrice: 0,
					closureTime: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
					isClosed: false,
					isPublished: false,
					imageUrls: [],
					currentBid: null
				};

		if (values) {
			const client = build_client(event);
			const {
				data,
				error: apiError,
				response
			} = await client.postApiAuctions({
				body: {
					title: values.title,
					description: values.description,
					minimumPrice: values.minimumPrice,
					closureTime: values.closureTime,
					isPublished: values.isPublished
				}
			});

			if (apiError) {
				errors.push(await read_error_message(response));
			} else {
				redirect(303, `/manage-auctions/${data}`);
			}
		}

		return fail(400, { auction, errors });
	}
} satisfies Actions;
