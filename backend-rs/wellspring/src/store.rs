use std::future::Future;

use crate::context::EventContext;
use crate::fold::EventFold;
use crate::selector::EventSelector;

#[derive(Debug, thiserror::Error)]
pub enum StoreError {
	#[error("concurrency conflict")]
	Conflict,
	#[error("transient store failure: {0}")]
	Transient(String),
	#[error("{0}")]
	Decode(#[from] crate::DecodeError),
	#[error("{0}")]
	Payload(#[from] crate::payload::PayloadError),
	#[error("store failure: {0}")]
	Backend(String),
}

pub trait EventReader: Send + Sync {
	fn read<F: EventFold>(
		&self,
		boundary: &[EventSelector<F::Event>],
	) -> impl Future<Output = Result<EventContext<F>, StoreError>> + Send;
}

pub trait EventStore: EventReader {
	fn append<E: crate::AppEvent + Sync>(
		&self,
		events: &[E],
		boundary: &crate::ConsistencyBoundary<E>,
	) -> impl Future<Output = Result<(), StoreError>> + Send;
}

impl<T: EventReader> EventReader for &T {
	async fn read<F: EventFold>(
		&self,
		boundary: &[EventSelector<F::Event>],
	) -> Result<EventContext<F>, StoreError> {
		(**self).read(boundary).await
	}
}

impl<T: EventStore> EventStore for &T {
	async fn append<E: crate::AppEvent + Sync>(
		&self,
		events: &[E],
		boundary: &crate::ConsistencyBoundary<E>,
	) -> Result<(), StoreError> {
		(**self).append(events, boundary).await
	}
}
