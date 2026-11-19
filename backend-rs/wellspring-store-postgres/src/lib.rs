mod scalar_extraction;

use std::{error::Error, fmt::Display, pin::pin};

use erased_serde::Serialize;
use futures_util::StreamExt;
use tokio_postgres::{
    Client,
    binary_copy::BinaryCopyInWriter,
    types::{Json, ToSql, Type},
};
use wellspring::{CommitLease, Event, EventSelector, EventStore};

use std::fmt::Write;

use crate::{PostgresStoreErr::SerializationError, scalar_extraction::extract_lock_keys};

pub struct PostgresStore {
    client: Client,
}

#[derive(Debug)]
pub enum PostgresStoreErr {
    PostgresError(tokio_postgres::Error),
    SerializationError(Box<dyn Error>),
    ResponseNotOneRow,
}

impl Error for PostgresStoreErr {}
impl Display for PostgresStoreErr {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            PostgresStoreErr::PostgresError(error) => write!(f, "DB error: {error}"),
            PostgresStoreErr::SerializationError(error) => {
                write!(f, "Serialization error: {error}")
            }
            PostgresStoreErr::ResponseNotOneRow => {
                write!(f, "Check result was not exactly one row")
            }
        }
    }
}
impl From<tokio_postgres::Error> for PostgresStoreErr {
    fn from(value: tokio_postgres::Error) -> Self {
        Self::PostgresError(value)
    }
}

#[derive(Debug)]
pub struct PostgresCommitLease<'s, TSel> {
    sequence_id: i64,
    selectors: &'s [TSel],
    client: &'s mut Client,
}

impl EventStore for PostgresStore {
    type Err = PostgresStoreErr;

    type CommitLease<'s, TS>
        = PostgresCommitLease<'s, TS>
    where
        TS: EventSelector + 's,
        Self: 's;

    async fn query_context<'s, TA, TS>(
        &'s mut self,
        selectors: &'s [TS],
    ) -> Result<(TA, Self::CommitLease<'s, TS>), Self::Err>
    where
        TA: wellspring::Aggregate,
        TS: wellspring::EventSelector,
    {
        if selectors.is_empty() {
            return Ok((
                TA::seed(),
                PostgresCommitLease {
                    sequence_id: 0,
                    selectors,
                    client: &mut self.client,
                },
            ));
        }

        let client = &mut self.client;

        let rows = with_boundary_predicate(selectors, async |predicate, predicate_params| {
            let statement = format!(
                "SELECT sequence_id, insertion_time, event_type, payload
			        FROM app_events WHERE {} ORDER BY sequence_id ASC",
                predicate
            );

            client
                .query_typed_raw(&statement, predicate_params.iter().cloned())
                .await
        })
        .await?;

        let mut rows = pin!(rows);

        let mut max_seq_id = 0;
        let mut result = TA::seed();

        while let Some(row) = rows.next().await {
            let row = row?;
            let sequence_id: i64 = row.get(0);
            let insertion_time: time::OffsetDateTime = row.get(1);
            let event_type: &str = row.get(2);
            let payload: &str = row.get(3);

            max_seq_id = sequence_id;
            result
                .include_event(insertion_time, event_type, payload)
                .map_err(|e| SerializationError(Box::new(e)))?;
        }

        Ok((
            result,
            PostgresCommitLease {
                sequence_id: max_seq_id,
                selectors,
                client: &mut self.client,
            },
        ))
    }
}

impl<'a, TSel> CommitLease for PostgresCommitLease<'a, TSel>
where
    TSel: EventSelector,
{
    type Err = PostgresStoreErr;

    async fn commit_events(
        self,
        events: impl IntoIterator<Item: Event>,
    ) -> Result<wellspring::CommitResult, Self::Err> {
        let events: Vec<_> = events.into_iter().collect();

        let lock_keys = extract_lock_keys(
            self.selectors
                .iter()
                .map(|s| s.dimensions())
                .chain(events.iter().map(|e| e as &dyn Serialize)),
        );

        let transaction = self.client.transaction().await?;

        transaction
            .execute_typed(
                "SELECT COUNT(pg_advisory_xact_lock(l)) FROM unnest($1) l",
                &[(&lock_keys, Type::INT8_ARRAY)],
            )
            .await?;

        let check_result =
            with_boundary_predicate(self.selectors, async |predicate, mut params| {
                let statement = format!(
                    "SELECT NOT EXISTS (
         			SELECT 1 FROM app_events
         			WHERE ({}) AND sequence_id > ${}
         		)",
                    predicate,
                    self.selectors.len() + 1,
                );

                params.push((&self.sequence_id, Type::INT8));

                let mut rows = pin!(transaction.query_typed_raw(&statement, params).await?);

                let ok = rows
                    .as_mut()
                    .next()
                    .await
                    .ok_or_else(|| PostgresStoreErr::ResponseNotOneRow)??;

                let ok: bool = ok.get(0);

                match rows.next().await {
                    Some(Ok(_)) => return Err(PostgresStoreErr::ResponseNotOneRow),
                    Some(Err(e)) => return Err(PostgresStoreErr::PostgresError(e)),
                    _ => {}
                }

                Ok(ok)
            })
            .await?;

        if !check_result {
            return Ok(wellspring::CommitResult::ConcurrencyConflict);
        }

        let writer = transaction
            .copy_in("COPY app_events (event_type, payload) FROM STDIN (FORMAT BINARY)")
            .await?;
        let mut writer = pin!(BinaryCopyInWriter::new(writer, &[Type::TEXT, Type::JSON]));

        for event in events {
            writer
                .as_mut()
                .write_raw([
                    &event.event_type() as &dyn ToSql,
                    &Json(Wrapper { inner: &event }),
                ])
                .await?;
        }

        writer.finish().await?;

        Ok(wellspring::CommitResult::Commited)
    }
}

struct Wrapper<'w> {
    inner: &'w dyn erased_serde::Serialize,
}
impl std::fmt::Debug for Wrapper<'_> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match serde_json::to_string(self.inner) {
            Ok(json) => f.debug_tuple("Wrapper").field(&json).finish(),
            Err(e) => write!(f, "Error formatting as json: {e:?}"),
        }
    }
}
impl serde::Serialize for Wrapper<'_> {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        self.inner.serialize(serializer)
    }
}

async fn with_boundary_predicate<'a, T, TF>(selectors: &[impl EventSelector], cb: TF) -> T
where
    TF: for<'i> AsyncFnOnce(String, Vec<(&'i dyn ToSql, Type)>) -> T,
{
    let params: Vec<_> = selectors
        .iter()
        .map(|s| {
            (
                s.event_types(),
                Json(Wrapper {
                    inner: s.dimensions(),
                }),
            )
        })
        .collect();

    let n = params.len();

    let params: Vec<_> = params
        .iter()
        .flat_map(|(t, c)| [(t as &dyn ToSql, Type::TEXT_ARRAY), (c, Type::JSON)])
        .collect();

    let mut predicate = String::new();
    for i in 1..=n {
        if i > 1 {
            predicate.push_str(" OR ");
        }

        write!(
            &mut predicate,
            "(event_type = ANY(${}) AND payload @> ${})",
            2 * i - 1,
            2 * i,
        )
        .unwrap();
    }

    cb(predicate, params).await
}
