import { pg } from "$lib/server/db";
import type { PageServerLoad } from "./$types";


export const load: PageServerLoad = async ({ locals, params }) => {
    await locals.authorize('User');

    const sql = pg();

    const auctions = await sql<{
        id: string,
        item_name: string,
        description: string,
        current_value: number,
        first_image: string | null,
    }[]>`
        select a.id, i.item_name, i.description
        , (
            select coalesce(max(current_value), a.minimum_bid) from bids b
            where b.auction_id = a.id
        ) as current_value
        , (
            select '/auction_images/' || img.id
            from auction_images img
            where img.auction_item_id = i.id
            limit 1
        ) as first_image
        from auctions a
        inner join auction_item i
        on i.id = a.item_id
        where a.end_time > now()
    `;

    return {
        auctions
    }
};