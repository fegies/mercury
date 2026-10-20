use time::OffsetDateTime;

use crate::fold::EventFold;
use crate::selector::EventSelector;

pub enum DecisionResult<E: crate::AppEvent, R> {
	NeedMoreContext(EventSelector<E>),
	Rejected(String),
	Complete { result: R, events: Vec<E> },
}

pub trait DecisionFunction<F: EventFold>: Send + Sync {
	type Result;

	fn initial_selector(&self) -> EventSelector<F::Event>;

	fn decide(&self, events: &[F::Event], now: OffsetDateTime) -> DecisionResult<F::Event, Self::Result>;
}
