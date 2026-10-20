use serde_json::Value;

#[derive(Debug, thiserror::Error)]
pub enum PayloadError {
	#[error("event payload must serialize to a JSON object")]
	NotAnObject,
	#[error("event payload must carry at least one top-level scalar property")]
	NoDimensions,
}

pub fn validate(payload: &Value) -> Result<(), PayloadError> {
	let Some(object) = payload.as_object() else {
		return Err(PayloadError::NotAnObject);
	};
	if !object
		.iter()
		.any(|(_, value)| matches!(value, Value::String(_) | Value::Number(_) | Value::Bool(_)))
	{
		return Err(PayloadError::NoDimensions);
	}
	Ok(())
}

pub fn scalar_dimensions(payload: &Value) -> Vec<(String, String)> {
	payload
		.as_object()
		.into_iter()
		.flatten()
		.filter(|(_, value)| matches!(value, Value::String(_) | Value::Number(_) | Value::Bool(_)))
		.map(|(property, value)| (property.clone(), scalar_text(value)))
		.collect()
}

pub fn scalar_text_of(payload: &Value, property: &str) -> Option<String> {
	match payload.get(property) {
		Some(value) => match value {
			Value::String(text) => Some(text.clone()),
			value @ (Value::Number(_) | Value::Bool(_)) => Some(value.to_string()),
			_ => None,
		},
		None => None,
	}
}

fn scalar_text(value: &Value) -> String {
	match value {
		Value::String(text) => text.clone(),
		other => other.to_string(),
	}
}
