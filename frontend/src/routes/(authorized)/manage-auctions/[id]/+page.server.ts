import { error, fail } from '@sveltejs/kit';
import { build_client } from '#lib/api.js';
import { parse_auction_form, selected_files, to_auction } from '#lib/auction_form.js';
import type { Actions, PageServerLoad, RequestEvent } from './$types';

function api_error_message(apiError: unknown): string | null {
	if (typeof apiError === 'string') return apiError;
	if (apiError && typeof apiError === 'object' && 'detail' in apiError) {
		const detail = (apiError as { detail?: unknown }).detail;
		if (typeof detail === 'string') return detail;
	}
	return null;
}

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
		const { values, errors: parseErrors } = parse_auction_form(formData, {
			allow_past_closure: true
		});
		const errors: string[] = [...parseErrors];

		if (values && errors.length === 0) {
			const client = build_client(event);
			const { data: current } = await client.getApiAuctionsById({
				path: { id: event.params.id }
			});

			const closureChanged =
				current !== undefined &&
				new Date(values.closureTime).getTime() !== new Date(current.closureTime).getTime();

			if (closureChanged) {
				const { error: extendError } = await client.postApiAuctionsByIdExtendClose({
					path: { id: event.params.id },
					body: { closureTime: values.closureTime }
				});

				const message = api_error_message(extendError);
				if (message) {
					errors.push(message);
				}
			}

			if (errors.length === 0) {
				const body = current?.isClosed
					? { isPublished: values.isPublished }
					: {
							title: values.title,
							description: values.description,
							minimumPrice: values.minimumPrice,
							isPublished: values.isPublished
						};

				const { error: apiError } = await client.patchApiAuctionsById({
					path: { id: event.params.id },
					body
				});

				const message = api_error_message(apiError);
				if (message) {
					errors.push(message);
				}
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
		const { error: apiError } = await client.postApiAuctionsByIdImages({
			path: { id: event.params.id },
			body: { Files: files }
		});

		if (apiError) {
			return fail(400, { errors: [apiError.detail ?? 'Image upload failed.'] });
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
		const { error: apiError } = await client.deleteApiAuctionsByIdImagesByImageId({
			path: { id: event.params.id, imageId }
		});

		if (apiError) {
			return fail(400, { errors: [apiError] });
		}

		return { success: true };
	},

	close: async (event: RequestEvent) => {
		await event.locals.authorize('Admin');

		const client = build_client(event);
		const { error: apiError } = await client.postApiAuctionsByIdClose({
			path: { id: event.params.id }
		});

		if (apiError) {
			return fail(400, { errors: [apiError] });
		}

		return { success: true };
	},

	cancel: async (event: RequestEvent) => {
		await event.locals.authorize('Admin');

		const client = build_client(event);
		const { error: apiError } = await client.postApiAuctionsByIdCancel({
			path: { id: event.params.id }
		});

		if (apiError) {
			return fail(400, { errors: [apiError] });
		}

		return { success: true };
	}
} satisfies Actions;
