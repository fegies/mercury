use std::sync::Mutex;

use serde::{Deserialize, Serialize};
use time::OffsetDateTime;
use wellspring::{
	AppEvent, DecisionFunction, DecisionResult, DimensionConstraint, EventContext, EventFold,
	EventRow, EventSelector, EventStore, EventReader, HandlerOptions, IncomingEventHandler,
	StoreError,
};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct DocCreated {
	doc_id: String,
	title: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct DocRenamed {
	doc_id: String,
	title: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(untagged)]
enum DocEvent {
	Created(DocCreated),
	Renamed(DocRenamed),
}

impl AppEvent for DocEvent {
	fn event_type(&self) -> &'static str {
		match self {
			Self::Created(_) => "DocCreated",
			Self::Renamed(_) => "DocRenamed",
		}
	}
}

#[derive(Debug, Default, Clone)]
struct DocState {
	doc_id: Option<String>,
	title: Option<String>,
}

struct DocFold;

impl EventFold for DocFold {
	type Event = DocEvent;
	type State = DocState;

	fn decode(row: &EventRow) -> Result<DocEvent, wellspring::DecodeError> {
		match row.event_type.as_str() {
			"DocCreated" => Ok(DocEvent::Created(serde_json::from_value(row.payload.clone())?)),
			"DocRenamed" => Ok(DocEvent::Renamed(serde_json::from_value(row.payload.clone())?)),
			other => Err(wellspring::DecodeError::UnknownType(other.to_string())),
		}
	}

	fn seed() -> DocState {
		DocState::default()
	}

	fn fold(state: DocState, event: &DocEvent) -> DocState {
		match event {
			DocEvent::Created(created) => DocState {
				doc_id: Some(created.doc_id.clone()),
				title: Some(created.title.clone()),
			},
			DocEvent::Renamed(renamed) => DocState {
				title: Some(renamed.title.clone()),
				..state
			},
		}
	}
}

struct CreateDoc {
	doc_id: String,
	title: String,
}

impl DecisionFunction<DocFold> for CreateDoc {
	type Result = String;

	fn initial_selector(&self) -> EventSelector<DocEvent> {
		EventSelector::new(
			vec!["DocCreated"],
			vec![DimensionConstraint {
				property: "docId",
				value: self.doc_id.clone(),
			}],
		)
	}

	fn decide(&self, events: &[DocEvent], _now: OffsetDateTime) -> DecisionResult<DocEvent, String> {
		if !events.is_empty() {
			return DecisionResult::Rejected("document already exists".to_string());
		}
		DecisionResult::Complete {
			result: self.title.clone(),
			events: vec![DocEvent::Created(DocCreated {
				doc_id: self.doc_id.clone(),
				title: self.title.clone(),
			})],
		}
	}
}

struct RenameDoc {
	doc_id: String,
	title: String,
}

impl DecisionFunction<DocFold> for RenameDoc {
	type Result = ();

	fn initial_selector(&self) -> EventSelector<DocEvent> {
		EventSelector::new(
			vec!["DocRenamed"],
			vec![DimensionConstraint {
				property: "docId",
				value: self.doc_id.clone(),
			}],
		)
	}

	fn decide(&self, events: &[DocEvent], _now: OffsetDateTime) -> DecisionResult<DocEvent, ()> {
		if events.is_empty() {
			return DecisionResult::NeedMoreContext(EventSelector::new(
				vec!["DocCreated"],
				vec![DimensionConstraint {
					property: "docId",
					value: self.doc_id.clone(),
				}],
			));
		}
		DecisionResult::Complete {
			result: (),
			events: vec![DocEvent::Renamed(DocRenamed {
				doc_id: self.doc_id.clone(),
				title: self.title.clone(),
			})],
		}
	}
}

struct MockStore {
	rows: Mutex<Vec<EventRow>>,
}

impl EventReader for MockStore {
	async fn read<F: EventFold>(
		&self,
		boundary: &[EventSelector<F::Event>],
	) -> Result<EventContext<F>, StoreError> {
		let rows = self.rows.lock().unwrap();
		let mut matching: Vec<EventRow> =
			rows.iter().filter(|row| row.matches(boundary)).cloned().collect();
		matching.sort_by_key(|row| row.sequence_id);
		let head = matching.last().map_or(0, |row| row.sequence_id);
		let events = F::decode_all(&matching)?;
		Ok(EventContext::new(boundary.to_vec(), events, head))
	}
}

impl EventStore for MockStore {
	async fn append<E: AppEvent + Sync>(
		&self,
		events: &[E],
		boundary: &wellspring::ConsistencyBoundary<E>,
	) -> Result<(), StoreError> {
		if events.is_empty() {
			return Ok(());
		}

		let mut rows = self.rows.lock().unwrap();
		if rows.iter().any(|row| {
			row.sequence_id > boundary.last_position() && row.matches(boundary.selectors())
		}) {
			return Err(StoreError::Conflict);
		}

		let next_sequence_id = rows.iter().map(|row| row.sequence_id).max().unwrap_or(0) + 1;
		for (sequence_id, event) in (next_sequence_id..).zip(events) {
			let payload =
				serde_json::to_value(event).map_err(|error| StoreError::Backend(error.to_string()))?;
			wellspring::payload::validate(&payload)?;
			rows.push(EventRow {
				sequence_id,
				insertion_time: OffsetDateTime::now_utc(),
				event_type: event.event_type().to_string(),
				payload,
			});
		}
		Ok(())
	}
}

#[tokio::main]
async fn main() {
	let store = MockStore {
		rows: Mutex::new(Vec::new()),
	};
	let now = OffsetDateTime::now_utc();

	let create = IncomingEventHandler::new(
		&store,
		CreateDoc {
			doc_id: "d1".to_string(),
			title: "Specification".to_string(),
		},
		HandlerOptions::default(),
	);
	let title = create.execute(now).await.unwrap();
	println!("created: {title}");

	let rename = IncomingEventHandler::new(
		&store,
		RenameDoc {
			doc_id: "d1".to_string(),
			title: "Specification v2".to_string(),
		},
		HandlerOptions::default(),
	);
	rename.execute(now).await.unwrap();
	println!("renamed (decision expanded its boundary once)");

	let duplicate = IncomingEventHandler::new(
		&store,
		CreateDoc {
			doc_id: "d1".to_string(),
			title: "Other".to_string(),
		},
		HandlerOptions::default(),
	);
	let error = duplicate.execute(now).await.unwrap_err();
	println!("duplicate: {error}");

	let context = store
		.read::<DocFold>(&[EventSelector::of_types(&["DocCreated", "DocRenamed"])])
		.await
		.unwrap();
	let state = context.state();
	println!("state: doc_id = {:?}, title = {:?}", state.doc_id, state.title);
}
