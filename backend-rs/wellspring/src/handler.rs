use std::marker::PhantomData;

use time::OffsetDateTime;

use crate::context::EventContext;
use crate::decision::{DecisionFunction, DecisionResult};
use crate::fold::EventFold;
use crate::selector::ConsistencyBoundary;
use crate::store::{EventStore, StoreError};

#[derive(Debug, Clone, Copy)]
pub struct HandlerOptions {
	pub max_attempts: u32,
	pub max_expansions: u32,
}

impl Default for HandlerOptions {
	fn default() -> Self {
		Self {
			max_attempts: 10,
			max_expansions: 10,
		}
	}
}

#[derive(Debug, thiserror::Error)]
pub enum HandlerError {
	#[error("invariant violated: {0}")]
	Rejected(String),
	#[error(transparent)]
	Store(#[from] StoreError),
	#[error("event handler did not converge after {attempts} attempts")]
	Convergence { attempts: u32 },
	#[error("decision did not converge within the expansion limit")]
	ExpansionLimit,
}

pub struct IncomingEventHandler<S: EventStore, F: EventFold, D: DecisionFunction<F>> {
	store: S,
	decision: D,
	options: HandlerOptions,
	_fold: PhantomData<fn() -> F>,
}

impl<S: EventStore, F: EventFold, D: DecisionFunction<F>> IncomingEventHandler<S, F, D> {
	pub fn new(store: S, decision: D, options: HandlerOptions) -> Self {
		Self {
			store,
			decision,
			options,
			_fold: PhantomData,
		}
	}

	pub async fn execute(&self, now: OffsetDateTime) -> Result<D::Result, HandlerError> {
		let mut attempt: u32 = 0;
		loop {
			match self.try_execute(now).await {
				Err(HandlerError::Store(_err @ (StoreError::Conflict | StoreError::Transient(_)))) => {
					if attempt >= self.options.max_attempts.saturating_sub(1) {
						return Err(HandlerError::Convergence { attempts: attempt + 1 });
					}
					attempt += 1;
				}
				other => return other,
			}
		}
	}

	async fn try_execute(&self, now: OffsetDateTime) -> Result<D::Result, HandlerError> {
		let initial = self.decision.initial_selector();
		let mut context: EventContext<F> = self.store.read(std::slice::from_ref(&initial)).await?;
		let mut boundary: ConsistencyBoundary<F::Event> = context.to_boundary();

		for _ in 0..self.options.max_expansions {
			match self.decision.decide(&context.events, now) {
				DecisionResult::NeedMoreContext(additional) => {
					boundary.include(additional);
					context = self.store.read(boundary.selectors()).await?;
					boundary = context.to_boundary();
				}
				DecisionResult::Rejected(invariant) => {
					return Err(HandlerError::Rejected(invariant));
				}
				DecisionResult::Complete { result, events } => {
					self.store.append(&events, &boundary).await?;
					return Ok(result);
				}
			}
		}

		Err(HandlerError::ExpansionLimit)
	}
}
