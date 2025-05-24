import { error, redirect } from "@sveltejs/kit";
import type { RequestHandler } from "./$types";
import { OAuthFlows } from "$lib/server/oauth";

export const GET: RequestHandler = async (event) => {
    const { params: { provider } } = event;

    await OAuthFlows.get_provider(provider)?.finish_flow(event);

    error(400, 'Bad provider');
}