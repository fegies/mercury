using System.Collections.Frozen;
using System.Threading.Channels;
using appcore.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace appcore.Infra.Events;

/// <summary>
/// Default <see cref="IAppBus"/>: an unbounded channel drained by a self-hosted pump, with
/// a copy-on-write frozen subscriber registry. Dispatch is single-drain and FIFO; handlers
/// for a message type (and all its ancestor types) run serially, in registration order.
/// </summary>
public sealed class InMemoryAppBus : IAppBus, IHostedService
{
	private readonly ILogger _logger;
	private readonly Channel<object> _channel = Channel.CreateUnbounded<object>();
	private readonly Lock _subscription_modification_lock = new();
	private volatile FrozenDictionary<Type, Subscription[]> _subscribers =
		FrozenDictionary<Type, Subscription[]>.Empty;
	private Task? _pump;
	private bool _started;

	public InMemoryAppBus(ILogger<InMemoryAppBus> logger)
	{
		_logger = logger;
	}

	public IDisposable Subscribe<TMessage>(Func<TMessage, CancellationToken, ValueTask> handler)
		where TMessage : class
	{
		var boxed = new Subscription((message, ct) => handler((TMessage)message, ct));

		lock (_subscription_modification_lock)
		{
			var updated = _subscribers.TryGetValue(typeof(TMessage), out var existing)
				? existing.Append(boxed).ToArray()
				: [boxed];
			_subscribers = new Dictionary<Type, Subscription[]>(_subscribers)
			{
				[typeof(TMessage)] = updated,
			}.ToFrozenDictionary();
		}

		return new Unsubscriber(() =>
		{
			lock (_subscription_modification_lock)
			{
				if (!_subscribers.TryGetValue(typeof(TMessage), out var current) || !current.Contains(boxed))
					return;

				var updated = new Dictionary<Type, Subscription[]>(_subscribers);
				var remaining = current.Where(s => s != boxed).ToArray();
				if (remaining.Length == 0)
					updated.Remove(typeof(TMessage));
				else
					updated[typeof(TMessage)] = remaining;
				_subscribers = updated.ToFrozenDictionary();
			}
		});
	}

	public ValueTask EmitAsync<TMessage>(TMessage message, CancellationToken ct = default)
		where TMessage : class
	{
		ArgumentNullException.ThrowIfNull(message);
		ct.ThrowIfCancellationRequested();
		_channel.Writer.TryWrite(message);
		return ValueTask.CompletedTask;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (!_started)
		{
			_started = true;
			_pump = Task.Run(PumpAsync);
		}
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_channel.Writer.TryComplete();
		if (_pump is not null)
			await _pump;
		lock (_subscription_modification_lock)
			_subscribers = FrozenDictionary<Type, Subscription[]>.Empty;
	}

	private async Task PumpAsync()
	{
		var ancestorCache = new Dictionary<Type, Type[]>();

		await foreach (var message in _channel.Reader.ReadAllAsync())
		{
			var subscribers = _subscribers;
			var type = message.GetType();
			if (!ancestorCache.TryGetValue(type, out var chain))
				ancestorCache[type] = chain = AncestorChainOf(type);

			foreach (var ancestor in chain)
			{
				if (!subscribers.TryGetValue(ancestor, out var bucket))
					continue;

				foreach (var handler in bucket)
				{
					try
					{
						await handler.Handler(message, CancellationToken.None);
					}
					catch (Exception ex)
					{
						_logger.AppBusHandlerFailed(ancestor.Name, ex);
					}
				}
			}
		}
	}

	private static Type[] AncestorChainOf(Type type)
	{
		var chain = new List<Type>(8);
		for (var current = type; current is not null && current != typeof(object); current = current!.BaseType)
			chain.Add(current);
		foreach (var @interface in type.GetInterfaces())
			if (!chain.Contains(@interface))
				chain.Add(@interface);
		chain.Add(typeof(object));
		return chain.ToArray();
	}

	private sealed class Subscription(Func<object, CancellationToken, ValueTask> handler)
	{
		public Func<object, CancellationToken, ValueTask> Handler { get; } = handler;
	}

	private sealed class Unsubscriber(Action unsubscribe) : IDisposable
	{
		private Action? _unsubscribe = unsubscribe;

		public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
	}
}