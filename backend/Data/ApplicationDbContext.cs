using System;
using backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }


    public DbSet<UserEntity> Users { get; set; }
}
