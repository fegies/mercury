import type { LayoutLoad } from './$types';

import { PUBLIC_PAGE_BRANDING } from '$app/env/public';

export const load: LayoutLoad = async () => {
	return { branding: PUBLIC_PAGE_BRANDING || 'Mercury' };
};
