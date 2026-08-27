import type { LayoutLoad } from './$types';

import { env } from '$env/dynamic/public';

export const load: LayoutLoad = async () => {
	return {
		branding: env.PUBLIC_PAGE_BRANDING || 'Mercury'
	};
};
