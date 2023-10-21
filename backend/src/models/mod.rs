use diesel::prelude::*;
use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Serialize, Deserialize, Queryable, Selectable, Insertable, AsChangeset)]
#[diesel(table_name = crate::schema::oauth_providers)]
pub struct OAuthProvider {
    pub name: String,
    pub auth_url: String,
    pub token_url: String,
    pub client_id: String,
    pub client_secret: String,
}

#[derive(Selectable, Queryable, AsChangeset)]
#[diesel(table_name = crate::schema::users)]
pub struct User {
    pub user_id: Uuid,
    pub display_name: String,
    pub preferred_username: String,
    pub can_start_auctions: bool,
}
