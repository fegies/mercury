import { describe, expect, it, vi, afterEach } from 'vitest';
import { parse_auction_form, selected_files, to_auction } from '../src/lib/auction_form';

function form(entries: Record<string, string | File | undefined>): FormData {
	const fd = new FormData();
	for (const [k, v] of Object.entries(entries)) {
		if (v !== undefined) fd.set(k, v);
	}
	return fd;
}

afterEach(() => {
	vi.useRealTimers();
});

describe('parse_auction_form', () => {
	it('parses a valid form into values', () => {
		const fd = form({
			name: 'My Auction',
			description: 'A nice item',
			'min-price': '12.5',
			'auction-end': '2030-01-01T10:00:00.000Z',
			published: 'on'
		});

		const { values, errors } = parse_auction_form(fd);

		expect(errors).toEqual([]);
		expect(values).toEqual({
			title: 'My Auction',
			description: 'A nice item',
			minimumPrice: 12.5,
			closureTime: '2030-01-01T10:00:00.000Z',
			isPublished: true
		});
	});

	it('treats an unchecked published checkbox as unpublished', () => {
		const fd = form({
			name: 'My Auction',
			description: 'A nice item',
			'min-price': '1',
			'auction-end': '2030-01-01T10:00:00.000Z'
		});

		const { values, errors } = parse_auction_form(fd);

		expect(errors).toEqual([]);
		expect(values?.isPublished).toBe(false);
	});

	it('rejects a missing name', () => {
		const fd = form({
			description: 'x',
			'min-price': '1',
			'auction-end': '2030-01-01T10:00:00.000Z'
		});

		const { values, errors } = parse_auction_form(fd);

		expect(values).toBeNull();
		expect(errors).toContain('name must be present and a string');
	});

	it('rejects a missing description', () => {
		const fd = form({
			name: 'x',
			'min-price': '1',
			'auction-end': '2030-01-01T10:00:00.000Z'
		});

		const { values, errors } = parse_auction_form(fd);

		expect(values).toBeNull();
		expect(errors).toContain('description must be present and a string');
	});

	it('rejects a non-numeric minimum price', () => {
		const fd = form({
			name: 'x',
			description: 'x',
			'min-price': 'abc',
			'auction-end': '2030-01-01T10:00:00.000Z'
		});

		const { values, errors } = parse_auction_form(fd);

		expect(values).toBeNull();
		expect(errors).toContain('min-price must be present and a number');
	});

	it('rejects a closure time in the past', () => {
		const fd = form({
			name: 'x',
			description: 'x',
			'min-price': '1',
			'auction-end': '2000-01-01T10:00:00.000Z'
		});

		const { values, errors } = parse_auction_form(fd);

		expect(values).toBeNull();
		expect(errors).toContain('auction-end must not be in the past');
	});

	it('rejects an unparseable closure time', () => {
		const fd = form({
			name: 'x',
			description: 'x',
			'min-price': '1',
			'auction-end': 'not-a-date'
		});

		const { values, errors } = parse_auction_form(fd);

		expect(values).toBeNull();
		expect(errors).toContain('auction-end must be present and a date');
	});
});

describe('selected_files', () => {
	it('keeps non-empty files and drops empty/non-file entries', () => {
		const fd = new FormData();
		const real = new File(['data'], 'a.png', { type: 'image/png' });
		fd.append('images', real);
		fd.append('images', new File([], 'empty.png'));
		fd.append('name', 'not a file');

		const files = selected_files(fd);

		expect(files).toEqual([real]);
	});
});

describe('to_auction', () => {
	it('builds an AuctionSummary with defaults and given values', () => {
		const auction = to_auction({
			title: 'T',
			description: 'D',
			minimumPrice: 5,
			closureTime: '2030-01-01T00:00:00.000Z',
			isPublished: true
		});

		expect(auction).toEqual({
			id: '',
			isClosed: false,
			isCancelled: false,
			isPublished: true,
			imageUrls: [],
			currentBid: null,
			title: 'T',
			description: 'D',
			minimumPrice: 5,
			closureTime: '2030-01-01T00:00:00.000Z'
		});
	});
});
