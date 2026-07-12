// See https://svelte.dev/docs/kit/types#app.d.ts

import type { Me } from "$lib/client/types.gen";
import type { AuthHandler } from "$lib/server/auth";

// for information about these interfaces
declare global {
	namespace App {
		// interface Error {}
		interface Locals {
			authorize: (requested_role: string) => Promise<Me>
		}
		// interface PageData {}
		// interface PageState {}
		// interface Platform {}
	}
}

export { };
