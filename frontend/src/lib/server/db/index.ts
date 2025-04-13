import { env } from "$env/dynamic/private";
import postgres from "postgres";

const PG_URL = (() => {
    if (env.PG_URL)
        return env.PG_URL;
    throw new Error('PG_URL env var must be set');
})()

// const PG_URL = env.PG_URL || throw new Error('PG_URL environment variable must be set.');
// if (!PG_URL)
export function pg() {
    let options: postgres.Options<{}> = {};
    if (env.PGSOCKET)
        options.host = env.PGSOCKET;
    return postgres(PG_URL, options);
} 