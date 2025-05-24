import { error, redirect, type Cookies, type RequestEvent } from "@sveltejs/kit";
import type { OAuthProvider } from ".";
import { random_string } from "../util";
import { SessionStore } from "../auth";
import { pg } from "../db";

const MS_SCOPES = 'openid profile User.Read';

const DEPLOYMENT_DOMAIN = 'http://localhost:5173';

export class MicrosoftProvider implements OAuthProvider {
    private name: string;
    private client_id: string;
    private client_secret: string;
    private tenant: string;
    constructor(name: string, client_id: string, client_secret: string, tenant: string) {
        this.name = name;
        this.client_id = client_id;
        this.client_secret = client_secret;
        this.tenant = tenant;
    }

    private get_redir_url(): string {
        return `${DEPLOYMENT_DOMAIN}/oauth/flows/${this.name}/callback`;
    }

    get_name(): string {
        return this.name;
    }

    start_flow(cookies: Cookies): Promise<never> {
        return this.start_flow_inner(cookies, false);
    }

    async start_flow_inner(cookies: Cookies, ignore_saved_provider: boolean): Promise<never> {
        const state = await random_string();
        const expiresAt = new Date().getTime() + 1000 * 5 * 60;
        cookies.set('OAUTH_STATE', state, {
            sameSite: 'lax',
            path: '',
            expires: new Date(expiresAt),
            maxAge: 60 * 5
        });

        const params = new URLSearchParams();
        params.set('client_id', this.client_id);
        params.set('redirect_uri', this.get_redir_url());
        params.set('scope', MS_SCOPES);
        params.set('state', state);
        params.set('response_type', 'code');

        if (!ignore_saved_provider && cookies.get('SAVED_OAUTH_PROVIDER') === this.name)
            params.set('prompt', 'none');
        const trigger_url = `https://login.microsoftonline.com/${this.tenant}/oauth2/v2.0/authorize?${params}`;
        return redirect(302, trigger_url);
    }

    async finish_flow(event: RequestEvent): Promise<never> {
        const query_params = new URL(event.url).searchParams;
        const state = query_params.get('state');
        if (!state || state !== event.cookies.get('OAUTH_STATE'))
            throw error(400, 'bad state');

        switch (query_params.get('error')) {
            case 'login_required':
            case 'interaction_required':
                await this.start_flow_inner(event.cookies, true);
                break;
        }

        event.cookies.delete('OAUTH_STATE', { path: '' });

        const body = new FormData();
        body.set('client_id', this.client_id);
        body.set('client_secret', this.client_secret);
        body.set('code', query_params.get('code') || '');
        body.set('redirect_uri', this.get_redir_url());
        body.set('grant_type', 'authorization_code');
        body.set('scope', MS_SCOPES);

        const token_response = await fetch(`https://login.microsoftonline.com/${this.tenant}/oauth2/v2.0/token`, {
            method: 'POST',
            body,
        });
        if (!token_response.ok) {
            throw error(500, await token_response.text())
        }


        const token_body: {
            access_token: string;
            token_type: string;
            expires_in: number;
            scope: string;
            id_token: string;
        } = await token_response.json();

        const expiration_time = new Date(new Date().getTime() + 12 * 60 * 60 * 1000);
        const parsed_token = parse_id_token(token_body.id_token);
        const { user, session_id } = await SessionStore.exchange_oauth_user(parsed_token.iss, parsed_token.sub, parsed_token.preferred_username, parsed_token.name, expiration_time);

        event.cookies.set('SESSION', session_id, {
            path: '/',
            sameSite: 'lax',
            expires: new Date(expiration_time.getTime() - 60 * 1000),
            maxAge: (expiration_time.getTime() - new Date().getTime() - 60) / 1000,
        });

        refresh_profile_pic(user.id, token_body.access_token).catch(() => { });

        redirect(302, '/');
    }
}

async function refresh_profile_pic(user_id: string, auth_token: string): Promise<void> {
    const result = await fetch('https://graph.microsoft.com/v1.0/me/photo/$value', {
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


function parse_id_token(token: string): {
    iss: string;
    sub: string;
    name: string;
    exp: number;
    preferred_username: string;
} {
    const id_part = token.split('.')[1];
    const buf = Buffer.from(id_part, 'base64');
    return JSON.parse(buf.toString('utf-8'))
}