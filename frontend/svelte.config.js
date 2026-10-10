import adapter from '@sveltejs/adapter-node';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

const config = {
	preprocess: vitePreprocess(),
	kit: {
		adapter: adapter(),
		// Nonce-based script/style policies. SvelteKit's production bootstrap
		// is an inline script, so a static `script-src 'self'` at a TLS
		// terminator would block it, and `unsafe-inline` would gut XSS
		// protection. The nonce travels with each SSR response instead.
		csp: {
			mode: 'nonce',
			directives: {
				'script-src': ['self'],
				'style-src': ['self']
			}
		}
	}
};

export default config;
