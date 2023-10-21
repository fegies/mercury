// mod api;
mod admin_api;
pub mod avatars;
pub mod models;
mod oauth;
pub mod response_types;
mod schema;
mod user_info;
use std::env;

use diesel::PgConnection;
use diesel_migrations::{embed_migrations, EmbeddedMigrations, MigrationHarness};
use dotenvy::dotenv;
use rocket::{fairing::AdHoc, get, launch};
use rocket_sync_db_pools::database;
use tracing_subscriber::fmt::format::FmtSpan;

#[macro_use]
extern crate rocket;

pub const MIGRATIONS: EmbeddedMigrations = embed_migrations!("migrations");

fn init_tracing() {
    // let filter = std::env::var("RUST_LOG").unwrap_or_else(|_| "tracing=info,warp=info".to_owned());

    // Configure the default `tracing` subscriber.
    // The `fmt` subscriber from the `tracing-subscriber` crate logs `tracing`
    // events to stdout. Other subscribers are available for integrating with
    // distributed tracing systems such as OpenTelemetry.
    tracing_subscriber::fmt()
        // Use the filter we built above to determine which traces to record.
        // Record an event when each span closes. This can be used to time our
        // routes' durations!
        .with_span_events(FmtSpan::CLOSE)
        .init();
}

fn migrate_db(pg: &mut PgConnection) {
    tracing::info_span!("migrating database").in_scope(|| {
        pg.run_pending_migrations(MIGRATIONS)
            .expect("could not run migrations");
    })
}

fn get_env_var(name: &str) -> String {
    env::var(name).expect(&format!("{name} must be set"))
}

#[database("diesel")]
struct Db(diesel::PgConnection);

#[get("/")]
fn index() -> &'static str {
    "test"
}

struct AdminPassword(String);

#[launch]
fn rocket() -> _ {
    dotenv().ok();
    init_tracing();

    let database_url = get_env_var("DATABASE_URL");
    let signing_key = "mUQLRO6z67uJ4yKHANismXvErSK0sjDLNsxgq+yrick=";

    let admin_password = get_env_var("ADMIN_PASSWORD");

    // todo!();
    // migrate_db(&database_url);
    let config = rocket::Config::figment()
        .merge(("databases.diesel.url", database_url))
        .merge(("databases.diesel.pool_size", 4))
        .merge(("secret_key", signing_key));

    rocket::custom(config)
        .manage(AdminPassword(admin_password))
        .attach(Db::fairing())
        .attach(AdHoc::on_ignite("Diesel migrations", |rocket| async move {
            let db = Db::get_one(&rocket)
                .await
                .expect("could not get db connection");

            db.run(|db| migrate_db(db)).await;
            rocket
        }))
        .mount("/", routes![index])
        .mount("/api/admin", admin_api::routes())
        .mount("/api/users", user_info::routes())
        .mount("/api/users/picture", avatars::routes())
        .mount("/", crate::oauth::routes())
}
