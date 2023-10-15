import type { AppConfig, OAuthProvider } from "./types/oauth_providers";

async function makeRequest<T>(url: string, requestInit?: {
    method?: string,
    body?: object | string
    headers?: object
}): Promise<T> {
    if (!requestInit)
        requestInit = {};

    if (requestInit.body && typeof requestInit.body == "object") {
        requestInit.body = JSON.stringify(requestInit.body);
        if (!requestInit.headers)
            requestInit.headers = {};

        (requestInit.headers as any)['Content-Type'] = 'application/json';
    }
    const res = await fetch(url, requestInit as any);
    if (!res.ok)
        throw new Error("Request failed!");
    if (res.status != 204)
        return res.json();
}

class AdminApiClass {
    loadConfig(): Promise<AppConfig> {
        return makeRequest("/api/admin/config");
    }
    saveConfig(config: AppConfig): Promise<void> {
        return makeRequest("/api/admin/config", {
            method: "PATCH",
            body: config,
        });
    }
    loadOauthProviders(): Promise<OAuthProvider[]> {
        return makeRequest("/api/admin/oauth");
    }
    createProvider(provider: OAuthProvider): Promise<void> {
        return makeRequest("/api/admin/oauth", {
            method: "POST",
            body: provider
        });
    }
    deleteProvider(id: string): Promise<void> {
        return makeRequest(`/api/admin/oauth/${id}`, {
            method: "DELETE",
        });
    }
}

class OauthApiClass {
    listProviders(): Promise<{ provider_name: string, flow_url: string }[]> {
        return makeRequest("/api/oauth/providers");
    }
}

export const AdminApi = new AdminApiClass();
export const OAuthApi = new OauthApiClass();