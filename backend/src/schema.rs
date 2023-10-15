// @generated automatically by Diesel CLI.

diesel::table! {
    app_config (id) {
        id -> Int4,
        deployment_domain -> Text,
    }
}

diesel::table! {
    external_users (issuer, issuer_sub) {
        internal_user -> Uuid,
        issuer -> Text,
        issuer_sub -> Text,
    }
}

diesel::table! {
    oauth_providers (name) {
        name -> Text,
        auth_url -> Text,
        token_url -> Text,
        client_id -> Text,
        client_secret -> Text,
    }
}

diesel::table! {
    profile_pics (user_id) {
        user_id -> Uuid,
        picture -> Bytea,
    }
}

diesel::table! {
    users (user_id) {
        user_id -> Uuid,
        display_name -> Text,
        preferred_username -> Text,
    }
}

diesel::joinable!(external_users -> users (internal_user));
diesel::joinable!(profile_pics -> users (user_id));

diesel::allow_tables_to_appear_in_same_query!(
    app_config,
    external_users,
    oauth_providers,
    profile_pics,
    users,
);
