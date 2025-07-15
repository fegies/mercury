import { redirect } from "@sveltejs/kit";
import type { RequestHandler } from "./$types";


export const GET: RequestHandler = (event) => {
    redirect(302, "/auctions")
}