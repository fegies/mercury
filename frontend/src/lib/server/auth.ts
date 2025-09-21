import { pg } from "./db";
import { random_string } from "./util";

export type RequestedAuthRole =
    'User' | 'Admin';

export type AuthResult = {
    user: User;
};

export type AuthHandler = (requested_role: RequestedAuthRole) => Promise<AuthResult>;

const sessions = new Map<string, User>();
export const SessionStore = {
    try_get_user(session_id: string): User | undefined {
        return sessions.get(session_id);
    },

    logout(session_id: string) {
        sessions.delete(session_id);
    },

    async exchange_oauth_user(issuer: string
        , issuer_sub: string
        , issuer_username: string
        , issuer_display_name: string
        , session_expiration: Date): Promise<{ user: User, session_id: string }> {

        const session_id = await random_string();

        const user = await pg().begin(async sql => {
            let r = await sql`select internal_user from external_users
            where issuer = ${issuer} and issuer_sub = ${issuer_sub}`;
            if (r.count == 0) {
                const [{ user_id }] = await sql`insert into users (display_name, preferred_username)
                values (${issuer_display_name}, ${issuer_username})
                returning user_id`;

                r = await sql`insert into external_users (internal_user, issuer, issuer_sub)
                values (${user_id}, ${issuer}, ${issuer_sub})
                returning internal_user`;
            }
            else {
                const [{ internal_user }] = r;

                await sql`update users
                set display_name = ${issuer_display_name}
                , preferred_username = ${issuer_username}
                where user_id = ${internal_user}
                and (display_name <> ${issuer_display_name} or preferred_username <> ${issuer_username})`;

            }
            const [{ internal_user }] = r;

            const [{ can_start_auctions }] = await sql`select can_start_auctions from users
            where user_id = ${internal_user}`;

            return {
                id: internal_user,
                can_start_auctions,
                preferred_username: issuer_username,
                name: issuer_display_name,
                roles: []
            };
        });

        sessions.set(session_id, user);
        setTimeout(() => sessions.delete(session_id), session_expiration.getTime() - new Date().getTime());

        return {
            user,
            session_id
        }
    }
}
