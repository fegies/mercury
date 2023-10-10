mod domain_logic;

use std::{convert::Infallible, sync::Arc};

use cookie::{Cookie, CookieJar};
use serde::{Deserialize, Serialize};
use tracing::info;
use warp::{
    filters::BoxedFilter,
    http::HeaderValue,
    hyper::{body::Bytes, HeaderMap, StatusCode},
    reject::{self, Reject},
    reply::{Json, Response},
    Filter, Rejection, Reply,
};

use crate::{DbPool, InitializationState};

use self::domain_logic::{AppConfig, OAuthProvider};

use super::InternalServerError;

struct AdminAuthContext {
    cookie_key: cookie::Key,
    admin_pw: Box<str>,
}

const ADMIN_TOKEN_NAME: &str = "MERCURY_ADMINTOKEN";

async fn login(ctx: &AdminAuthContext, token: Bytes) -> Result<Response, Rejection> {
    info!("processing login call");
    if &token != ctx.admin_pw.as_bytes() {
        return Ok(StatusCode::FORBIDDEN.into_response());
    }

    let mut jar = CookieJar::new();
    jar.add(
        Cookie::build("MERCURY_IS_ADMIN", "1")
            .expires(None)
            .path("/admin")
            .finish(),
    );
    jar.private_mut(&ctx.cookie_key).add(
        Cookie::build(ADMIN_TOKEN_NAME, "true")
            .expires(None)
            .path("/api/admin")
            .http_only(true)
            .finish(),
    );

    let mut response = "".into_response();
    set_cookies(response.headers_mut(), jar);

    Ok(response)
}

fn set_cookies(headers: &mut HeaderMap, jar: CookieJar) {
    for cookie in jar.delta() {
        if let Some(value) = HeaderValue::try_from(format!("{cookie}")).ok() {
            headers.append("Set-Cookie", value);
        }
    }
}

#[derive(Debug)]
struct Unauthorized;
impl Reject for Unauthorized {}

async fn authenticate_admin_user(
    ctx: &AdminAuthContext,
    cookie: Option<String>,
) -> Result<(), Rejection> {
    fn auth_inner(ctx: &AdminAuthContext, cookie: Option<String>) -> Option<()> {
        let mut cookie = Cookie::new(ADMIN_TOKEN_NAME, cookie?);
        cookie = CookieJar::new().private(&ctx.cookie_key).decrypt(cookie)?;
        if cookie.value() == "true" {
            Some(())
        } else {
            None
        }
    }

    auth_inner(ctx, cookie).ok_or_else(|| reject::custom(Unauthorized))
}
async fn handle_failure(err: Rejection) -> Result<impl Reply, Infallible> {
    info!("handling failure : {err:?}");
    if let Some(Unauthorized) = err.find() {
        let mut jar = CookieJar::new();
        jar.remove(Cookie::named("MERCURY_IS_ADMIN"));
        jar.remove(Cookie::named(ADMIN_TOKEN_NAME));
        let mut response = StatusCode::UNAUTHORIZED.into_response();
        set_cookies(response.headers_mut(), jar);
        Ok(response)
    } else {
        Ok(StatusCode::INTERNAL_SERVER_ERROR.into_response())
    }
}

pub fn initialize(state: &InitializationState) -> BoxedFilter<(impl warp::Reply,)> {
    // let cookie_key = cookie::Key::generate();
    let cookie_key = cookie::Key::derive_from("testtestesttuanetuiarenaiurteunirtain".as_bytes());
    let auth_context = Arc::new(AdminAuthContext {
        cookie_key,
        admin_pw: state.admin_key.to_owned().into_boxed_str(),
    });

    let login_context = auth_context.clone();
    let admin_login = warp::post()
        .and(warp::path("login"))
        .and(warp::body::bytes())
        .and_then(move |token| {
            let ctx = login_context.clone();
            async move { login(&ctx, token).await }
        });

    let pool1 = state.db_pool.clone();
    let pool2 = pool1.clone();
    let pool3 = pool1.clone();
    let pool4 = pool1.clone();
    let pool5 = pool1.clone();

    let config_api = warp::get()
        .then(move || perform_call(&pool1, domain_logic::get_config))
        .or(warp::patch()
            .and(warp::body::json())
            .then(move |val: AppConfig| perform_call_value(&pool2, domain_logic::put_config, val)))
        .unify();

    let oauth_api = warp::get()
        .then(move || perform_call(&pool3, domain_logic::get_providers))
        .or(warp::delete().and(
            warp::body::bytes()
                .and_then(|body: Bytes| async move {
                    Ok::<_, InternalServerError>(String::from_utf8(body.to_vec())?)
                })
                .then(move |provider_id: String| {
                    perform_call_value(&pool4, domain_logic::delete_provider, provider_id)
                }),
        ))
        .unify()
        .or(
            warp::post().and(warp::body::json().then(move |provider: OAuthProvider| {
                perform_call_value(&pool5, domain_logic::add_provider, provider)
            })),
        )
        .unify();

    let authenticated_admin_api = warp::cookie::optional(ADMIN_TOKEN_NAME)
        .and_then(move |cookie| {
            let ctx = auth_context.clone();
            async move { authenticate_admin_user(&ctx, cookie).await }
        })
        .untuple_one()
        .and(
            (warp::path("config").and(config_api))
                .or(warp::path("oauth").and(oauth_api))
                .unify(),
        )
        .map(|r: Result<_, _>| {
            r.map_or_else(
                |j: InternalServerError| j.into_response(),
                |e: Json| e.into_response(),
            )
        });

    // .map(|_| "foo");

    admin_login
        .or(authenticated_admin_api)
        .recover(handle_failure)
        .boxed()
}

fn perform_call<F, R>(
    pool: &DbPool,
    action: F,
) -> impl std::future::Future<Output = Result<Json, InternalServerError>>
where
    F: FnOnce(DbPool) -> Result<R, InternalServerError>,
    R: Serialize,
{
    let pool = pool.clone();
    async move { tokio::task::block_in_place(|| action(pool).map(|v| warp::reply::json(&v))) }
}

fn perform_call_value<F, V, R>(
    pool: &DbPool,
    action: F,
    value: V,
) -> impl std::future::Future<Output = Result<Json, InternalServerError>>
where
    F: FnOnce(DbPool, V) -> Result<R, InternalServerError>,
    R: Serialize,
{
    let pool = pool.clone();
    async move { tokio::task::block_in_place(|| action(pool, value).map(|v| warp::reply::json(&v))) }
}
