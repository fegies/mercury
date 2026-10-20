use crate::fold::EventFold;
use crate::selector::{ConsistencyBoundary, EventSelector};

pub struct EventContext<F: EventFold> {
	pub query: Vec<EventSelector<F::Event>>,
	pub events: Vec<F::Event>,
	head: i64,
}

impl<F: EventFold> EventContext<F> {
	pub fn new(query: Vec<EventSelector<F::Event>>, events: Vec<F::Event>, head: i64) -> Self {
		Self { query, events, head }
	}

	pub fn head(&self) -> i64 {
		self.head
	}

	pub fn state(&self) -> F::State {
		self.events.iter().fold(F::seed(), |state, event| F::fold(state, event))
	}

	pub fn to_boundary(&self) -> ConsistencyBoundary<F::Event> {
		ConsistencyBoundary::new(self.query.clone(), self.head)
	}
}
