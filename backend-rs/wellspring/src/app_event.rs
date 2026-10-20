use serde::Serialize;

pub trait AppEvent: Serialize {
	fn event_type(&self) -> &'static str;
}
