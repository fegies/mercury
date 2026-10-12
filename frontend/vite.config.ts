import adapter from '@sveltejs/adapter-node';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';
import tailwindcss from '@tailwindcss/vite';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vitest/config';

export default defineConfig({
	plugins: [
		tailwindcss(),
		sveltekit({
			preprocess: vitePreprocess(),
			adapter: adapter(),
			// Nonce-based script/style policies. SvelteKit's production bootstrap
			// is an inline script, so a static `script-src 'self'` at a TLS
			// terminator would block it, and `unsafe-inline` would gut XSS
			// protection. The nonce travels with each SSR response instead.
			csp: {
				mode: 'nonce',
				directives: { 'script-src': ['self'], 'style-src': ['self'] }
			}
		})
	],
	server: {
		proxy: {
			'/api': { target: 'http://localhost:5023', xfwd: true }
			// "/oauth/flows": "http://localhost:8000"
		}
	},
	test: {
		include: ['src/**/*.{test,spec}.ts', 'tests/**/*.{test,spec}.ts'],
		environment: 'happy-dom',
		coverage: {
			provider: 'v8',
			include: ['src/lib/**', 'src/routes/**'],
			exclude: ['src/lib/client/**']
		}
	}
});
