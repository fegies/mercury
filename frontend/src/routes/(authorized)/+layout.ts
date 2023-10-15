import { UserApi } from "$lib/api";
import { redirect } from "@sveltejs/kit";


export const load = async (event) => {
    try {
        const me = await new UserApi(event.fetch).get_me();
        return {
            me
        };
    }
    catch {
        throw redirect(307, "/oauth");
    }
}

