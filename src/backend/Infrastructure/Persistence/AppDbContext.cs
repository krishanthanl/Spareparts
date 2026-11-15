using System.Linq.Expressions;
using Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class AppDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamp with time zone");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(AuditableEntity).IsAssignableFrom(type.ClrType))
            {
                modelBuilder.Entity(type.ClrType).Property(nameof(AuditableEntity.CreatedBy)).HasMaxLength(200).IsRequired();
                modelBuilder.Entity(type.ClrType).Property(nameof(AuditableEntity.UpdatedBy)).HasMaxLength(200).IsRequired();
            }
            if (type.BaseType is null && typeof(ISoftDeletable).IsAssignableFrom(type.ClrType))
            {
                var parameter = Expression.Parameter(type.ClrType, "entity");
                var filter = Expression.Lambda(Expression.Not(Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted))), parameter);
                modelBuilder.Entity(type.ClrType).HasQueryFilter(filter);
                modelBuilder.Entity(type.ClrType).Property(nameof(ISoftDeletable.DeletedBy)).HasMaxLength(200);
            }
        }
    }
}
