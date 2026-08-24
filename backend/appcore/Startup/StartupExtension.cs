using appcore.Data;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace appcore;

public static class StartupExtension
{
    public static void RegisterAppcoreServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
        builder.Services.AddScoped<IEventReader, DbEventStore>();
        builder.Services.AddScoped<IEventStore, DbEventStore>();
        builder.Services.AddSingleton<EventHandlerOptions>();
        builder.Services.AddScoped<AuctionService>();

        builder.Services.AddScoped<IDecisionFunction<AuctionCreated, Guid>, CreateAuctionEvaluator>();
        builder.Services.AddScoped<IDecisionFunction<AuctionUpdated, bool>, UpdateAuctionEvaluator>();
        builder.Services.AddScoped<IDecisionFunction<AuctionImagesRemoved, bool>, RemoveImagesEvaluator>();
        builder.Services.AddScoped<IDecisionFunction<AuctionClosed, bool>, CloseAuctionEvaluator>();
        builder.Services.AddScoped<IDecisionFunction<AuctionImagesAdded, bool>, AddImagesEvaluator>();

        builder.Services.AddScoped<IncomingEventHandler<AuctionCreated, Guid>>();
        builder.Services.AddScoped<IncomingEventHandler<AuctionUpdated, bool>>();
        builder.Services.AddScoped<IncomingEventHandler<AuctionImagesRemoved, bool>>();
        builder.Services.AddScoped<IncomingEventHandler<AuctionClosed, bool>>();
        builder.Services.AddScoped<IncomingEventHandler<AuctionImagesAdded, bool>>();
    }
}
