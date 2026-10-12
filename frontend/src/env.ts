import { defineEnvVars } from '@sveltejs/kit/env';

export const variables = defineEnvVars({
	PUBLIC_PAGE_BRANDING: { public: true, schema: (input) => input ?? '' }
});
