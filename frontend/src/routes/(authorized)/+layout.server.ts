import type { LayoutServerLoad } from "./$types";

export const load: LayoutServerLoad = async ({ locals }) => {
    const authres = await locals.authorize('User');
    return {
        me: authres
    }
};