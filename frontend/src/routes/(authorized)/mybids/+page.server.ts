import { pg } from "$lib/server/db";
import type { PageServerLoad } from "./$types";

export const load: PageServerLoad = async ({ request, locals }) => {
    const { user } = await locals.authorize('User');

    const sql = pg();

    const bids = sql`select `

    return {
        user
    };
};