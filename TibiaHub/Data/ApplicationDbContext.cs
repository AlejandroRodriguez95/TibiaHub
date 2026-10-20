using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TibiaHub.Identity;
using TibiaHub.CharacterScanning;

namespace TibiaHub.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ScannedCharacter> ScannedCharacters => Set<ScannedCharacter>();
    public DbSet<OnlineSession> OnlineSessions => Set<OnlineSession>();
    public DbSet<WorldObservation> WorldObservations => Set<WorldObservation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ScannedCharacter>(b =>
        {
            b.Property(c => c.World).HasMaxLength(100);
            b.Property(c => c.Name).HasMaxLength(100);
            b.Property(c => c.NormalizedName).HasMaxLength(100);
            b.HasIndex(c => new { c.World, c.NormalizedName }).IsUnique();
        });
        builder.Entity<OnlineSession>(b =>
        {
            b.HasOne(s => s.Character).WithMany().HasForeignKey(s => s.CharacterId).OnDelete(DeleteBehavior.Cascade);
            b.Property(s => s.LogoutAt).HasColumnName("logout_at");
            b.HasIndex(s => s.CharacterId).IsUnique().HasFilter("logout_at IS NULL");
            b.HasIndex(s => new { s.CharacterId, s.LoginAt });
            b.HasIndex(s => new { s.LoginAt, s.CharacterId });
            b.HasIndex(s => s.LogoutAt);
        });
        builder.Entity<WorldObservation>(b =>
        {
            b.Property(o => o.World).HasMaxLength(100);
            b.Property(o => o.SourceKey).HasMaxLength(500);
            b.Property(o => o.Exclusion).HasMaxLength(40);
            b.HasIndex(o => new { o.World, o.SourceAt });
            b.HasIndex(o => new { o.World, o.FetchedAt });
            b.HasIndex(o => o.FetchedAt);
        });
    }
}
