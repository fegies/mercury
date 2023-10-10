mod api;
mod schema;
use std::env;

use diesel::{
    r2d2::{ConnectionManager, Pool},
    Connection, PgConnection,
};
use diesel_migrations::{embed_migrations, EmbeddedMigrations, MigrationHarness};
use dotenvy::dotenv;
use tracing_subscriber::fmt::format::FmtSpan;

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

fn migrate_db(db_url: &str) {
    tracing::info_span!("migrating database").in_scope(|| {
        let mut pg = PgConnection::establish(&db_url).expect("could not connect to db");
        pg.run_pending_migrations(MIGRATIONS)
            .expect("could not run migrations");
    })
}

fn get_env_var(name: &str) -> String {
    env::var(name).expect(&format!("{name} must be set"))
}

pub type DbPool = Pool<ConnectionManager<PgConnection>>;
pub struct InitializationState {
    pub admin_key: String,
    pub db_pool: DbPool,
}

impl InitializationState {
    fn gather() -> Self {
        let database_url = get_env_var("DATABASE_URL");
        let db_pool = Pool::builder()
            .min_idle(Some(1))
            .build(ConnectionManager::new(database_url))
            .expect("could not initialize connection pool");

        Self {
            admin_key: get_env_var("ADMIN_PASSWORD"),
            db_pool,
        }
    }
}

#[tokio::main]
async fn main() {
    dotenv().ok();
    init_tracing();

    let database_url = get_env_var("DATABASE_URL");
    migrate_db(&database_url);

    api::run(InitializationState::gather()).await
}
