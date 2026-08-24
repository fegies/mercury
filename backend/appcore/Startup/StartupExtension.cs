using appcore.Data;
using appcore.Infra;
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
        builder.Services.AddScoped<DbEventStore>();
        builder.Services.AddScoped<IEventReader>(sp => sp.GetRequiredService<DbEventStore>());
        builder.Services.AddScoped<IEventStore>(sp => sp.GetRequiredService<DbEventStore>());
        builder.Services.AddScoped<AuctionService>();
    }
}