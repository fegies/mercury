use std::{error::Error, fmt::Debug};

use erased_serde::Serialize;

pub trait Event: Serialize {
    fn event_type(&self) -> &'static str;
}

pub trait Aggregate {
    type Error: Error + 'static;

    fn seed() -> Self;

    fn include_event(
        &mut self,
        insertion_time: time::OffsetDateTime,
        event_type: &str,
        event_payload: &str,
    ) -> Result<(), Self::Error>;
}

pub trait Fold<TEvent> {
    fn incorporate(self, event: &TEvent) -> Self;
}

pub enum DecisionResult<TResult, TNewEvent, TErr, TSelectors> {
    Accept { output: TResult, events: TNewEvent },
    Reject(TErr),
    NeedMoreData { selectors: TSelectors },
}

pub trait EventSelector {
    fn event_types<'s>(&'s self) -> &'s [&'s str];
    fn dimensions(&self) -> &dyn Serialize;
}

pub trait Decision<TContext>: Sized {
    type Output;
    type TNewEvent: Event;
    type TErr;
    type TSelector<'s>: EventSelector
    where
        Self: 's;

    fn initial_selector<'s>(&'s self) -> impl IntoIterator<Item = Self::TSelector<'s>> {
        []
    }

    fn decide<'s>(
        &'s self,
        context: TContext,
        selectors: &[Self::TSelector<'s>],
    ) -> DecisionResult<
        Self::Output,
        impl IntoIterator<Item = Self::TNewEvent> + 's,
        Self::TErr,
        impl IntoIterator<Item = Self::TSelector<'s>> + 's,
    >;
}

#[derive(Debug)]
pub enum EvalError {
    ExpansionsExhausted,
    CommitAttemptsExhausted,
    StoreError(Box<dyn Error>),
}
impl<E: Error + 'static> From<E> for EvalError {
    fn from(value: E) -> Self {
        Self::StoreError(Box::new(value))
    }
}

pub async fn evaluate_event_async<TR, TErr, TD, TAgg>(
    decision: &TD,
    store: &mut impl EventStore,
) -> Result<Result<TR, TErr>, EvalError>
where
    TD: Decision<TAgg, Output = TR, TErr = TErr>,
    TAgg: Aggregate,
{
    let mut selectors: Vec<_> = decision.initial_selector().into_iter().collect();

    let mut expansions_remaining = 10;
    let mut commit_attempts_remaining = 10;

    loop {
        let (context, commit_lease) = store.query_context(&selectors).await?;

        match decision.decide(context, &selectors) {
            DecisionResult::Accept { output, events } => {
                match commit_lease.commit_events(events).await? {
                    CommitResult::Commited => {
                        return Ok(Ok(output));
                    }
                    CommitResult::ConcurrencyConflict => {
                        commit_attempts_remaining -= 1;
                        if commit_attempts_remaining == 0 {
                            return Err(EvalError::CommitAttemptsExhausted);
                        }
                    }
                }
            }
            DecisionResult::Reject(rejection) => {
                return Ok(Err(rejection));
            }
            DecisionResult::NeedMoreData {
                selectors: more_selectors,
            } => {
                expansions_remaining -= 1;
                if expansions_remaining == 0 {
                    return Err(EvalError::ExpansionsExhausted);
                }
                core::mem::drop(commit_lease);
                selectors.extend(more_selectors);
            }
        }
    }
}

pub enum CommitResult {
    Commited,
    ConcurrencyConflict,
}

#[allow(async_fn_in_trait)]
pub trait EventStore {
    type Err: Error + 'static;

    type CommitLease<'s, TS>: CommitLease
    where
        TS: EventSelector + 's,
        Self: 's;

    async fn query_context<'s, TA, TS>(
        &'s mut self,
        selectors: &'s [TS],
    ) -> Result<(TA, Self::CommitLease<'s, TS>), Self::Err>
    where
        TA: Aggregate,
        TS: EventSelector;
}

#[allow(async_fn_in_trait)]
pub trait CommitLease {
    type Err: Error + 'static;

    async fn commit_events(
        self,
        events: impl IntoIterator<Item: Event>,
    ) -> Result<CommitResult, Self::Err>;
}
