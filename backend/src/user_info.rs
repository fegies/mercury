use rocket::{serde::json::Json, Route};

use crate::oauth::users::RegisteredUser;

pub fn routes() -> Vec<Route> {
    routes![me]
}

#[get("/me")]
pub fn me(user: RegisteredUser) -> Json<RegisteredUser> {
    Json(user)
}
