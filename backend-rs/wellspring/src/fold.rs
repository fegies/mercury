#[derive(Debug, thiserror::Error)]
pub enum DecodeError {
	#[error("unknown event type: {0}")]
	UnknownType(String),
	#[error("invalid event payload: {0}")]
	Invalid(#[from] serde_json::Error),
}

pub trait EventFold: Send + Sync {
	type Event: crate::AppEvent + Send + Sync + 'static;
	type State;

	fn decode(row: &crate::EventRow) -> Result<Self::Event, DecodeError>;
	fn seed() -> Self::State;
	fn fold(state: Self::State, event: &Self::Event) -> Self::State;

	fn decode_all(rows: &[crate::EventRow]) -> Result<Vec<Self::Event>, DecodeError> {
		rows.iter().map(Self::decode).collect()
	}
}
