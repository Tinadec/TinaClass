using Microsoft.EntityFrameworkCore;

namespace TinaClass.Api.Common.Persistence;

/// <summary>
/// An entity with an application-level concurrency token: an integer the DbContext raises by one on every update, so that a
/// write based on stale data fails (409) on every database alike. Not <c>rowversion</c> (SQL Server) or <c>xmin</c> (PostgreSQL):
/// those are provider-specific and the shared model must not be.
/// </summary>
public interface IVersioned
{
    int Version { get; set; }
}

/// <summary>
/// The base of every feature's DbContext. A feature describes its model once in <see cref="ConfigureModel"/> (provider neutral:
/// explicit maximum length on every string, a table prefix, no provider-specific column types or SQL); one subclass per provider
/// (<c>Data/Sqlite</c>, <c>Data/Postgres</c>, <c>Data/SqlServer</c>) adds only what that provider needs and owns its migrations.
/// </summary>
public abstract class FeatureDbContext(DbContextOptions options) : DbContext(options)
{
    /// <summary>Entities, tables (with the feature's prefix), keys, indexes and maximum lengths.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(t => typeof(IVersioned).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entity.ClrType).Property(nameof(IVersioned.Version)).IsConcurrencyToken();
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RaiseVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RaiseVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// The update is sent as <c>WHERE Version = &lt;the value read&gt;</c> with the new value; zero rows changed means someone else
    /// got there first (<see cref="DbUpdateConcurrencyException"/>). <c>ExecuteUpdate</c> and <c>ExecuteDelete</c> skip this: use them
    /// only where the last write may win.
    /// </summary>
    private void RaiseVersions()
    {
        foreach (var entry in ChangeTracker.Entries<IVersioned>().Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.Version = entry.Property(e => e.Version).OriginalValue + 1;
        }
    }
}

public static class VersionExtensions
{
    /// <summary>
    /// Makes the next update of <paramref name="entity"/> fail with a concurrency conflict (409) unless the caller's copy is current:
    /// the update is sent as <c>WHERE Version = <paramref name="version"/></c>.
    /// </summary>
    public static void ExpectVersion<T>(this DbContext db, T entity, int version) where T : class, IVersioned =>
        db.Entry(entity).Property(e => e.Version).OriginalValue = version;
}
