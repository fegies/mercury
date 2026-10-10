import type { RequestEvent } from '@sveltejs/kit';
import { createClient } from './client/client';
import { BackendClient } from './client/sdk.gen';

export function build_client(event: RequestEvent): BackendClient {
	const client = createClient({
		fetch: (input, init) => event.fetch(backend_request(event, input, init)),
		baseUrl: backend_base_url(event)
	});
	return new BackendClient({
		client
	});
}

export function build_browser_client(): BackendClient {
	const client = createClient({
		fetch: (input, init) => fetch(input, { ...init, credentials: 'same-origin' }),
		baseUrl: ''
	});
	return new BackendClient({
		client
	});
}

// The generated client hands fetch a single Request and no RequestInit, and
// event.fetch ignores a RequestInit when its first argument is a Request —
// so the request is rebuilt with manual redirect handling: a 302 from the
// backend (e.g. the anonymous OIDC challenge) must surface to callers as
// "signed out" instead of being followed. When the target is not the public
// origin (the backend URL is configured directly via MERCURY_BACKEND_URL,
// which keeps SSR calls off the public host), the browser's cookie is
// attached explicitly; same-origin targets (the fallback when unset) keep
// SvelteKit's own cookie forwarding. The client's X-Forwarded-For chain is
// preserved so the backend sees the real client address. The SSR guard
// keeps `process` out of the browser bundle.
function backend_base_url(event: RequestEvent): string {
	if (import.meta.env.SSR) {
		const configured = process.env.MERCURY_BACKEND_URL?.trim();
		if (configured) return configured.replace(/\/+$/, '');
	}

	const url = new URL(event.url);
	url.pathname = '';
	url.search = '';
	return url.toString();
}

function backend_request(
	event: RequestEvent,
	input: RequestInfo | URL,
	init?: RequestInit
): Request {
	const source = input instanceof Request ? input : new Request(input, init);
	const request = new Request(source, { redirect: 'manual' });

	if (new URL(request.url).origin !== event.url.origin) {
		const cookie = event.request.headers.get('cookie');
		if (cookie && !request.headers.has('cookie')) request.headers.set('cookie', cookie);
	}

	// The SSR connection is a trusted loopback proxy from the backend's point
	// of view, so it consumes this chain and learns the real client IP.
	const forwarded_for = event.request.headers.get('x-forwarded-for');
	if (forwarded_for && !request.headers.has('x-forwarded-for')) {
		request.headers.set('x-forwarded-for', forwarded_for);
	}

	return request;
}
