using Application.Common;
using Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Infrastructure.Persistence;

public sealed class AuditSaveChangesInterceptor(ICurrentActor actor, TimeProvider clock) : SaveChangesInterceptor
{
    public void ApplyAudit(DbContext context)
    {
        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow().ToUniversalTime();
        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable deleted)
            {
                entry.State = EntityState.Modified;
                deleted.IsDeleted = true;
                deleted.DeletedAt = now;
                deleted.DeletedBy = actor.ActorId;
            }
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = actor.ActorId;
            }
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = actor.ActorId;
                if (entry.State == EntityState.Modified)
                {
                    entry.Property(x => x.CreatedAt).IsModified = false;
                    entry.Property(x => x.CreatedBy).IsModified = false;
                }
            }
        }
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) ApplyAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) ApplyAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
