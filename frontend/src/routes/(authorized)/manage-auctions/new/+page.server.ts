import { fail, redirect } from '@sveltejs/kit';
import {
	parse_auction_form,
	read_error_message,
	selected_files,
	to_auction
} from '$lib/auction_form';
import type { AuctionSummary } from '$lib/types/auction.js';
import type { Actions } from './$types';

export const actions = {
	default: async ({ request, fetch, locals }) => {
		await locals.authorize('Admin');

		const formData = await request.formData();
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
					imageUrls: [],
					currentBid: null
				};

		if (values) {
			const body = new FormData();
			body.set('title', values.title);
			body.set('description', values.description);
			body.set('minimumPrice', String(values.minimumPrice));
			body.set('closureTime', values.closureTime);
			for (const file of selected_files(formData)) {
				body.append('files', file);
			}

			const resp = await fetch('/api/auctions', { method: 'POST', body });
			if (!resp.ok) {
				errors.push(await read_error_message(resp));
			} else {
				const id = await resp.json();
				redirect(303, `/manage-auctions/${id}`);
			}
		}

		return fail(400, { auction, errors });
	}
} satisfies Actions;
