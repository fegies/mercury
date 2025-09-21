import { pg } from "../db";

export async function refresh_profile_pic(
    picture_url: string,
    user_id: string,
    auth_token: string,
): Promise<void> {
    const result = await fetch(picture_url, {
        headers: {
            Authorization: `Bearer ${auth_token}`,
        }
    });
    if (!result.ok)
        return;

    const body = await result.bytes();
    const hash = await crypto.subtle.digest('SHA-1', body);
    const hash_string = Buffer.from(hash).toString('base64url');

    const sql = pg();
    const [{ already_in }] = await sql`select exists(
        select 1 from profile_pics where user_id = ${user_id} and hash = ${hash_string}
    ) as already_in`;

    if (already_in) {
        return;
    }

    await sql`insert into profile_pics (user_id, hash, picture)
        values (${user_id}, ${hash_string}, ${body})
        on conflict(user_id) do update
        set hash = excluded.hash
        , picture = excluded.picture
    `;


}