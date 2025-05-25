import { pg } from "$lib/server/db";
import type { DetailedAuction } from "$lib/types/auction.js";
import type { Actions } from "./$types";

export const actions = {
    default: async ({ request, locals }) => {
        await locals.authorize('Admin');

        const formData = await request.formData();

        const errors: string[] = [];

        let auction: DetailedAuction = {
            name: form_string('name'),
            description: form_string('description'),
            min_price: form_num('min-price'),
            auction_end: form_date('auction-end'),
            image_urls: [],
            id: '',
        };

        if (errors.length == 0) {
            const images = formData.getAll('images');
            try {
                auction = await create_auction(auction, Promise.all(images.map(load_image)).then(a => a.filter(i => i != null)));
            }
            catch (e) {
                errors.push((e as Error).message);
            }
        }

        return {
            auction,
            errors,
        }

        function form_string(name: string): string {
            const val = formData.get(name);
            if (typeof val === 'string')
                return val;
            errors.push(`${name} must be present and a string`);
            return '';
        }
        function form_num(name: string): number {
            const val = parseInt(formData.get(name)?.toString() || '');
            if (isNaN(val)) {
                errors.push(`${name} must be present and a number`);
                return 0;
            }
            return val;
        }
        function form_date(name: string): Date {
            const val = Date.parse(formData.get(name)?.toString() || '');
            if (isNaN(val)) {
                errors.push(`${name} must be present and a date`);
                return new Date();
            }

            if (val <= new Date().getTime()) {
                errors.push(`${name} must not be in the past`);
            }

            return new Date(val);

        }
    }
} satisfies Actions;

async function create_auction(auction: DetailedAuction, image_prom: Promise<Image[]>): Promise<DetailedAuction> {
    await pg().begin(async sql => {
        const [{ id: item_id }] = await sql`insert into auction_item (item_name, description)
        values (${auction.name}, ${auction.description})
        returning id`;

        const [{ id: auction_id }] = await sql`insert into auctions (item_id, end_time, multiplicity, minimum_bid)
        values (${item_id}, ${auction.auction_end}, 1, ${auction.min_price})
        returning id`;

        auction.id = auction_id;

        const images = await image_prom;
        if (images.length > 0) {
            const values = images.map(i => ({
                auction_item_id: item_id,
                hash: i.hash,
                content: i.content
            }));
            const image_ids = await sql<{ id: number }[]>`insert into auction_images
            ${sql(values)}
            returning id
            `;

            auction.image_urls = image_ids.map(id => `/auction_images/${id.id}`);
        }
    });

    return auction;
}

type Image = {
    content: Uint8Array,
    hash: string
}

async function load_image(entry: FormDataEntryValue): Promise<Image | null> {
    if (entry instanceof File) {
        const content = await entry.bytes();
        if (content.length == 0)
            return null;

        const hash_buf = await crypto.subtle.digest('SHA-1', content);
        const hash = Buffer.from(hash_buf).toString('base64url');
        return {
            content,
            hash,
        };
    }
    return null;
}