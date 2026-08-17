using appcore.Entities;
using appcore.Infra;

namespace appcore.Tests.Infrastructure;

internal class InMemoryContextProvider(List<AppEvent> events) : IContextProvider
{
	private readonly List<AppEvent> _events = events;
	private long _max_observed_sequence_id = long.MinValue;

	public Task<List<AppEvent>> QueryEvents(string? eventType = null)
	{
		var matching = _events
			.Where(e => eventType == null || e.EventType == eventType)
			.OrderBy(e => e.SequenceId)
			.ToList();
		if (matching.Count > 0)
		{
			var max = matching.Max(e => e.SequenceId);
			if (max > _max_observed_sequence_id)
				_max_observed_sequence_id = max;
		}
		return Task.FromResult(matching);
	}

	public Task AssertConsistency()
	{
		if (_events.Count > 0)
		{
			var currentMax = _events.Max(e => e.SequenceId);
			if (currentMax > _max_observed_sequence_id)
				throw new ContextInconsistentException();
		}
		return Task.CompletedTask;
	}
}
