import { UserApi } from "$lib/api";
import { redirect } from "@sveltejs/kit";


export const load = async (event) => {
    let res;
    try {
        const me = await new UserApi(event.fetch).get_me();
        res = {
            me
        };
    }
    catch {
        throw redirect(307, "/oauth");
    }
    return res;
}

