import { error, redirect } from "@sveltejs/kit";
import type { RequestHandler } from "./$types";
import { OAuthFlows } from "$lib/server/oauth";

export const GET: RequestHandler = async (event) => {
    const { params: { provider }, request, cookies } = event;
    const query_params = new URL(request.url).searchParams;
    if (query_params.get('error') === 'interaction_required') {
        cookies.delete('SAVED_OAUTH_PROVIDER', {
            path: ''
        });
        redirect(302, `/oauth/flows/${provider}`);
    }

    await OAuthFlows.get_provider(provider)?.finish_flow(event);

    error(400, 'Bad provider');
}