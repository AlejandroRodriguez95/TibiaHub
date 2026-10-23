using TibiaIntel.Vip;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TibiaIntel.Identity;
using TibiaIntel.CharacterScanning;

namespace TibiaIntel.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ScannedCharacter> ScannedCharacters => Set<ScannedCharacter>();
    public DbSet<OnlineSession> OnlineSessions => Set<OnlineSession>();
    public DbSet<WorldObservation> WorldObservations => Set<WorldObservation>();

    public DbSet<VipCharacter> VipCharacters => Set<VipCharacter>();
    public DbSet<VipGroup> VipGroups => Set<VipGroup>();
    public DbSet<VipMembership> VipMemberships => Set<VipMembership>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<VipCharacter>(b =>
        {
            b.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Property(c => c.Name).HasMaxLength(100);
            b.Property(c => c.NormalizedName).HasMaxLength(100);
            b.Property(c => c.World).HasMaxLength(100);
            b.HasIndex(c => new { c.UserId, c.NormalizedName }).IsUnique();
        });
        builder.Entity<VipGroup>(b =>
        {
            b.HasOne(g => g.User).WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Property(g => g.Name).HasMaxLength(100);
            b.HasIndex(g => g.UserId);
        });
        builder.Entity<VipMembership>(b =>
        {
            b.HasKey(m => new { m.CharacterId, m.GroupId });
            b.HasOne(m => m.Character).WithMany(c => c.Memberships).HasForeignKey(m => m.CharacterId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(m => m.Group).WithMany(g => g.Memberships).HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        });
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
