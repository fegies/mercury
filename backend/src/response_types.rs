use rocket::response::Responder;
use rocket::{http::Status, serde::json::Json, Request};

pub type JsonResult<T> = Result<Json<T>, QueryFailure>;
pub type DbResult<T> = Result<T, QueryFailure>;

#[derive(Debug)]
pub struct QueryFailure {}

impl<'r> Responder<'r, 'static> for QueryFailure {
    fn respond_to(self, request: &'r Request<'_>) -> rocket::response::Result<'static> {
        Status::InternalServerError.respond_to(request)
    }
}

impl From<diesel::result::Error> for QueryFailure {
    fn from(value: diesel::result::Error) -> Self {
        tracing::error!("Query error: {value:?}");
        Self {}
    }
}
