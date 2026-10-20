use crate::app_event::AppEvent;

pub trait EventPublisher: Send + Sync {
	fn publish<E: AppEvent + Sync>(&self, events: &[E]);
}

impl EventPublisher for () {
	fn publish<E: AppEvent + Sync>(&self, _events: &[E]) {}
}
