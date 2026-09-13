using System.Threading.Channels;
using appcore.Infra.Expiry;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace appcore.Tests.Expiry;

public class AuctionDeadlineSchedulerTests
{
	private readonly FakeTimeProvider _clock = new();
	private readonly AuctionDeadlineScheduler _scheduler;

	public AuctionDeadlineSchedulerTests()
	{
		_scheduler = new AuctionDeadlineScheduler(_clock);
	}

	[Fact]
	public async Task YieldsAsSoonAsDeadlineArrives()
	{
		var id = Guid.NewGuid();
		var deadline = Now().AddHours(2);
		_scheduler.Schedule(id, deadline);

		var stream = Start();
		_clock.Advance(TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1));
		Assert.True(await stream.NothingAsync());
		_clock.Advance(TimeSpan.FromSeconds(1));

		var elapsed = await stream.NextAsync();
		Assert.Equal(id, elapsed.AuctionId);
		Assert.Equal(deadline, elapsed.Deadline);
	}

	[Fact]
	public async Task HandlesFarFutureDeadlineAcrossMultipleCappedDelays()
	{
		var id = Guid.NewGuid();
		var deadline = Now().AddYears(2);
		_scheduler.Schedule(id, deadline);

		var stream = Start();
		_clock.Advance(TimeSpan.FromHours(24)); // one capped hop
		Assert.True(await stream.NothingAsync());
		_clock.Advance(TimeSpan.FromDays(365) - TimeSpan.FromHours(24));
		Assert.True(await stream.NothingAsync());
		_clock.Advance(TimeSpan.FromDays(365) + TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

		var elapsed = await stream.NextAsync();
		Assert.Equal(id, elapsed.AuctionId);
		Assert.Equal(deadline, elapsed.Deadline);
	}

	[Fact]
	public async Task ReschedulingReplacesEarlierDeadline()
	{
		var id = Guid.NewGuid();
		var first = Now().AddHours(1);
		var second = Now().AddHours(2);
		_scheduler.Schedule(id, first);
		_scheduler.Schedule(id, second);

		var stream = Start();
		_clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
		Assert.True(await stream.NothingAsync()); // stale entry from `first` skipped
		_clock.Advance(TimeSpan.FromHours(1));

		var elapsed = await stream.NextAsync();
		Assert.Equal(second, elapsed.Deadline);
	}

	[Fact]
	public async Task RemoveDropsPendingDeadline()
	{
		var id = Guid.NewGuid();
		_scheduler.Schedule(id, Now().AddHours(1));
		_scheduler.Remove(id);

		var stream = Start();
		_clock.Advance(TimeSpan.FromHours(2));

		Assert.True(await stream.NothingAsync());
	}

	[Fact]
	public async Task EmitsInChronologicalOrder()
	{
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();
		_scheduler.Schedule(b, Now().AddHours(1));
		_scheduler.Schedule(a, Now().AddHours(2));

		var stream = Start();
		_clock.Advance(TimeSpan.FromHours(1));
		Assert.Equal(b, (await stream.NextAsync()).AuctionId);
		_clock.Advance(TimeSpan.FromHours(1));
		Assert.Equal(a, (await stream.NextAsync()).AuctionId);
	}

	[Fact]
	public async Task RemovingAnAuctionKeepsOthersOnTime()
	{
		var cancelled = Guid.NewGuid();
		var open = Guid.NewGuid();
		_scheduler.Schedule(cancelled, Now().AddHours(1));
		_scheduler.Schedule(open, Now().AddHours(3));
		_scheduler.Remove(cancelled);

		var stream = Start();
		_clock.Advance(TimeSpan.FromHours(2));
		Assert.True(await stream.NothingAsync());
		_clock.Advance(TimeSpan.FromHours(1));

		var elapsed = await stream.NextAsync();
		Assert.Equal(open, elapsed.AuctionId);
	}

	[Fact]
	public async Task ScheduleWhileParkedOnCapDelayStillFiresOnTime()
	{
		var stream = Start(); // nothing scheduled yet: loop parks on the 24h cap
		var id = Guid.NewGuid();
		var deadline = Now().AddHours(1);
		_scheduler.Schedule(id, deadline);

		Assert.True(await stream.NothingAsync());
		_clock.Advance(TimeSpan.FromHours(1));

		var elapsed = await stream.NextAsync();
		Assert.Equal(id, elapsed.AuctionId);
	}

	private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

	private Stream Start() => new(_scheduler);

	private sealed class Stream : IDisposable
	{
		private readonly AuctionDeadlineScheduler _scheduler;
		private readonly Channel<AuctionExpiryElapsed> _channel = Channel.CreateUnbounded<AuctionExpiryElapsed>();
		private readonly CancellationTokenSource _stop = new();

		public Stream(AuctionDeadlineScheduler scheduler)
		{
			_scheduler = scheduler;
			_ = Task.Run(Pump);
		}

		private async Task Pump()
		{
			await foreach (var item in _scheduler.ElapsedAsync(_stop.Token))
				await _channel.Writer.WriteAsync(item);
		}

		public async Task<AuctionExpiryElapsed> NextAsync()
		{
			var item = await _channel.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
			return item;
		}

		/// <summary>Asserts nothing is emitted within a bounded real-time window.</summary>
		public async Task<bool> NothingAsync()
		{
			for (var i = 0; i < 15; i++)
			{
				if (_channel.Reader.TryRead(out _))
					return false;
				await Task.Delay(10);
			}
			return true;
		}

		public void Dispose()
		{
			_stop.Cancel();
			_channel.Writer.TryComplete();
		}
	}
}