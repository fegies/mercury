using appcore.Configuration;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Events;
using appcore.Infra.Evaluators;
using appcore.Infra.Expiry;
using appcore.Infra.Live;
using appcore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace appcore;

public static class StartupExtension
{
    public static void RegisterAppcoreServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("DefaultConnection")).Build());
        builder.Services.AddScoped<IEventReader, DbEventStore>();
        builder.Services.AddScoped<IEventStore, DbEventStore>();
        builder.Services.AddSingleton<EventHandlerOptions>();
        builder.Services.AddScoped<AuctionService>();
        builder.Services.AddScoped<UserService>();

        // In-memory event bus (non-persistent wakeup index) and its hosted pump.
        builder.Services.AddSingleton<InMemoryAppBus>();
        builder.Services.AddSingleton<IAppBus>(sp => sp.GetRequiredService<InMemoryAppBus>());
        builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<InMemoryAppBus>());
        builder.Services.AddSingleton(TimeProvider.System);

        // Auto-closes auctions once their closure time passes.
        builder.Services.AddHostedService<AuctionExpiryWorker>();

        // Live notification fan-out over the SSE stream (/api/events/stream).
        builder.Services.AddSingleton<LiveEventHub>();
        builder.Services.AddHostedService<LiveEventBridge>();

        builder.Services.AddScoped<IDecisionFunction<AuctionCreated, Guid>, CreateAuctionEvaluator>();
        builder.Services.AddScoped<IDecisionFunction<AuctionUpdated, bool>, UpdateAuctionEvaluator>();
        builder.Services.AddScoped<IDecisionFunction<AuctionImagesRemoved, bool>, RemoveImagesEvaluator>();
        builder.Services.AddScoped<IDecisionFunction<AuctionClosed, bool>, CloseAuctionEvaluator>(sp =>
            new CloseAuctionEvaluator(sp.GetRequiredService<AuctionConfig>()));
        builder.Services.AddScoped<IDecisionFunction<AuctionCloseExtended, bool>, ExtendAuctionCloseEvaluator>();
        builder.Services.AddScoped<IDecisionFunction<AuctionCancelled, bool>, CancelAuctionEvaluator>();
        builder.Services.AddScoped<IDecisionFunction<AuctionImagesAdded, bool>, AddImagesEvaluator>();
        builder.Services.AddScoped<IDecisionFunction<BidPlaced, BidResult>, PlaceBidEvaluator>(sp =>
            new PlaceBidEvaluator(sp.GetRequiredService<AuctionConfig>().MinBidIncrement));
        builder.Services.AddScoped<IDecisionFunction<ProvisionUserInput, UserProvisionResult>, ProvisionUserEvaluator>();

        builder.Services.AddScoped<IncomingEventHandler<AuctionCreated, Guid>>();
        builder.Services.AddScoped<IncomingEventHandler<AuctionUpdated, bool>>();
        builder.Services.AddScoped<IncomingEventHandler<AuctionImagesRemoved, bool>>();
        builder.Services.AddScoped<IncomingEventHandler<AuctionClosed, bool>>();
        builder.Services.AddScoped<IncomingEventHandler<AuctionCloseExtended, bool>>();
        builder.Services.AddScoped<IncomingEventHandler<AuctionCancelled, bool>>();
        builder.Services.AddScoped<IncomingEventHandler<AuctionImagesAdded, bool>>();
        builder.Services.AddScoped<IncomingEventHandler<BidPlaced, BidResult>>();
        builder.Services.AddScoped<IncomingEventHandler<ProvisionUserInput, UserProvisionResult>>();
    }
}
