// @vitest-environment node
import { afterEach, describe, expect, it } from 'vitest';
import type { RequestEvent } from '@sveltejs/kit';
import { build_client } from './api';

interface CapturedCall {
	input: RequestInfo | URL;
	init?: RequestInit;
}

function make_event(options?: { cookie?: string }): { event: RequestEvent; calls: CapturedCall[] } {
	const calls: CapturedCall[] = [];
	const url = 'http://mercury.example.org/auctions';
	const request = new Request(url);
	if (options?.cookie) request.headers.set('cookie', options.cookie);
	const event = {
		url: new URL(url),
		request,
		fetch: ((input: RequestInfo | URL, init?: RequestInit) => {
			calls.push({ input, init });
			return Promise.resolve(
				new Response(null, { status: 302, headers: { location: '/api/login' } })
			);
		}) as typeof fetch
	} as unknown as RequestEvent;
	return { event, calls };
}

function request_of(call: CapturedCall): Request {
	expect(call.input).toBeInstanceOf(Request);
	return call.input as Request;
}

describe('build_client', () => {
	afterEach(() => {
		delete process.env.MERCURY_BACKEND_URL;
	});

	it('sends SSR calls to MERCURY_BACKEND_URL when configured', async () => {
		process.env.MERCURY_BACKEND_URL = 'http://127.0.0.1:5023/';
		const { event, calls } = make_event({ cookie: 'myauth=cookie-value' });
		await build_client(event).getApiUserinfoMe();

		expect(calls).toHaveLength(1);
		expect(new URL(request_of(calls[0]).url).origin).toBe('http://127.0.0.1:5023');
	});

	it('falls back to the public request origin when unset', async () => {
		const { event, calls } = make_event({ cookie: 'myauth=cookie-value' });
		await build_client(event).getApiUserinfoMe();

		expect(new URL(request_of(calls[0]).url).origin).toBe('http://mercury.example.org');
	});

	it('attaches the browser cookie for direct backend targets', async () => {
		process.env.MERCURY_BACKEND_URL = 'http://127.0.0.1:5023';
		const { event, calls } = make_event({ cookie: 'myauth=cookie-value' });
		await build_client(event).getApiUserinfoMe();

		expect(request_of(calls[0]).headers.get('cookie')).toBe('myauth=cookie-value');
	});

	it('leaves same-origin cookie forwarding to SvelteKit', async () => {
		const { event, calls } = make_event({ cookie: 'myauth=cookie-value' });
		await build_client(event).getApiUserinfoMe();

		expect(request_of(calls[0]).headers.get('cookie')).toBeNull();
	});

	it('preserves caller headers', async () => {
		const { event, calls } = make_event({ cookie: 'myauth=cookie-value' });
		await build_client(event).getApiUserinfoMe({ headers: { 'x-trace': '1' } });

		expect(request_of(calls[0]).headers.get('x-trace')).toBe('1');
	});

	it('requests manual redirect handling', async () => {
		const { event, calls } = make_event({ cookie: 'myauth=cookie-value' });
		await build_client(event).getApiUserinfoMe();

		expect(request_of(calls[0]).redirect).toBe('manual');
	});

	it('surfaces an OIDC challenge as signed out', async () => {
		const { event } = make_event({ cookie: 'myauth=cookie-value' });
		const { data, error } = await build_client(event).getApiUserinfoMe();

		expect(data).toBeUndefined();
		expect(error).toBeDefined();
	});
});
