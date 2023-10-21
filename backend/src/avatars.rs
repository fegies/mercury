use std::str::FromStr;

use diesel::prelude::*;
use diesel::upsert::excluded;
use rocket::fairing::Fairing;
use rocket::http::Header;
use rocket::{http::ContentType, Route};
use sha2::Digest;
use sha2::Sha256;
use tracing::Level;
use uuid::Uuid;

use crate::{oauth::users::RegisteredUser, response_types::QueryFailure, Db};

pub fn routes() -> Vec<Route> {
    routes![picture]
}

#[derive(Responder)]
struct AvatarResponder {
    inner: Vec<u8>,
    content_type: ContentType,
    cache_header: Header<'static>,
}

#[get("/<user_id>")]
async fn picture(_user: RegisteredUser, user_id: &str, db: Db) -> Option<AvatarResponder> {
    let user = Uuid::parse_str(&user_id).ok()?;
    let (picture, mime) = db
        .run(move |db| {
            use crate::schema::profile_pics::dsl::*;

            profile_pics
                .filter(user_id.eq(&user))
                .select((picture, mime_type))
                .first::<(_, Option<String>)>(db)
                .ok()
        })
        .await?;

    let content_type = mime
        .and_then(|mime| ContentType::from_str(&mime).ok())
        .unwrap_or(ContentType::Binary);

    let responder = AvatarResponder {
        inner: picture,
        content_type,
        cache_header: Header::new("cache-control", "private, max-age=86400"),
    };

    Some(responder)
}

pub(crate) async fn refresh_profile_picture(
    db: Db,
    user: uuid::Uuid,
    auth_token: String,
) -> Option<()> {
    let span = tracing::span!(Level::INFO, "refresh_profile_picture");
    let _ = span.enter();
    let client = reqwest::Client::new();
    let resp = client
        .get("https://graph.microsoft.com/v1.0/me/photo/$value")
        .header("Authorization", format!("Bearer {auth_token}"))
        .send()
        .await
        .ok()?;

    let body = resp.bytes().await.ok()?.to_vec();
    let picture_hash = hash_picture(&body);
    let new_mime_type = infer::get(&body).map(|t| t.mime_type());

    db.run(move |db| {
        let db_span = tracing::info_span!("insert_to_db");
        db_span.follows_from(span);
        let _ = db_span.enter();
        use crate::schema::profile_pics::dsl::*;

        db.transaction(move |db| {
            let already_exists: bool = diesel::select(diesel::dsl::exists(
                profile_pics.filter(user_id.eq(&user).and(hash.eq(&picture_hash))),
            ))
            .get_result(db)?;

            if already_exists {
                info!("already present");
                return Ok(());
            }

            diesel::insert_into(profile_pics)
                .values((
                    user_id.eq(&user),
                    picture.eq(body),
                    hash.eq(picture_hash),
                    mime_type.eq(&new_mime_type),
                ))
                .on_conflict(user_id)
                .do_update()
                .set((
                    picture.eq(excluded(picture)),
                    mime_type.eq(excluded(mime_type)),
                    hash.eq(excluded(hash)),
                ))
                .execute(db)?;

            info!("picture inserted");

            Ok::<_, QueryFailure>(())
        })
        .ok();
    })
    .await;

    Some(())
}

fn hash_picture(picture: &[u8]) -> Uuid {
    let result: [u8; 32] = Sha256::new_with_prefix(picture).finalize().into();
    let shorter_result: [u8; 16] = (&result[..16]).try_into().expect("from longer to shorter");
    Uuid::from_bytes(shorter_result)
}
