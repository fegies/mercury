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
        return makeRequest("/api/admin/oauth", {
            method: "DELETE",
            body: id
        });
    }
}

export const AdminApi = new AdminApiClass();