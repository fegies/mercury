use diesel::prelude::*;
use rocket::{
    figment::Provider,
    http::{ContentType, Status},
    serde::json::Json,
    Route,
};
use uuid::Uuid;

use crate::{
    admin_api::AdminUser, models::User, oauth::users::RegisteredUser, response_types::DbResult, Db,
};

pub fn routes() -> Vec<Route> {
    routes![me, list_users, update_user]
}

#[get("/me")]
fn me(user: RegisteredUser) -> Json<RegisteredUser> {
    Json(user)
}

#[get("/")]
async fn list_users(a: AdminUser, db: Db) -> DbResult<Json<Vec<RegisteredUser>>> {
    use crate::schema::users::dsl::*;
    use diesel::prelude::*;
    db.run(|db| {
        let u: Vec<User> = users.load(db)?;
        let u = u.into_iter().map(|u| u.into()).collect();
        Ok(Json(u))
    })
    .await
}

#[patch("/", data = "<user>")]
async fn update_user(a: AdminUser, db: Db, user: Json<RegisteredUser>) -> DbResult<Status> {
    let user = user.0;
    db.run(move |db| {
        use crate::schema::users::dsl::*;
        diesel::update(users.filter(user_id.eq(user.get_id())))
            .set(can_start_auctions.eq(user.can_start_auctions))
            .execute(db)?;
        Ok(Status::NoContent)
    })
    .await
}
