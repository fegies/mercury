export type BidFormValues = {
	maximumAmount: number;
};

export function parse_bid_form(formData: FormData): {
	values: BidFormValues | null;
	errors: string[];
} {
	const errors: string[] = [];

	const val = parseFloat(formData.get('maximum_amount')?.toString() || '');
	if (isNaN(val)) {
		errors.push('maximum_amount must be present and a number');
		return { values: null, errors };
	}

	return { values: { maximumAmount: val }, errors };
}
