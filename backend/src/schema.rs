// @generated automatically by Diesel CLI.

diesel::table! {
    app_config (id) {
        id -> Int4,
        deployment_domain -> Text,
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

diesel::allow_tables_to_appear_in_same_query!(
    app_config,
    oauth_providers,
);
