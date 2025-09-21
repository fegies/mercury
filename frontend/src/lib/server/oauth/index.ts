import { error, redirect, type Cookies, type RequestEvent } from "@sveltejs/kit";
import { pg } from "../db";

import { MicrosoftProvider } from "./microsoft";
import { ZitadelProvider } from "./zitadel";

const active_providers: OAuthProvider[] = await (async () => {
    const sql = pg();
    const list = await sql<
        [{ name: string, auth_url: string, token_url: string, client_id: string, client_secret: string }]
    >`select name, auth_url, token_url, client_id, client_secret from oauth_providers`;

    const proms = list.map(db_provider => {
        const ms_regex = new RegExp('https://login.microsoftonline.com/([^/]+)/oauth2/v2.0/authorize');
        const ms_match = db_provider.auth_url.match(ms_regex);
        if (ms_match) {
            const tenant = ms_match[1];
            return new MicrosoftProvider(db_provider.name, db_provider.client_id, db_provider.client_secret, tenant);
        }
        else {
            return ZitadelProvider.init(db_provider.name, db_provider.client_id, db_provider.client_secret, 'https://sso.fegies.me/.well-known/openid-configuration');
        }
        throw new Error("Invalid provider found")
    });
    return Promise.all(proms);
})();




class OAuthFlowsClass {
    constructor() {

    }

    public list_providers(): OAuthProvider[] {
        return active_providers;
    }

    public get_provider(name: string): OAuthProvider | undefined {
        return active_providers.find(p => p.get_name() == name);
    }
}

export interface OAuthProvider {
    get_name(): string;
    start_flow(request: Cookies): Promise<never>;
    finish_flow(event: RequestEvent): Promise<never>;
}

export const OAuthFlows = new OAuthFlowsClass();