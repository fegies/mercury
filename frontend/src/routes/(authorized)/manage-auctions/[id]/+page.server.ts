import { error, fail } from '@sveltejs/kit';
import { build_client } from '$lib/api';
import {
	parse_auction_form,
	read_error_message,
	selected_files,
	to_auction
} from '$lib/auction_form';
import type { AuctionSummary } from '$lib/types/auction.js';
import type { Actions, PageServerLoad, RequestEvent } from './$types';

export const load: PageServerLoad = async ({ fetch, locals, params }) => {
	await locals.authorize('Admin');

	const resp = await fetch(`/api/auctions/${params.id}`);
	if (!resp.ok) {
		error(404, 'No such auction found');
	}

	const auction: AuctionSummary = await resp.json();

	return { auction };
};

export const actions = {
	update: async (event: RequestEvent) => {
		await event.locals.authorize('Admin');

		const formData = await event.request.formData();
		const errors: string[] = [];

		const { values } = parse_auction_form(formData);

		if (values) {
			const client = build_client(event);
			const { error: apiError, response } = await client.patchApiAuctionsById({
				path: { id: event.params.id },
				body: {
					title: values.title,
					description: values.description,
					minimumPrice: values.minimumPrice,
					closureTime: values.closureTime
				}
			});

			if (apiError) {
				errors.push(await read_error_message(response));
			}
		}

		if (errors.length > 0) {
			return fail(400, { auction: values ? to_auction(values) : undefined, errors });
		}

		return { success: true };
	},

	add_images: async (event: RequestEvent) => {
		await event.locals.authorize('Admin');

		const formData = await event.request.formData();
		const files = selected_files(formData);

		if (files.length === 0) {
			return fail(400, { errors: ['No images selected.'] });
		}

		const body = new FormData();
		for (const file of files) {
			body.append('files', file);
		}

		const resp = await event.fetch(`/api/auctions/${event.params.id}/images`, {
			method: 'POST',
			body
		});

		if (!resp.ok) {
			return fail(400, { errors: [await read_error_message(resp)] });
		}

		return { success: true };
	},

	remove_image: async (event: RequestEvent) => {
		await event.locals.authorize('Admin');

		const formData = await event.request.formData();
		const imageId = formData.get('image_id');
		if (typeof imageId !== 'string' || imageId.length === 0) {
			return fail(400, { errors: ['image_id must be present'] });
		}

		const client = build_client(event);
		const { error: apiError, response } = await client.deleteApiAuctionsByIdImagesByImageId({
			path: { id: event.params.id, imageId }
		});

		if (apiError) {
			return fail(400, { errors: [await read_error_message(response)] });
		}

		return { success: true };
	},

	close: async (event: RequestEvent) => {
		await event.locals.authorize('Admin');

		const client = build_client(event);
		const { error: apiError, response } = await client.postApiAuctionsByIdClose({
			path: { id: event.params.id }
		});

		if (apiError) {
			return fail(400, { errors: [await read_error_message(response)] });
		}

		return { success: true };
	}
} satisfies Actions;
