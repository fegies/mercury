import type { RequestEvent } from '@sveltejs/kit';
import { createClient } from './client/client';
import { BackendClient } from './client/sdk.gen';

export function build_client(event: RequestEvent): BackendClient {
	const url = new URL(event.url);
	url.pathname = '';
	url.search = '';

	const client = createClient({
		fetch: (input, init) => event.fetch(input, { ...init, redirect: 'manual' }),
		baseUrl: url.toString()
	});
	return new BackendClient({
		client
	});
}
