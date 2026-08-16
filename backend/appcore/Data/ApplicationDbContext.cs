using appcore.Entities;
using appcore.Entities.Events;
using Microsoft.EntityFrameworkCore;

namespace appcore.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuctionCreated>().Property(p => p.SequenceId)
            .HasDefaultValueSql("nextval('event_id_seq')");
    }

    public DbSet<UserEntity> Users { get; set; }

}
