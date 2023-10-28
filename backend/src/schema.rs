// @generated automatically by Diesel CLI.

diesel::table! {
    app_config (id) {
        id -> Int4,
        deployment_domain -> Text,
    }
}

diesel::table! {
    auction_item (id) {
        id -> Uuid,
        item_name -> Text,
        description -> Nullable<Text>,
    }
}

diesel::table! {
    auctions (id) {
        id -> Uuid,
        item_id -> Uuid,
        end_time -> Timestamptz,
        multiplicity -> Int4,
        minimum_bid -> Numeric,
    }
}

diesel::table! {
    bids (id) {
        id -> Int4,
        auction_id -> Uuid,
        creator_id -> Nullable<Uuid>,
        modification_time -> Timestamptz,
        current_value -> Numeric,
        max_value -> Numeric,
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
        hash -> Uuid,
        mime_type -> Nullable<Text>,
        picture -> Bytea,
    }
}

diesel::table! {
    users (user_id) {
        user_id -> Uuid,
        display_name -> Text,
        preferred_username -> Text,
        can_start_auctions -> Bool,
    }
}

diesel::joinable!(auctions -> auction_item (item_id));
diesel::joinable!(bids -> auctions (auction_id));
diesel::joinable!(bids -> users (creator_id));
diesel::joinable!(external_users -> users (internal_user));
diesel::joinable!(profile_pics -> users (user_id));

diesel::allow_tables_to_appear_in_same_query!(
    app_config,
    auction_item,
    auctions,
    bids,
    external_users,
    oauth_providers,
    profile_pics,
    users,
);
