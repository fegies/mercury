import { SessionStore } from "$lib/server/auth";
import { redirect } from "@sveltejs/kit";
import type { RequestHandler } from "./$types";

export const GET: RequestHandler = async ({ cookies }) => {
    const session_id = cookies.get('SESSION');
    if (session_id)
        SessionStore.logout(session_id);

    cookies.delete('SESSION', {
        path: "/",
        httpOnly: true,
    });

    return redirect(302, '/');
};