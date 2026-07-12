import { build_client } from "$lib/api";
import type { Me } from "$lib/client/types.gen";
import { redirect, type Cookies, type Handle, type HandleServerError, type RequestEvent } from "@sveltejs/kit";




export const handle: Handle = async ({ event, resolve }) => {
    event.locals.authorize = build_authorize(event);
    return await resolve(event);
}

function build_authorize(event: RequestEvent): ((requested_role: string) => Promise<Me>) {
    let user_prom: Promise<Me | undefined> | null = null;


    async function fetch_user(): Promise<Me | undefined> {
        const client = build_client(event);
        const user = await client.getApiUserinfoMe();
        return user.data;
    }

    return async (requested_role) => {
        user_prom ??= fetch_user();

        try {
            const user = await user_prom;
            if (user != null)
                return user;
        }
        catch {
        }


        const login_url = new URL('/api/login', event.url);
        login_url.searchParams.append('return_to', event.url.toString());
        throw redirect(303, login_url);
    };
}
