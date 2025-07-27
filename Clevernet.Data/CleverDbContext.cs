using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Clevernet.Data;

public class CleverDbContext : DbContext
{
    public CleverDbContext(DbContextOptions<CleverDbContext> options) : base(options) {}

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Set default schema for all entities
        modelBuilder.HasDefaultSchema("clevernet");
        
        modelBuilder.Entity<File>(e =>
        {   
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            
            // Share validation
            e.Property(x => x.Share)
                .IsRequired()
                .HasMaxLength(64);
            
            // Path validation
            e.Property(x => x.Path)
                .IsRequired()
                .HasMaxLength(1024);
                
            // Unique constraint on Share + Path combination
            e.HasIndex(x => new { x.Share, x.Path }).IsUnique();
            
            // Index on Share for fast lookups within a share
            e.HasIndex(x => x.Share);
                
            // Content can be null (for directories)
            e.Property(x => x.TextContent).IsRequired(false);
            e.Property(x => x.BinaryContent).IsRequired(false);
             
            // Timestamps
            e.Property(x => x.CreatedAt)
                .IsRequired();
                
            e.Property(x => x.UpdatedAt)
                .IsRequired()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<Ephemeron>(e =>
        {
            e.ToTable("Ephemeris");
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is File && (e.State == EntityState.Added || e.State == EntityState.Modified));

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in entries)
        {
            var file = (File)entry.Entity;
            if (entry.State == EntityState.Added)
            {
                file.CreatedAt = now;
            }
            file.UpdatedAt = now;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
    
    public DbSet<File> Files { get; set; }
    public DbSet<Ephemeron> Ephemeris { get; set; }
}