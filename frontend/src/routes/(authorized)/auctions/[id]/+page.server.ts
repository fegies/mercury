import { pg } from "$lib/server/db";
import { error } from "@sveltejs/kit";
import type { PageServerLoad } from "./$types";

export const load: PageServerLoad = async ({ locals, params }) => {
    await locals.authorize('User');

    const auction_id = params.id;

    const sql = pg();

    const auction_prom = sql<{
        item_name: string,
        description: string,
        current_value: number,
    }[]>`
    select i.item_name, i.description, (
        select coalesce(max(current_value), a.minimum_bid) from bids b
        where b.auction_id = a.id
    ) as current_value
    from auctions a
    inner join auction_item i
    on i.id = a.item_id
    where a.id = ${auction_id}`;

    const image_links = await sql<{ link: string }[]>`
        select '/auction_images/' || img.id as link
        from auction_images img
        inner join auction_item i
            on img.auction_item_id = i.id
        inner join auctions a
            on a.item_id = i.id
        where a.id = ${auction_id}
    `.then(r => r.map(r => r.link));

    const auction = await auction_prom;

    if (auction.count == 0)
        error(404, "No such auction found");

    return {
        auction: auction[0],
        image_links
    }
};