import { pg } from "$lib/server/db";
import type { RequestHandler } from "./$types";

export const GET: RequestHandler = async ({ request, params, locals }) => {
    await locals.authorize('User');

    const sql = pg();

    const etag = request.headers.get('If-None-Match');
    if (etag) {
        const r = await sql`select 1 from auction_images where id = ${params.image_id} and hash = ${etag}`;
        if (r.length > 0) {
            return new Response(null, {
                status: 304,
                headers: cacheHeaders()
            });
        }
    }

    const rows = await sql`select hash, content from auction_images where id = ${params.image_id}`;

    if (rows.length == 0) {
        return new Response('not found', {
            status: 404
        });
    }

    const {
        hash, content
    } = rows[0];

    return new Response(content, {
        status: 200,
        headers: {
            'etag': hash,
            ...cacheHeaders()
        }
    })
}

function cacheHeaders(): HeadersInit {
    return {
        'cache-control': 'private, max-age=108000'
    }
}