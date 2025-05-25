import { SessionStore, type AuthHandler, type AuthResult, type RequestedAuthRole } from "$lib/server/auth";
import { redirect, type Cookies, type Handle, type HandleServerError } from "@sveltejs/kit";



export const handle: Handle = async ({ event, resolve }) => {
    event.locals.authorize = build_authorize(event.cookies);


    return await resolve(event);
}

function build_authorize(cookies: Cookies): AuthHandler {
    let user_prom: Promise<User> | null = null;

    async function fetch_user(): Promise<User> {
        const session_key = cookies.get('SESSION');
        if (session_key) {
            const user = SessionStore.try_get_user(session_key);
            if (user)
                return user;
        }

        redirect(302, '/oauth');
    }

    return async (requested_role) => {
        user_prom ??= fetch_user();
        const user = await user_prom;

        return {
            user
        }
    };
}
