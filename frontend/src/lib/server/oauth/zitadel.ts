import { error, redirect, type Cookies, type RequestEvent } from "@sveltejs/kit";
import type { OAuthProvider } from ".";
import { CONFIG } from "../config";
import { random_string } from "../util";
import { SessionStore } from "../auth";
import { refresh_profile_pic } from "./util";

const ZITADEL_SCOPES = 'openid profile';

export class ZitadelProvider implements OAuthProvider {
    private name: string;
    private client_id: string;
    private client_secret: string;
    private auth_url: string;
    private token_url: string;
    private userinfo_url: string;

    private constructor(name: string,
        client_id: string,
        client_secret: string,
        auth_url: string,
        token_url: string,
        userinfo_url: string,
    ) {
        this.name = name;
        this.client_id = client_id;
        this.client_secret = client_secret;
        this.auth_url = auth_url;
        this.token_url = token_url;
        this.userinfo_url = userinfo_url;
    }

    static async init(name: string, client_id: string, client_secret: string, discovery_endpoint: string): Promise<ZitadelProvider> {
        const discovery_response = await fetch(discovery_endpoint).then(r => r.json());
        const token_endpoint = discovery_response['token_endpoint'];
        const auth_endpoint = discovery_response['authorization_endpoint'];
        const userinfo_endpoint = discovery_response['userinfo_endpoint'];

        return new ZitadelProvider(name, client_id, client_secret, auth_endpoint, token_endpoint, userinfo_endpoint);
    }

    get_name(): string {
        return this.name;
    }
    async start_flow(cookies: Cookies): Promise<never> {
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
        params.set('scope', ZITADEL_SCOPES);
        params.set('state', state);
        params.set('response_type', 'code');

        const trigger_url = `${this.auth_url}?${params}`;
        return redirect(302, trigger_url);
    }

    private get_redir_url(): string {
        return `${CONFIG.deployment_domain}/oauth/flows/${this.name}/callback`;
    }

    async finish_flow(event: RequestEvent): Promise<never> {
        const query_params = new URL(event.url).searchParams;
        const state = query_params.get('state');
        if (!state || state !== event.cookies.get('OAUTH_STATE'))
            throw error(400, 'bad state');

        event.cookies.delete('OAUTH_STATE', { path: '' });

        const body = new URLSearchParams();
        body.set('client_id', this.client_id);
        body.set('client_secret', this.client_secret);
        body.set('code', query_params.get('code') || '');
        body.set('redirect_uri', this.get_redir_url());
        body.set('grant_type', 'authorization_code');
        body.set('scope', ZITADEL_SCOPES);

        const token_response = await fetch(this.token_url, {
            method: 'POST',
            body
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

        const user_info: {
            preferred_username: string,
            name: string,
            picture: string,
        } = await fetch(this.userinfo_url, {
            headers: {
                'Authorization': 'Bearer ' + token_body.access_token,
            }
        }).then(r => r.json());

        const expiration_time = new Date(new Date().getTime() + 12 * 60 * 60 * 1000);
        const parsed_token = parse_id_token(token_body.id_token);

        const { user, session_id } = await SessionStore.exchange_oauth_user(
            parsed_token.iss, parsed_token.sub, user_info.preferred_username, user_info.name, expiration_time);

        refresh_profile_pic(user_info.picture, user.id, token_body.access_token).catch(() => { });

        event.cookies.set('SESSION', session_id, {
            path: '/',
            sameSite: 'lax',
            expires: new Date(expiration_time.getTime() - 60 * 1000),
            maxAge: (expiration_time.getTime() - new Date().getTime() - 60) / 1000,
        });

        return redirect(302, '/');
    }

}

function parse_id_token(token: string): {
    iss: string;
    sub: string;
    exp: number;
} {
    const id_part = token.split('.')[1];
    const buf = Buffer.from(id_part, 'base64');
    return JSON.parse(buf.toString('utf-8'))
}