using System.Linq;
using System.Linq.Expressions;
using appcore.Data;
using appcore.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace appcore.Infra;

public interface IContextProvider
{
    public Task<List<T>> QueryEvents<T>(Expression<Func<T, bool>> selector)
        where T : StoredEvent;
}

internal class DbEventStore(ApplicationDbContext _context) : IContextProvider
{
    internal readonly ApplicationDbContext Context = _context;

    private readonly List<IQueryable<long>> _referenced_events = [];
    private long _max_observed_sequenceid = long.MinValue;

    public async Task<List<T>> QueryEvents<T>(Expression<Func<T, bool>> selector) where T : StoredEvent
    {
        var dbset = _context.Set<T>();

        var res_set = dbset.Where(selector);

        _referenced_events.Add(res_set.Select(e => e.SequenceId));

        var res = await res_set.ToListAsync();
        var m = res.Max(e => e.SequenceId);
        if (m > _max_observed_sequenceid)
            _max_observed_sequenceid = m;

        return res;
    }

    internal async Task AssertContextIsStillConsistent()
    {
        if (_referenced_events.Count == 0)
            return;

        var res_q = _referenced_events.Aggregate((l, r) => l.Union(r));
        var checked_max = await res_q.MaxAsync();

        if (checked_max > _max_observed_sequenceid)
            throw new ContextInconsistentException();
    }
}

internal class ContextInconsistentException() : Exception
{

}

public interface EventEvaluator<TEvent, TResult>
{
    public TResult EvaluateEventWithContext(TEvent input, IContextProvider provider, out List<StoredEvent> generated_events, CancellationToken ct);
}

internal class IncomingEventHandler<TEvent, TResult>(DbEventStore provider, EventEvaluator<TEvent, TResult> evaluator)
{
    public async Task Execute(TEvent input, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await TryExecuteAndPersist(input, ct);
            }
            catch (NpgsqlException ex)
            {
            }
        }


    }

    private async Task<TResult> TryExecuteAndPersist(TEvent input, CancellationToken ct)
    {
        var res = evaluator.EvaluateEventWithContext(input, provider, out var generated_events, ct);

        if (generated_events.Count > 0)
        {
            var ctx = provider.Context;
            using var trans = ctx.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
            await provider.AssertContextIsStillConsistent();
            ctx.AddRange(generated_events);
            await ctx.SaveChangesAsync(ct);

            trans.Commit();
        }
        return res;
    }
}
