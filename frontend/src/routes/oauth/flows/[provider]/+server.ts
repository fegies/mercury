import { error } from "@sveltejs/kit";
import type { RequestHandler } from "./$types";
import { OAuthFlows } from "$lib/server/oauth";

export const GET: RequestHandler = async ({ params: { provider }, cookies }) => {
    await OAuthFlows.get_provider(provider)?.start_flow(cookies);

    error(404, 'Bad provider');
}