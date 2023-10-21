import { AdminApi, OAuthApi, UserApi } from '$lib/api.js'

export const load = async (event) => {
    const admin_api = new AdminApi(event.fetch);
    const user_api = new UserApi(event.fetch);

    const [config, users, oauth_providers] = await Promise.all([
        admin_api.loadConfig(),
        user_api.list_users(),
        admin_api.loadOauthProviders()
    ]);

    return {
        config, users, oauth_providers
    }
}