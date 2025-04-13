import { OAuthApi } from "$lib/api";
import { OAuthFlows } from "$lib/server/oauth";
import type { PageServerLoad } from "./$types";


export const load: PageServerLoad = async ({ }) => {

    return {
        providers: OAuthFlows.list_providers().map(provider => {
            const name = provider.get_name();
            return { name, auth_url: `/oauth/flows/${name}` };
        })
    }
};