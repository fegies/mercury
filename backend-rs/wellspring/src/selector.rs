use std::marker::PhantomData;

use crate::app_event::AppEvent;

#[derive(Clone, Debug)]
pub struct DimensionConstraint {
	pub property: &'static str,
	pub value: String,
}

pub struct EventSelector<E: AppEvent> {
	pub event_types: Vec<&'static str>,
	pub constraints: Vec<DimensionConstraint>,
	_family: PhantomData<fn() -> E>,
}

impl<E: AppEvent> EventSelector<E> {
	pub fn new(event_types: Vec<&'static str>, constraints: Vec<DimensionConstraint>) -> Self {
		Self {
			event_types,
			constraints,
			_family: PhantomData,
		}
	}

	pub fn of_types(event_types: &[&'static str]) -> Self {
		Self::new(event_types.to_vec(), Vec::new())
	}
}

impl<E: AppEvent> Clone for EventSelector<E> {
	fn clone(&self) -> Self {
		Self {
			event_types: self.event_types.clone(),
			constraints: self.constraints.clone(),
			_family: PhantomData,
		}
	}
}

impl<E: AppEvent> std::fmt::Debug for EventSelector<E> {
	fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
		f.debug_struct("EventSelector")
			.field("event_types", &self.event_types)
			.field("constraints", &self.constraints)
			.finish()
	}
}

pub struct ConsistencyBoundary<E: AppEvent> {
	selectors: Vec<EventSelector<E>>,
	last_position: i64,
}

impl<E: AppEvent> ConsistencyBoundary<E> {
	pub(crate) fn new(selectors: Vec<EventSelector<E>>, last_position: i64) -> Self {
		Self {
			selectors,
			last_position,
		}
	}

	pub fn selectors(&self) -> &[EventSelector<E>] {
		&self.selectors
	}

	pub fn last_position(&self) -> i64 {
		self.last_position
	}

	pub fn include(&mut self, selector: EventSelector<E>) {
		self.selectors.push(selector);
	}
}
