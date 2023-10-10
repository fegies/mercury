use warp::{reject::Reject, reply::Reply, Filter};

use crate::InitializationState;

mod admin;

#[derive(Debug)]
pub struct InternalServerError;
impl Reject for InternalServerError {}

impl Reply for InternalServerError {
    fn into_response(self) -> warp::reply::Response {
        warp::reply::with_status(
            "Internal Server err",
            warp::http::StatusCode::INTERNAL_SERVER_ERROR,
        )
        .into_response()
    }
}

impl<E> From<E> for InternalServerError
where
    E: std::error::Error,
{
    fn from(value: E) -> Self {
        tracing::error!("Internal server error: {value:?}");
        Self {}
    }
}

fn internal_error<T>(val: T) -> InternalServerError {
    InternalServerError {}
}

pub(crate) async fn run(state: InitializationState) {
    let admin_api = warp::path("admin").and(admin::initialize(&state));

    let subapis = admin_api;

    let api = warp::path("api").and(subapis).with(warp::trace::request());

    warp::serve(api).run(([127, 0, 0, 1], 8080)).await
}
