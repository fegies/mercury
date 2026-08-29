import type { AuctionSummary } from './types/auction.js';

export type AuctionFormValues = {
	title: string;
	description: string;
	minimumPrice: number;
	closureTime: string;
	isPublished: boolean;
};

export function parse_auction_form(formData: FormData): {
	values: AuctionFormValues | null;
	errors: string[];
} {
	const errors: string[] = [];

	const values = {
		title: form_string('name'),
		description: form_string('description'),
		minimumPrice: form_num('min-price'),
		closureTime: form_date('auction-end'),
		isPublished: form_bool('published')
	};

	return { values: errors.length == 0 ? values : null, errors };

	function form_string(name: string): string {
		const val = formData.get(name);
		if (typeof val === 'string') return val;
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

	function form_bool(name: string): boolean {
		return formData.get(name) !== null && formData.get(name) !== undefined;
	}
}

export function selected_files(formData: FormData, name = 'images'): File[] {
	return formData.getAll(name).filter((f): f is File => f instanceof File && f.size > 0);
}

export async function read_error_message(resp: Response | undefined): Promise<string> {
	if (!resp) return 'Request failed';
	const text = await resp.text();
	if (!text) return `Request failed (${resp.status})`;

	try {
		const body = JSON.parse(text);
		return body?.message ?? body?.title ?? text;
	} catch {
		return text;
	}
}

export function to_auction(values: AuctionFormValues): AuctionSummary {
	return {
		id: '',
		isClosed: false,
		imageUrls: [],
		currentBid: null,
		...values
	};
}
