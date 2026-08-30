import { describe, expect, it } from 'vitest';
import { parse_bid_form } from '../src/lib/bid_form';

function form(entries: Record<string, string | undefined>): FormData {
	const fd = new FormData();
	for (const [k, v] of Object.entries(entries)) {
		if (v !== undefined) fd.set(k, v);
	}
	return fd;
}

describe('parse_bid_form', () => {
	it('parses a valid maximum amount', () => {
		const { values, errors } = parse_bid_form(form({ maximum_amount: '12.5' }));

		expect(errors).toEqual([]);
		expect(values).toEqual({ maximumAmount: 12.5 });
	});

	it('rejects a missing maximum amount', () => {
		const { values, errors } = parse_bid_form(form({}));

		expect(values).toBeNull();
		expect(errors).toContain('maximum_amount must be present and a number');
	});

	it('rejects a non-numeric maximum amount', () => {
		const { values, errors } = parse_bid_form(form({ maximum_amount: 'abc' }));

		expect(values).toBeNull();
		expect(errors).toContain('maximum_amount must be present and a number');
	});
});
