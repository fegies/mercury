import { pg } from "$lib/server/db";
import type { RequestHandler } from "./$types";


export const GET: RequestHandler = async ({ locals, params, request }) => {
    await locals.authorize('User');

    const user_id = params.user_id;
    const sql = pg();

    const rows = await sql`select picture, hash from profile_pics where user_id = ${user_id}`;
    if (rows.length == 0)
        return new Response('not found', {
            status: 404,
        });

    const {
        picture,
        hash
    } = rows[0];

    if (request.headers.get('If-None-Match') === hash)
        return new Response(null, {
            status: 304,
        })

    return new Response(picture, {
        headers: {
            'cache-control': 'private, max-age=3600',
            'etag': hash
        }
    });
}