using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using backend.Models;

namespace backend.Data;

/// <summary>
/// Time-zone convention for this database:
///   - TimeEntry.ClockIn / ClockOut  — stored as Zurich-local (CET/CEST), no UTC offset.
///   - RefreshToken.CreatedAt / ExpiresAt / RevokedAt — stored as UTC (DateTime.UtcNow).
/// Never convert ClockIn/ClockOut to UTC; the intended semantics are wall-clock Zurich time.
/// </summary>
public class AppDbContext : DbContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor? httpContextAccessor = null)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<TimeEntryAuditLog> TimeEntryAuditLogs => Set<TimeEntryAuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).HasMaxLength(100);
            entity.Property(u => u.FullName).HasMaxLength(200);
            entity.Property(u => u.Role).HasMaxLength(20);
            entity.Property(u => u.HourlyRate).HasColumnType("decimal(8,2)");
        });

        modelBuilder.Entity<TimeEntry>(entity =>
        {
            entity.HasOne(t => t.User)
                  .WithMany(u => u.TimeEntries)
                  .HasForeignKey(t => t.UserId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.Property(t => t.RowVersion).IsRowVersion();

            // Covering index for date-range queries
            entity.HasIndex(t => new { t.UserId, t.ClockIn })
                  .IncludeProperties(t => new { t.ClockOut, t.DurationMinutes, t.Notes });

            // Prevent duplicate open entries (race condition guard)
            entity.HasIndex(t => t.UserId)
                  .HasFilter("[ClockOut] IS NULL")
                  .IsUnique()
                  .HasDatabaseName("IX_TimeEntries_OpenEntry");
        });

        // #17: Refresh tokens table
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasOne(r => r.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => r.Token).IsUnique();
            entity.HasIndex(r => r.UserId);
            entity.Property(r => r.Token).HasMaxLength(128);
            entity.Property(r => r.ReplacedByToken).HasMaxLength(128);
        });

        // #10: Audit log table
        modelBuilder.Entity<TimeEntryAuditLog>(entity =>
        {
            entity.HasOne(a => a.TimeEntry)
                  .WithMany()
                  .HasForeignKey(a => a.TimeEntryId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.ChangedByUser)
                  .WithMany()
                  .HasForeignKey(a => a.ChangedByUserId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(a => a.TimeEntryId);
            entity.Property(a => a.FieldName).HasMaxLength(50);
            entity.Property(a => a.OldValue).HasMaxLength(500);
            entity.Property(a => a.NewValue).HasMaxLength(500);
        });
    }

    // #10: Auto-audit TimeEntry modifications using ChangeTracker
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var auditLogs = BuildAuditLogs();
        if (auditLogs.Count > 0)
            TimeEntryAuditLogs.AddRange(auditLogs);

        return await base.SaveChangesAsync(cancellationToken);
    }

    private List<TimeEntryAuditLog> BuildAuditLogs()
    {
        // Resolve the current user from the HTTP context (null-safe: seed, auto-close, tests)
        var userIdClaim = _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var changedByUserId))
            return []; // No HTTP context — skip audit (seeding, background tasks, tests)

        var logs = new List<TimeEntryAuditLog>();
        var now = DateTime.UtcNow;

        foreach (EntityEntry<TimeEntry> entry in ChangeTracker.Entries<TimeEntry>()
                     .Where(e => e.State == EntityState.Modified))
        {
            foreach (var prop in entry.Properties.Where(p => p.IsModified))
            {
                var fieldName = prop.Metadata.Name;
                // Only audit meaningful user-visible fields; skip RowVersion and IsManuallyEdited (noise)
                if (fieldName is not ("ClockIn" or "ClockOut" or "Notes" or "DurationMinutes"))
                    continue;

                var oldVal = prop.OriginalValue?.ToString();
                var newVal = prop.CurrentValue?.ToString();

                if (oldVal == newVal) continue;

                logs.Add(new TimeEntryAuditLog
                {
                    TimeEntryId = entry.Entity.Id,
                    ChangedByUserId = changedByUserId,
                    ChangedAt = now,
                    FieldName = fieldName,
                    OldValue = oldVal,
                    NewValue = newVal
                });
            }
        }

        return logs;
    }
}
