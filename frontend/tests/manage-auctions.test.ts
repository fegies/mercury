import { describe, expect, it, vi, beforeEach, type Mock } from 'vitest';

vi.mock('$lib/api', () => ({
	build_client: vi.fn()
}));

import { build_client } from '$lib/api';
import type { ActionFailure } from '@sveltejs/kit';
import { actions } from '../src/routes/(authorized)/manage-auctions/[id]/+page.server';

type Evt = Parameters<typeof actions.update>[0];

function event(
	overrides: {
		params?: Record<string, string>;
		form?: FormData;
		fetch?: Mock;
	} = {}
): Evt {
	return {
		locals: { authorize: vi.fn().mockResolvedValue({}) },
		params: overrides.params ?? { id: 'auction-1' },
		request: { formData: () => Promise.resolve(overrides.form ?? new FormData()) },
		fetch: overrides.fetch ?? vi.fn()
	} as unknown as Evt;
}

function failure(result: unknown): ActionFailure<{ errors: string[] }> {
	return result as ActionFailure<{ errors: string[] }>;
}

function make_client(overrides: Record<string, Mock>): Mock {
	const client = overrides;
	(build_client as Mock).mockReturnValue(client);
	return build_client as Mock;
}

describe('manage-auctions/[id] actions', () => {
	beforeEach(() => {
		vi.clearAllMocks();
	});

	describe('update', () => {
		it('patches the auction and extends the closure time when the end date changed', async () => {
			const form = new FormData();
			form.set('name', 'New Title');
			form.set('description', 'New Desc');
			form.set('min-price', '10');
			form.set('auction-end', '2030-01-01T10:00:00.000Z');
			form.set('published', 'on');

			const get = vi.fn().mockResolvedValue({
				data: { closureTime: '2029-01-01T10:00:00.000Z' },
				error: undefined
			});
			const extend = vi
				.fn()
				.mockResolvedValue({ error: undefined, response: new Response('', { status: 204 }) });
			const patch = vi
				.fn()
				.mockResolvedValue({ error: undefined, response: new Response('', { status: 204 }) });
			make_client({
				getApiAuctionsById: get,
				postApiAuctionsByIdExtendClose: extend,
				patchApiAuctionsById: patch
			});

			const result = await actions.update(event({ form }));

			expect(get).toHaveBeenCalledWith({ path: { id: 'auction-1' } });
			expect(extend).toHaveBeenCalledWith({
				path: { id: 'auction-1' },
				body: { closureTime: '2030-01-01T10:00:00.000Z' }
			});
			expect(patch).toHaveBeenCalledWith({
				path: { id: 'auction-1' },
				body: {
					title: 'New Title',
					description: 'New Desc',
					minimumPrice: 10,
					isPublished: true
				}
			});
			expect(result).toEqual({ success: true });
		});

		it('skips the extension when the end date is unchanged', async () => {
			const form = new FormData();
			form.set('name', 'New Title');
			form.set('description', 'New Desc');
			form.set('min-price', '10');
			form.set('auction-end', '2030-01-01T10:00:00.000Z');
			form.set('published', 'on');

			const get = vi.fn().mockResolvedValue({
				data: { closureTime: '2030-01-01T10:00:00.000Z' },
				error: undefined
			});
			const extend = vi.fn();
			const patch = vi
				.fn()
				.mockResolvedValue({ error: undefined, response: new Response('', { status: 204 }) });
			make_client({
				getApiAuctionsById: get,
				postApiAuctionsByIdExtendClose: extend,
				patchApiAuctionsById: patch
			});

			const result = await actions.update(event({ form }));

			expect(extend).not.toHaveBeenCalled();
			expect(patch).toHaveBeenCalled();
			expect(result).toEqual({ success: true });
		});

		it('returns field errors and does not call the API when the form is invalid', async () => {
			const form = new FormData();
			form.set('name', 'New Title');
			form.set('description', 'New Desc');
			form.set('min-price', 'not-a-number');
			form.set('auction-end', '2030-01-01T10:00:00.000Z');

			const patch = vi.fn();
			make_client({ patchApiAuctionsById: patch });

			const result = await actions.update(event({ form }));

			expect(patch).not.toHaveBeenCalled();
			const resultFail = failure(result);
			expect(resultFail.status).toBe(400);
			expect(resultFail.data.errors).toContain('min-price must be present and a number');
		});

		it('surfaces an extension error and does not patch', async () => {
			const form = new FormData();
			form.set('name', 'New Title');
			form.set('description', 'New Desc');
			form.set('min-price', '10');
			form.set('auction-end', '2030-01-01T10:00:00.000Z');
			form.set('published', 'on');

			const get = vi.fn().mockResolvedValue({
				data: { closureTime: '2029-01-01T10:00:00.000Z' },
				error: undefined
			});
			const extend = vi.fn().mockResolvedValue({
				error: { status: 400, detail: 'Closure time can only be extended.' },
				response: new Response('', { status: 400 })
			});
			const patch = vi.fn();
			make_client({
				getApiAuctionsById: get,
				postApiAuctionsByIdExtendClose: extend,
				patchApiAuctionsById: patch
			});

			const resultFail = failure(await actions.update(event({ form })));

			expect(resultFail.status).toBe(400);
			expect(resultFail.data.errors).toEqual(['Closure time can only be extended.']);
			expect(patch).not.toHaveBeenCalled();
		});

		it('surfaces an API error payload', async () => {
			const form = new FormData();
			form.set('name', 'New Title');
			form.set('description', 'New Desc');
			form.set('min-price', '10');
			form.set('auction-end', '2030-01-01T10:00:00.000Z');

			const get = vi.fn().mockResolvedValue({
				data: { closureTime: '2030-01-01T10:00:00.000Z' },
				error: undefined
			});
			const patch = vi.fn().mockResolvedValue({
				error: { status: 400, detail: 'Title must be non-empty.' },
				response: new Response(JSON.stringify({ message: 'Title must be non-empty.' }), {
					status: 400
				})
			});
			make_client({
				getApiAuctionsById: get,
				postApiAuctionsByIdExtendClose: vi.fn(),
				patchApiAuctionsById: patch
			});

			const resultFail = failure(await actions.update(event({ form })));

			expect(resultFail.status).toBe(400);
			expect(resultFail.data.errors).toEqual(['Title must be non-empty.']);
		});
	});

	describe('add_images', () => {
		it('uploads files and returns success', async () => {
			const form = new FormData();
			const file = new File(['data'], 'a.png', { type: 'image/png' });
			form.set('images', file);

			const upload = vi
				.fn()
				.mockResolvedValue({ error: undefined, response: new Response('', { status: 200 }) });
			make_client({ postApiAuctionsByIdImages: upload });

			const result = await actions.add_images(event({ form }));

			expect(upload).toHaveBeenCalledWith({
				path: { id: 'auction-1' },
				body: { Files: [file] }
			});
			expect(result).toEqual({ success: true });
		});

		it('rejects when no images are selected', async () => {
			const upload = vi.fn();
			make_client({ postApiAuctionsByIdImages: upload });

			expect(upload).not.toHaveBeenCalled();
			const resultFail = failure(await actions.add_images(event({ form: new FormData() })));
			expect(resultFail.status).toBe(400);
			expect(resultFail.data.errors).toEqual(['No images selected.']);
		});
	});

	describe('remove_image', () => {
		it('deletes the image and returns success', async () => {
			const form = new FormData();
			form.set('image_id', 'img-9');

			const remove = vi
				.fn()
				.mockResolvedValue({ error: undefined, response: new Response('', { status: 200 }) });
			make_client({ deleteApiAuctionsByIdImagesByImageId: remove });

			const result = await actions.remove_image(event({ form }));

			expect(remove).toHaveBeenCalledWith({ path: { id: 'auction-1', imageId: 'img-9' } });
			expect(result).toEqual({ success: true });
		});

		it('rejects a missing image_id', async () => {
			const remove = vi.fn();
			make_client({ deleteApiAuctionsByIdImagesByImageId: remove });

			expect(remove).not.toHaveBeenCalled();
			const resultFail = failure(await actions.remove_image(event({ form: new FormData() })));
			expect(resultFail.status).toBe(400);
			expect(resultFail.data.errors).toEqual(['image_id must be present']);
		});
	});

	describe('close', () => {
		it('closes the auction and returns success', async () => {
			const close = vi
				.fn()
				.mockResolvedValue({ error: undefined, response: new Response('', { status: 200 }) });
			make_client({ postApiAuctionsByIdClose: close });

			const result = await actions.close(event());

			expect(close).toHaveBeenCalledWith({ path: { id: 'auction-1' } });
			expect(result).toEqual({ success: true });
		});
	});
});
