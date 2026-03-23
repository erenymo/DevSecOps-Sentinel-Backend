using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sentinel.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentinel.Infrastructure.Persistence.Context
{
    public class SentinelDbContext : IdentityDbContext<AppUser, AppRole, Guid>
    {
        public SentinelDbContext(DbContextOptions<SentinelDbContext> options) : base(options) { }

        public DbSet<Workspace> Workspaces => Set<Workspace>();
        public DbSet<Module> Modules => Set<Module>();
        public DbSet<Scan> Scans => Set<Scan>();
        public DbSet<Component> Components => Set<Component>();
        public DbSet<License> Licenses => Set<License>();
        public DbSet<Vulnerability> Vulnerabilities => Set<Vulnerability>();
        public DbSet<VexStatement> VexStatements => Set<VexStatement>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ComponentLicense>()
            .HasKey(cl => new { cl.ComponentId, cl.LicenseId }); // Composite Key

            modelBuilder.Entity<ComponentLicense>()
                .HasOne(cl => cl.Component)
                .WithMany(c => c.ComponentLicenses)
                .HasForeignKey(cl => cl.ComponentId);

            modelBuilder.Entity<ComponentLicense>()
                .HasOne(cl => cl.License)
                .WithMany(l => l.ComponentLicenses)
                .HasForeignKey(cl => cl.LicenseId);

            // Workspace -> AppUser
            modelBuilder.Entity<Workspace>()
                .HasOne<AppUser>()
                .WithMany(u => u.Workspaces)
                .HasForeignKey(w => w.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Workspace -> Module (1:N)
            modelBuilder.Entity<Module>()
                .HasOne(m => m.Workspace)
                .WithMany(w => w.Modules)
                .HasForeignKey(m => m.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Module -> Scan (1:N)
            modelBuilder.Entity<Scan>()
                .HasOne(s => s.Module)
                .WithMany(m => m.Scans)
                .HasForeignKey(s => s.ModuleId);

            // Scan -> Component (1:N)
            modelBuilder.Entity<Component>()
                .HasOne(c => c.Scan)
                .WithMany(s => s.Components)
                .HasForeignKey(c => c.ScanId);

            // VEX Relationship (Many-to-Many via Entity)
            modelBuilder.Entity<VexStatement>()
                .HasOne(v => v.Component)
                .WithMany(c => c.VexStatements)
                .HasForeignKey(v => v.ComponentId);

            modelBuilder.Entity<VexStatement>()
                .HasOne(v => v.Vulnerability)
                .WithMany(vul => vul.VexStatements)
                .HasForeignKey(v => v.VulnerabilityId);

            // Indexing for high-performance lookups
            modelBuilder.Entity<Component>().HasIndex(c => c.Purl);
            modelBuilder.Entity<Vulnerability>().HasIndex(v => v.ExternalId).IsUnique();
        }
    }
}
