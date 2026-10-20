use serde_json::Value;

use crate::app_event::AppEvent;
use crate::payload;
use crate::selector::EventSelector;

#[derive(Clone)]
pub struct EventRow {
	pub sequence_id: i64,
	pub insertion_time: time::OffsetDateTime,
	pub event_type: String,
	pub payload: Value,
}

impl EventRow {
	pub fn matches<E: AppEvent>(&self, selectors: &[EventSelector<E>]) -> bool {
		selectors.iter().any(|selector| {
			selector.event_types.iter().any(|event_type| *event_type == self.event_type)
				&& selector.constraints.iter().all(|constraint| {
					payload::scalar_text_of(&self.payload, constraint.property).as_deref()
						== Some(constraint.value.as_str())
				})
		})
	}
}
