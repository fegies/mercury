import tailwindcss from '@tailwindcss/vite';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vitest/config';

export default defineConfig({
	plugins: [tailwindcss(), sveltekit()],
	server: {
		proxy: {
			'/api': 'http://localhost:5023'
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
