import type { AppConfig, OAuthProvider } from "./types/oauth_providers";



class ApiClassBase {
    fetch: (input: RequestInfo | URL, init?: RequestInit | undefined) => Promise<Response>;
    constructor(customFetch?: Window["fetch"]) {
        if (!customFetch)
            customFetch = fetch;
        this.fetch = customFetch;
    }

    async makeRequest<T>(url: string, requestInit?: {
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
        const res = await (this.fetch)(url, requestInit as any);
        if (!res.ok)
            throw new Error("Request failed!");

        if (res.status === 204)
            return undefined as any;
        else
            return res.json();
    }
}

export class AdminApi extends ApiClassBase {
    loadConfig(): Promise<AppConfig> {
        return this.makeRequest("/api/admin/config");
    }
    saveConfig(config: AppConfig): Promise<void> {
        return this.makeRequest("/api/admin/config", {
            method: "PATCH",
            body: config,
        });
    }
    loadOauthProviders(): Promise<OAuthProvider[]> {
        return this.makeRequest("/api/admin/oauth");
    }
    createProvider(provider: OAuthProvider): Promise<void> {
        return this.makeRequest("/api/admin/oauth", {
            method: "POST",
            body: provider
        });
    }
    deleteProvider(id: string): Promise<void> {
        return this.makeRequest(`/api/admin/oauth/${id}`, {
            method: "DELETE",
        });
    }
}

export class OAuthApi extends ApiClassBase {
    listProviders(): Promise<{ provider_name: string, flow_url: string }[]> {
        return this.makeRequest("/api/oauth/providers");
    }
}

export class UserApi extends ApiClassBase {
    get_me(): Promise<User> {
        return this.makeRequest('/api/users/me');
    }
    list_users(): Promise<User[]> {
        return this.makeRequest('/api/users');
    }
    updateUser(user: User): Promise<void> {
        return this.makeRequest('/api/users', {
            method: 'PATCH',
            body: user,
        });
    }
}