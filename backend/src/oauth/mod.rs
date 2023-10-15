use cookie::{Cookie, SameSite};
use diesel::prelude::*;
use rand::distributions::{Alphanumeric, DistString};
use rand::thread_rng;
use rocket::http::CookieJar;
use rocket::response::{Redirect, Responder};
use rocket::serde::json::Json;
use rocket::{Response, Route};
use serde::{Deserialize, Serialize};
use tracing::Level;
use url::Url;

use crate::models::OAuthProvider;
use crate::response_types::{DbResult, QueryFailure};
use crate::{response_types::JsonResult, Db};

use crate::schema::app_config::dsl::*;
use crate::schema::oauth_providers::dsl::*;

pub fn routes() -> Vec<Route> {
    routes![
        list_available_providers,
        start_flow,
        finish_flow,
        flow_error,
        handle_interaction_required
    ]
}

#[derive(Serialize)]
pub struct OauthProvider {
    pub provider_name: String,
    pub flow_url: String,
}

fn load_provider(db: &mut PgConnection, provider: &str) -> QueryResult<Option<OAuthProvider>> {
    let provider = oauth_providers
        .filter(name.eq(&provider))
        .first(db)
        .optional()?;
    Ok(provider)
}
fn build_redirect_url(db: &mut PgConnection, provider_name: &str) -> QueryResult<String> {
    let app_domain: String = app_config.select(deployment_domain).first(db)?;
    Ok(format!("{app_domain}/oauth/flows/{provider_name}/callback"))
}

#[get("/api/oauth/providers")]
async fn list_available_providers(db: Db) -> JsonResult<Vec<OauthProvider>> {
    db.run(|db| {
        let domain: String = app_config.select(deployment_domain).first(db)?;

        let providers = oauth_providers.select(name).load::<String>(db)?;

        let result = providers
            .into_iter()
            .map(|p| OauthProvider {
                flow_url: format!("{domain}/oauth/flows/{p}"),
                provider_name: p,
            })
            .collect();

        Ok(Json(result))
    })
    .await
}

const OAUTH_SCOPES: &str = "openid profile";

#[get("/oauth/flows/<provider>")]
async fn start_flow(provider: String, db: Db, cookies: &CookieJar<'_>) -> DbResult<Redirect> {
    let state = Alphanumeric.sample_string(&mut thread_rng(), 20);
    let mut state_cookie = Cookie::new("OAUTH_STATE", state.clone());
    state_cookie.set_same_site(SameSite::Lax);
    cookies.add_private(state_cookie);

    let use_prompt_none = cookies
        .get_private("SAVED_PROVIDER")
        .map_or(false, |p| p.value() == provider);

    db.run(move |db| {
        let provider = load_provider(db, &provider)?.ok_or(diesel::result::Error::NotFound)?;

        let mut redirect_url = Url::parse(&provider.auth_url).unwrap();
        let mut query_pairs = redirect_url.query_pairs_mut();
        query_pairs
            .append_pair("client_id", &provider.client_id)
            .append_pair("redirect_uri", &build_redirect_url(db, &provider.name)?)
            .append_pair("scope", OAUTH_SCOPES)
            .append_pair("response_type", "code")
            .append_pair("state", &state);
        if use_prompt_none {
            query_pairs.append_pair("prompt", "none");
        }
        // .append_pair("prompt", "consent")
        // .finish();
        let url = query_pairs.finish().to_string();

        Ok(Redirect::temporary(url))
    })
    .await
}

#[derive(Responder)]
enum OAuthFailure {
    #[response(status = 400)]
    StateError(&'static str),
    #[response(status = 400)]
    QueryFailure(QueryFailure),
    #[response(status = 400)]
    InvalidProvider(&'static str),
    #[response(status = 403)]
    TokenRedeemError(String),
}
impl From<diesel::result::Error> for OAuthFailure {
    fn from(value: diesel::result::Error) -> Self {
        Self::QueryFailure(QueryFailure::from(value))
    }
}
impl From<reqwest::Error> for OAuthFailure {
    fn from(value: reqwest::Error) -> Self {
        Self::TokenRedeemError(format!("{value:?}"))
    }
}

#[get("/oauth/flows/<provider>/callback?error=interaction_required")]
fn handle_interaction_required(provider: String, cookies: &CookieJar<'_>) -> Redirect {
    cookies.remove_private(Cookie::named("SAVED_PROVIDER"));
    Redirect::temporary(format!("/oauth/flows/{provider}"))
}

#[get(
    "/oauth/flows/<provider>/callback?<error>&<error_description>",
    rank = 1
)]
async fn flow_error(provider: String, error: String, error_description: Option<String>) -> String {
    let error_description = error_description.unwrap_or_else(|| "no description".to_owned());
    error_description
}

#[get("/oauth/flows/<provider>/callback?<code>&<state>")]
async fn finish_flow(
    provider: String,
    db: Db,
    cookies: &CookieJar<'_>,
    code: String,
    state: String,
) -> Result<String, OAuthFailure> {
    let saved_state = cookies
        .get_private("OAUTH_STATE")
        .ok_or(OAuthFailure::StateError("no state available"))?;

    if &state != saved_state.value() {
        return Err(OAuthFailure::StateError("state does not match"));
    }

    #[derive(Serialize)]
    struct TokenParams {
        client_id: String,
        client_secret: String,
        scope: &'static str,
        grant_type: &'static str,
        code: String,
        redirect_uri: String,
    }

    let provider_name = provider.clone();
    let (token_uri, token_params): (String, TokenParams) = db
        .run(move |db| {
            let gather = tracing::span!(Level::INFO, "gathering params");
            let _ = gather.enter();
            let provider_name = provider;
            let provider = load_provider(db, &provider_name)?
                .ok_or(OAuthFailure::InvalidProvider("no such provider configured"))?;

            let redir_url = build_redirect_url(db, &provider_name)?;

            Ok::<_, OAuthFailure>((
                provider.token_url,
                TokenParams {
                    client_id: provider.client_id,
                    client_secret: provider.client_secret,
                    code,
                    redirect_uri: redir_url,
                    grant_type: "authorization_code",
                    scope: OAUTH_SCOPES,
                },
            ))
        })
        .await?;

    #[derive(Deserialize, Debug)]
    struct TokenResponse {
        access_token: String,
        token_type: String,
        expires_in: u32,
        scope: String,
        refresh_token: Option<String>,
        id_token: String,
    }

    let request_span = tracing::span!(Level::INFO, "requesting token");
    let _req = request_span.enter();
    let client = reqwest::Client::new();
    println!("url: {token_uri}");
    let resp = client.post(token_uri).form(&token_params).send().await?;
    if resp.status() != 200 {
        return Err(OAuthFailure::TokenRedeemError(resp.text().await?));
    }

    let tokens: TokenResponse = resp.json().await?;
    println!("{tokens:?}");

    cookies.add_private(Cookie::new("SAVED_PROVIDER", provider_name));

    Ok("fo".to_owned())
}
