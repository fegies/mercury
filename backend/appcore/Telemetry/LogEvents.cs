using Microsoft.Extensions.Logging;

namespace appcore.Telemetry;

public static partial class LogEvents
{
	[LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "AppBus handler for {MessageType} failed.")]
	public static partial void AppBusHandlerFailed(this ILogger logger, string messageType, Exception exception);

	[LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Auto-closed auction {AuctionId} after its closure time.")]
	public static partial void AutoClosedAuction(this ILogger logger, Guid auctionId);

	[LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Auction {AuctionId} was not auto-closed: {Message}")]
	public static partial void AutoCloseNotApplied(this ILogger logger, Guid auctionId, string message);

	[LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Auction {AuctionId} could not be auto-closed after exhausting retries.")]
	public static partial void AutoCloseFailed(this ILogger logger, Guid auctionId, Exception exception);

	[LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Reconciled {OpenAuctionCount} open auction(s) at startup.")]
	public static partial void ReconciledOpenAuctions(this ILogger logger, int openAuctionCount);

	[LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Startup reconciliation of open auctions failed.")]
	public static partial void StartupReconciliationFailed(this ILogger logger, Exception exception);
}