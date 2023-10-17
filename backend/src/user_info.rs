use diesel::{QueryDsl, RunQueryDsl};
use rocket::{figment::Provider, http::ContentType, serde::json::Json, Route};
use uuid::Uuid;

use crate::{oauth::users::RegisteredUser, Db};

pub fn routes() -> Vec<Route> {
    routes![me, picture]
}

#[get("/me")]
fn me(user: RegisteredUser) -> Json<RegisteredUser> {
    Json(user)
}

#[get("/picture/<user_id>")]
async fn picture(_user: RegisteredUser, user_id: &str, db: Db) -> Option<(ContentType, Vec<u8>)> {
    let user = Uuid::parse_str(&user_id).ok()?;
    let picture = db
        .run(move |db| {
            use crate::schema::profile_pics::dsl::*;
            use diesel::prelude::*;

            profile_pics
                .filter(user_id.eq(&user))
                .select(picture)
                .first(db)
                .ok()
        })
        .await?;

    Some((ContentType::Binary, picture))
}
