import type { AuctionDetail } from '$lib/types/auction.js';
import type { Actions } from './$types';

export const actions = {
	default: async ({ request, fetch, locals }) => {
		await locals.authorize('Admin');

		const formData = await request.formData();

		const errors: string[] = [];

		let auction: AuctionDetail = {
			id: '',
			title: form_string('name'),
			description: form_string('description'),
			minimumPrice: form_num('min-price'),
			closureTime: form_date('auction-end'),
			isClosed: false,
			imageUrls: [],
			currentBid: null,
		};

		if (errors.length == 0) {
			try {
				const resp = await fetch('/api/auctions', {
					method: 'POST',
					headers: { 'Content-Type': 'application/json' },
					body: JSON.stringify({
						title: auction.title,
						description: auction.description,
						minimumPrice: auction.minimumPrice,
						closureTime: auction.closureTime,
					}),
				});

				if (!resp.ok) {
					const body = await resp.json().catch(() => null);
					errors.push(body?.message ?? `Failed to create auction (${resp.status})`);
				} else {
					const { id } = await resp.json();
					auction.id = id;

					const images = formData.getAll('images').filter((f): f is File => f instanceof File && f.size > 0);

					if (images.length > 0) {
						const imgForm = new FormData();
						for (const img of images) {
							imgForm.append('images', img);
						}

						const imgResp = await fetch(`/api/auctions/${id}/images`, {
							method: 'POST',
							body: imgForm,
						});

						if (imgResp.ok) {
							const { imageIds } = await imgResp.json();
							auction.imageUrls = imageIds.map((iid: string) => `/api/auctions/${id}/images/${iid}`);
						}
					}
				}
			} catch (e) {
				errors.push((e as Error).message);
			}
		}

		return {
			auction,
			errors,
		};

		function form_string(name: string): string {
			const val = formData.get(name);
			if (typeof val === 'string')
				return val;
			errors.push(`${name} must be present and a string`);
			return '';
		}
		function form_num(name: string): number {
			const val = parseFloat(formData.get(name)?.toString() || '');
			if (isNaN(val)) {
				errors.push(`${name} must be present and a number`);
				return 0;
			}
			return val;
		}
		function form_date(name: string): string {
			const val = Date.parse(formData.get(name)?.toString() || '');
			if (isNaN(val)) {
				errors.push(`${name} must be present and a date`);
				return new Date().toISOString();
			}

			if (val <= Date.now()) {
				errors.push(`${name} must not be in the past`);
			}

			return new Date(val).toISOString();
		}
	}
} satisfies Actions;
