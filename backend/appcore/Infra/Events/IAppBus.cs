namespace appcore.Infra.Events;

/// <summary>
/// In-memory, non-persistent message bus. Publishers emit strongly typed messages;
/// subscribers register per message type and receive the message itself and any
/// <c>StoredEvent</c> subtype. Dispatch is asynchronous, FIFO, single-drain.
/// Loss-tolerant by design — the durable source of truth lives in the event store;
/// the bus is a wakeup index that consumers can rebuild.
/// </summary>
public interface IAppBus
{
	/// <summary>
	/// Register a strongly typed handler for messages of runtime type <typeparamref name="TMessage"/>.
	/// The handler also receives messages of derived runtime types (e.g. subscribing to
	/// <c>StoredEvent</c> yields every stored domain event). Returns an <see cref="IDisposable"/>
	/// that removes the subscription. Thread-safe to call at any time.
	/// </summary>
	IDisposable Subscribe<TMessage>(Func<TMessage, CancellationToken, ValueTask> handler)
		where TMessage : class;

	/// <summary>
	/// Enqueue a message for dispatch. Non-blocking; routes by the message's runtime type and
	/// never throws because a handler failed. Messages emitted after shutdown are dropped.
	/// </summary>
	ValueTask EmitAsync<TMessage>(TMessage message, CancellationToken ct = default)
		where TMessage : class;
}