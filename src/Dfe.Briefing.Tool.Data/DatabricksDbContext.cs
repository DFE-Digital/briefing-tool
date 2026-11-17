using Dfe.Briefing.Tool.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;

namespace Dfe.Briefing.Tool.Data;

public class DatabricksDbContext(DbContextOptions<DatabricksDbContext> options, IConfiguration configuration) : DbContext(options)
{
    const string DEFAULT_SCHEMA = "mis_mstr";
    public virtual DbSet<MisEstablishment> Establishments { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var connectionString = configuration.GetConnectionString("AcademiesConnection");
            optionsBuilder.UseSqlServer(connectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MisEstablishment>(ConfigureEstablishment);
        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureEstablishment(EntityTypeBuilder<MisEstablishment> establishmentConfiguration)
    {
        establishmentConfiguration.HasKey(e => e.Urn);
        establishmentConfiguration.ToTable("Establishments", DEFAULT_SCHEMA);
        establishmentConfiguration.Property(e => e.WebLink).HasColumnName("WebLink");
        establishmentConfiguration.Property(e => e.Urn).HasColumnName("urn").ValueGeneratedNever();
        establishmentConfiguration.Property(e => e.Laestab).HasColumnName("laestab");
        establishmentConfiguration.Property(e => e.SchoolName).HasColumnName("school_name");
    }
}
