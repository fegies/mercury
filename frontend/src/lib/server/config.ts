import { env } from "$env/dynamic/private";


export class OAuthProvider {
    name: string = "";
    auth_url: string = "";
    token_url: string = "";
    redirect_url: string = "";
    client_id: string = "";
    client_secret: string = "";
}

export class AppConfig {
    deployment_domain: string = "";
}

function get_val(name: string): string {
    const val = env[name];
    if (!val)
        throw new Error(`env variable ${name} must be set`);
    return val;
}

function loadConfig(): AppConfig {
    return {
        deployment_domain: get_val('DEPLOYMENT_DOMAIN')
    }
}

export const CONFIG = loadConfig();