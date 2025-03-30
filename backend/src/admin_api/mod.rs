use cookie::Cookie;
use rocket::{
    http::{CookieJar, Status},
    request::{FromRequest, Outcome},
    serde::json::Json,
    Request, Route, State,
};
use serde::{Deserialize, Serialize};

use diesel::prelude::*;

use crate::{
    models::OAuthProvider,
    response_types::{DbResult, JsonResult},
    schema::{
        app_config::dsl::*,
        oauth_providers::{self, dsl::*},
    },
    AdminPassword,
};

use crate::{schema::app_config, Db};

pub fn routes() -> Vec<Route> {
    routes![
        get_config,
        login,
        set_config,
        list_oauth_providers,
        insert_or_update_provider,
        delete_provider
    ]
}

const ADMIN_TOKEN_NAME: &str = "MERCURY_ADMINTOKEN";

#[rocket::async_trait]
impl<'r> FromRequest<'r> for AdminUser {
    type Error = ();

    async fn from_request(req: &'r Request<'_>) -> Outcome<Self, Self::Error> {
        let cookies = req.cookies();
        let is_validated = cookies
            .get_private(ADMIN_TOKEN_NAME)
            .map(|cookie| cookie.value() == "MERCURY_IS_ADMIN")
            .unwrap_or(false);
        if is_validated {
            Outcome::Success(AdminUser {})
        } else {
            cookies.remove(Cookie::from("MERCURY_IS_ADMIN"));
            Outcome::Error((Status::Unauthorized, ()))
        }
    }
}

#[non_exhaustive]
pub struct AdminUser {}

#[post("/login", data = "<admin_pw>")]
fn login(
    configured_pw: &State<AdminPassword>,
    admin_pw: String,
    cookies: &CookieJar<'_>,
) -> Status {
    if &configured_pw.0 == &admin_pw {
        let mut cookie = Cookie::new(ADMIN_TOKEN_NAME, "MERCURY_IS_ADMIN");
        cookie.set_expires(None);
        cookies.add_private(cookie);
        cookie = Cookie::new("MERCURY_IS_ADMIN", "1");
        cookie.set_expires(None);
        cookies.add(cookie);
        Status::Ok
    } else {
        Status::Unauthorized
    }
}

#[derive(Serialize, Deserialize, Queryable, Selectable, Insertable, Default)]
#[diesel(table_name = crate::schema::app_config)]
pub struct AppConfig {
    deployment_domain: String,
}

#[get("/config")]
async fn get_config(_u: AdminUser, db: Db) -> JsonResult<AppConfig> {
    db.run(|pg| {
        let config = app_config
            .select(AppConfig::as_select())
            .first(pg)
            .optional()?
            .unwrap_or_default();

        Ok(Json(config))
    })
    .await
}

#[patch("/config", data = "<config>")]
async fn set_config(_u: AdminUser, db: Db, config: Json<AppConfig>) -> DbResult<Status> {
    db.run(move |db| {
        let r: Result<_, diesel::result::Error> = db.transaction(|db| {
            diesel::delete(app_config::table).execute(db)?;
            diesel::insert_into(app_config::table)
                .values(&*config)
                .execute(db)?;
            Ok(())
        });
        r
    })
    .await?;
    Ok(Status::NoContent)
}

#[get("/oauth")]
async fn list_oauth_providers(_u: AdminUser, db: Db) -> JsonResult<Vec<OAuthProvider>> {
    db.run(|db| {
        let data = oauth_providers
            .select(OAuthProvider::as_select())
            .load(db)?;
        Ok(Json(data))
    })
    .await
}

#[post("/oauth", data = "<provider>")]
async fn insert_or_update_provider(
    _u: AdminUser,
    db: Db,
    provider: Json<OAuthProvider>,
) -> DbResult<Status> {
    db.run(move |db| {
        diesel::insert_into(oauth_providers)
            .values(&*provider)
            .on_conflict(oauth_providers::name)
            .do_update()
            .set(&*provider)
            .execute(db)?;
        Ok(Status::NoContent)
    })
    .await
}

#[delete("/oauth/<provider>")]
async fn delete_provider(_u: AdminUser, db: Db, provider: String) -> DbResult<Status> {
    db.run(move |db| {
        let to_delete = oauth_providers.filter(name.eq(&provider));
        diesel::delete(to_delete).execute(db)?;
        Ok(Status::NoContent)
    })
    .await
}
