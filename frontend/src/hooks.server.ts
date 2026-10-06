import { build_client } from '$lib/api';
import type { Me } from '$lib/client/types.gen';
import { redirect, type Handle, type RequestEvent } from '@sveltejs/kit';

export const handle: Handle = async ({ event, resolve }) => {
	event.locals.authorize = build_authorize(event);
	return await resolve(event);
};

function build_authorize(event: RequestEvent): (requested_role: string) => Promise<Me> {
	let user_prom: Promise<Me | undefined> | null = null;

	async function fetch_user(): Promise<Me | undefined> {
		const client = build_client(event);
		// API requests do not follow redirects, so an unauthenticated userinfo
		// request surfaces the login redirect (a non-JSON, non-2xx response) as
		// a thrown error instead of completing the OAuth flow. As a safety net,
		// only accept a well-formed Me document; anything else means "not signed
		// in" so the caller issues the login redirect rather than rendering an
		// authenticated page.
		const { data } = await client.getApiUserinfoMe();
		if (typeof data !== 'object' || data === null || !('userinfo' in data)) {
			return undefined;
		}
		return data;
	}

	return async (requested_role) => {
		user_prom ??= fetch_user();

		const user = await user_prom.catch(() => undefined);
		if (user != null && has_role(user, requested_role)) return user;

		const login_url = new URL('/api/login', event.url);
		// Path + query only: return_to must stay a local URL (the backend
		// rejects absolute/foreign redirect targets).
		login_url.searchParams.append('return_to', event.url.pathname + event.url.search);
		throw redirect(303, login_url);
	};
}

function has_role(user: Me, requested_role: string): boolean {
	if (requested_role !== 'Admin') return true;
	return user.canStartAuctions === true;
}
