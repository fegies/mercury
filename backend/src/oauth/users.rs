use crate::models::User;
use crate::schema::{external_users::dsl::*, users::dsl::*};
use cookie::time::OffsetDateTime;
use cookie::{Cookie, SameSite};
use diesel::PgConnection;
use diesel::{prelude::*, result};
use rocket::http::{CookieJar, Status};
use rocket::request::{self, FromRequest, Outcome};
use rocket::Request;
use serde::{Deserialize, Serialize};

use super::UserDescriptor;

pub struct UserManager<'a> {
    db: &'a mut PgConnection,
}

#[derive(Serialize, Deserialize, Debug)]
pub struct RegisteredUser {
    id: uuid::Uuid,
    name: String,
    preferred_username: String,
    pub can_start_auctions: bool,
}

impl From<User> for RegisteredUser {
    fn from(value: User) -> Self {
        Self {
            id: value.user_id,
            name: value.display_name,
            preferred_username: value.preferred_username,
            can_start_auctions: value.can_start_auctions,
        }
    }
}

impl RegisteredUser {
    pub fn set_as_cookie(&self, cookies: &CookieJar, expiration: u64) -> Option<()> {
        let value = serde_json::to_string(&self).ok()?;
        let mut cookie = Cookie::new("USER_SESSION", value);
        let expiration = OffsetDateTime::from_unix_timestamp(expiration as i64).ok()?;
        cookie.set_expires(expiration);
        cookie.set_same_site(SameSite::Lax);
        cookies.add_private(cookie);
        Some(())
    }
    pub fn get_id(&self) -> uuid::Uuid {
        self.id
    }
}

#[derive(Debug)]
pub enum RegisteredUserError {
    NoSessionCookie,
    InvalidSessionCookie,
}

#[rocket::async_trait]
impl<'r> FromRequest<'r> for RegisteredUser {
    type Error = RegisteredUserError;

    async fn from_request(req: &'r Request<'_>) -> request::Outcome<Self, Self::Error> {
        fn inner(req: &Request<'_>) -> Result<RegisteredUser, RegisteredUserError> {
            let cookie = req
                .cookies()
                .get_private("USER_SESSION")
                .ok_or(RegisteredUserError::NoSessionCookie)?;

            let user = serde_json::from_str(cookie.value())
                .map_err(|_| RegisteredUserError::InvalidSessionCookie)?;
            Ok(user)
        }
        match inner(req) {
            Ok(user) => rocket::outcome::Outcome::Success(user),
            Err(err) => rocket::outcome::Outcome::Failure((Status::Forbidden, err)),
        }
    }
}

impl<'a> UserManager<'a> {
    pub fn new(db: &'a mut PgConnection) -> Self {
        Self { db }
    }
    // translates the external user to its local equivalent.
    // creates/updates our user record if needed.
    pub fn translate_user(&mut self, user: UserDescriptor) -> QueryResult<RegisteredUser> {
        let existing_user = external_users
            .inner_join(users)
            .filter(issuer.eq(&user.iss).and(issuer_sub.eq(&user.sub)))
            .select(User::as_select())
            .first::<User>(self.db)
            .optional()?;
        // .optional()?;

        let existing_user = if let Some(mut existing_user) = existing_user {
            self.update_user_registration(&mut existing_user, user)?;
            existing_user
        } else {
            self.create_user_registration(user)?
        };

        Ok(existing_user.into())
    }

    fn update_user_registration(
        &mut self,
        existing_user: &mut User,
        user: UserDescriptor,
    ) -> QueryResult<()> {
        if existing_user.display_name != user.name
            || existing_user.preferred_username != user.preferred_username
        {
            existing_user.display_name = user.name;
            existing_user.preferred_username = user.preferred_username;
            diesel::update(users.filter(user_id.eq(&existing_user.user_id)))
                .set(&*existing_user)
                .execute(self.db)?;
        }
        Ok(())
    }

    fn create_user_registration(&mut self, user: UserDescriptor) -> QueryResult<User> {
        self.db.transaction(move |db| {
            let db_user: User = diesel::insert_into(users)
                .values((
                    display_name.eq(user.name),
                    preferred_username.eq(user.preferred_username),
                ))
                .get_result(db)?;

            diesel::insert_into(external_users)
                .values((
                    internal_user.eq(&db_user.user_id),
                    issuer.eq(user.iss),
                    issuer_sub.eq(user.sub),
                ))
                .execute(db)?;

            Ok(db_user)
        })
    }
}
