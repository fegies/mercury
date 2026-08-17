using appcore.Entities;
using Microsoft.EntityFrameworkCore;

namespace appcore.Data;

public class ApplicationDbContext : DbContext
{
	public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

	public DbSet<UserEntity> Users { get; set; }
}
