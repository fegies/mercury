use diesel::{connection, prelude::*};
use serde::{Deserialize, Serialize};

use crate::schema::app_config::dsl::*;
use crate::schema::oauth_providers::dsl::*;
use crate::{api::InternalServerError, schema::app_config, DbPool};

#[derive(Serialize, Deserialize, Queryable, Selectable, Insertable, Default)]
#[diesel(table_name = crate::schema::app_config)]
pub struct AppConfig {
    deployment_domain: String,
}

pub fn get_config(db: DbPool) -> Result<AppConfig, InternalServerError> {
    let mut connection = db.get()?;

    let res = app_config
        .select(AppConfig::as_select())
        .first(&mut connection)
        .optional()?;
    // app_config.select(AppConfig::as_select()).load(connection);

    Ok(res.unwrap_or_default())
}

pub fn put_config(db: DbPool, config: AppConfig) -> Result<(), InternalServerError> {
    let mut connection = db.get()?;
    let res = connection.transaction(|con| {
        diesel::delete(app_config::table).execute(con)?;
        diesel::insert_into(app_config::table)
            .values(&config)
            .execute(con)?;
        Ok::<_, diesel::result::Error>(())
    })?;

    Ok(res)
}

#[derive(Serialize, Deserialize, Queryable, Selectable, Insertable)]
#[diesel(table_name = crate::schema::oauth_providers)]
pub struct OAuthProvider {
    name: String,
    auth_url: String,
    token_url: String,
    client_id: String,
    client_secret: String,
}

pub fn get_providers(db: DbPool) -> Result<Vec<OAuthProvider>, InternalServerError> {
    let mut connection = db.get()?;

    let res = oauth_providers.load(&mut connection)?;
    Ok(res)
}

pub fn add_provider(db: DbPool, provider: OAuthProvider) -> Result<(), InternalServerError> {
    let mut con = db.get()?;

    diesel::insert_into(oauth_providers)
        .values(&provider)
        .execute(&mut con)?;

    Ok(())
}

pub fn delete_provider(db: DbPool, provider_id: String) -> Result<(), InternalServerError> {
    let mut con = db.get()?;

    diesel::delete(oauth_providers.filter(name.eq(&provider_id))).execute(&mut con)?;

    Ok(())
}
