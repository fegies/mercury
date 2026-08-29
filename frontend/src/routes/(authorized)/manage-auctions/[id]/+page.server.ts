import { error, fail } from '@sveltejs/kit';
import { build_client } from '$lib/api';
import {
	parse_auction_form,
	read_error_message,
	selected_files,
	to_auction
} from '$lib/auction_form';
import type { Actions, PageServerLoad, RequestEvent } from './$types';

export const load: PageServerLoad = async (event) => {
	await event.locals.authorize('Admin');

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
	update: async (event: RequestEvent) => {
		await event.locals.authorize('Admin');

		const formData = await event.request.formData();
		const { values, errors: parseErrors } = parse_auction_form(formData);
		const errors: string[] = [...parseErrors];

		if (values && errors.length === 0) {
			const client = build_client(event);
			const { error: apiError, response } = await client.patchApiAuctionsById({
				path: { id: event.params.id },
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

		const client = build_client(event);
		const { error: apiError, response } = await client.postApiAuctionsByIdImages({
			path: { id: event.params.id },
			body: { Files: files }
		});

		if (apiError) {
			return fail(400, { errors: [await read_error_message(response)] });
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
