using appcore.Infra.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace appcore.Tests.Events;

internal interface ISampleMarker { }

internal class SampleBase(string value)
{
	public string Value { get; } = value;
}

internal sealed class SampleMessage(string value) : SampleBase(value), ISampleMarker;

public class InMemoryAppBusTests
{
	[Fact]
	public async Task EmitsToExactTypeSubscriber()
	{
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var received = new List<string>();
		using (bus.Subscribe<SampleMessage>((m, _) => { received.Add(m.Value); return ValueTask.CompletedTask; }))
		{
			await bus.StartAsync(CancellationToken.None);
			await bus.EmitAsync(new SampleMessage("a"));
			await bus.StopAsync(CancellationToken.None);
		}

		Assert.Equal(["a"], received);
	}

	[Fact]
	public async Task DerivedMessageReachesBaseTypeSubscriber()
	{
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var received = new List<string>();
		using (bus.Subscribe<SampleBase>((m, _) => { received.Add(m.Value); return ValueTask.CompletedTask; }))
		{
			await bus.StartAsync(CancellationToken.None);
			await bus.EmitAsync(new SampleMessage("via-base"));
			await bus.StopAsync(CancellationToken.None);
		}

		Assert.Equal(["via-base"], received);
	}

	[Fact]
	public async Task EmitsToInterfaceSubscriber()
	{
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var received = new List<string>();
		using (bus.Subscribe<ISampleMarker>((m, _) => { received.Add(((SampleMessage)m).Value); return ValueTask.CompletedTask; }))
		{
			await bus.StartAsync(CancellationToken.None);
			await bus.EmitAsync(new SampleMessage("via-interface"));
			await bus.StopAsync(CancellationToken.None);
		}

		Assert.Equal(["via-interface"], received);
	}

	[Fact]
	public async Task ObjectSubscriberReceivesEverything()
	{
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var received = new List<string>();
		using (bus.Subscribe<object>((m, _) => { received.Add(m.GetType().Name); return ValueTask.CompletedTask; }))
		{
			await bus.StartAsync(CancellationToken.None);
			await bus.EmitAsync(new SampleMessage(""));
			await bus.StopAsync(CancellationToken.None);
		}

		Assert.Equal(["SampleMessage"], received);
	}

	[Fact]
	public async Task BothExactAndBaseSubscribersFire()
	{
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var exact = 0;
		var derivedBase = 0;
		using (bus.Subscribe<SampleMessage>((_, _) => { exact++; return ValueTask.CompletedTask; }))
		using (bus.Subscribe<SampleBase>((_, _) => { derivedBase++; return ValueTask.CompletedTask; }))
		{
			await bus.StartAsync(CancellationToken.None);
			await bus.EmitAsync(new SampleMessage("x"));
			await bus.StopAsync(CancellationToken.None);
		}

		Assert.Equal(1, exact);
		Assert.Equal(1, derivedBase);
	}

	[Fact]
	public async Task HandlersRunInRegistrationOrder()
	{
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var received = new List<string>();
		using (bus.Subscribe<SampleMessage>((m, _) => { received.Add(m.Value + "1"); return ValueTask.CompletedTask; }))
		using (bus.Subscribe<SampleMessage>((m, _) => { received.Add(m.Value + "2"); return ValueTask.CompletedTask; }))
		{
			await bus.StartAsync(CancellationToken.None);
			await bus.EmitAsync(new SampleMessage("a"));
			await bus.StopAsync(CancellationToken.None);
		}

		Assert.Equal(["a1", "a2"], received);
	}

	[Fact]
	public async Task PreservesFifoOrderAcrossMultipleEmissions()
	{
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var received = new List<string>();
		using (bus.Subscribe<SampleMessage>((m, _) => { received.Add(m.Value); return ValueTask.CompletedTask; }))
		{
			await bus.StartAsync(CancellationToken.None);
			await bus.EmitAsync(new SampleMessage("a"));
			await bus.EmitAsync(new SampleMessage("b"));
			await bus.EmitAsync(new SampleMessage("c"));
			await bus.StopAsync(CancellationToken.None);
		}

		Assert.Equal(["a", "b", "c"], received);
	}

	[Fact]
	public async Task HandlerFailureDoesNotPreventOtherHandlersOrMessages()
	{
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var healthy = new List<string>();
		using (bus.Subscribe<SampleMessage>((_, _) => throw new InvalidOperationException("boom")))
		using (bus.Subscribe<SampleMessage>((m, _) => { healthy.Add(m.Value); return ValueTask.CompletedTask; }))
		{
			await bus.StartAsync(CancellationToken.None);
			await bus.EmitAsync(new SampleMessage("a"));
			await bus.EmitAsync(new SampleMessage("b"));
			await bus.StopAsync(CancellationToken.None);
		}

		Assert.Equal(["a", "b"], healthy);
	}

	[Fact]
	public async Task UnsubscribingStopsDelivery()
	{
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var received = new List<string>();
		var beforeArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var subscription = bus.Subscribe<SampleMessage>((m, _) =>
		{
			received.Add(m.Value);
			if (m.Value == "before")
				beforeArrived.SetResult();
			return ValueTask.CompletedTask;
		});
		await bus.StartAsync(CancellationToken.None);

		await bus.EmitAsync(new SampleMessage("before"));
		await beforeArrived.Task;
		subscription.Dispose();
		await bus.EmitAsync(new SampleMessage("after"));
		await bus.StopAsync(CancellationToken.None);

		Assert.Equal(["before"], received);
	}

	[Fact]
	public async Task StopDrainsQueuedMessages()
	{
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var received = new List<string>();
		using (bus.Subscribe<SampleMessage>((m, _) => { received.Add(m.Value); return ValueTask.CompletedTask; }))
		{
			await bus.StartAsync(CancellationToken.None);
			await bus.EmitAsync(new SampleMessage("a"));
			await bus.EmitAsync(new SampleMessage("b"));
			await bus.StopAsync(CancellationToken.None);
		}

		Assert.Equal(["a", "b"], received);
	}
}