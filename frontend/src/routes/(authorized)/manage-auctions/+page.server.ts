import { pg } from "$lib/server/db";
import type { PageServerLoad } from "./$types";

export const load: PageServerLoad = async ({ locals }) => {
    await locals.authorize('Admin');

    const sql = pg();
    const auctions = await sql`select `

    return {};
};