using System.Linq.Expressions;
using appcore.Entities;
using appcore.Infra;

namespace appcore.Tests.Infrastructure;

internal class InMemoryContextProvider(List<StoredEvent> events) : IContextProvider
{
    private readonly List<StoredEvent> _events = events;
    private readonly List<long> _referenced_sequence_ids = [];
    private long _max_observed_sequence_id = long.MinValue;

    public Task<List<T>> QueryEvents<T>(Expression<Func<T, bool>> selector) where T : StoredEvent
    {
        var compiled = selector.Compile();
        var matching = _events.OfType<T>().Where(compiled).ToList();

        if (matching.Count > 0)
        {
            var maxSeq = matching.Max(e => e.SequenceId);
            _referenced_sequence_ids.Add(maxSeq);
            if (maxSeq > _max_observed_sequence_id)
                _max_observed_sequence_id = maxSeq;
        }

        return Task.FromResult(matching);
    }

    internal void SimulateConcurrentAppend(params StoredEvent[] newEvents)
    {
        long seq = _events.Count > 0 ? _events.Max(e => e.SequenceId) + 1 : 1;
        foreach (var e in newEvents)
        {
            e.SequenceId = seq++;
            _events.Add(e);
        }
    }

    internal bool IsConsistent()
    {
        if (_referenced_sequence_ids.Count == 0)
            return true;

        var current_max = _events.Count > 0 ? _events.Max(e => e.SequenceId) : long.MinValue;
        return current_max <= _max_observed_sequence_id;
    }
}
