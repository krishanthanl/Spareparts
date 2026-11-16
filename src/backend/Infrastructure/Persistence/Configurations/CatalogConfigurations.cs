using Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ImageKey).HasMaxLength(300);
        builder.Property(x => x.ThumbnailKey).HasMaxLength(300);
        builder.Property(x => x.Version).IsRowVersion();
        // Slug is the lower-case form of the name, so this also makes names unique ignoring case.
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items", t =>
        {
            t.HasCheckConstraint("ck_items_reorder_level", "\"ReorderLevel\" IS NULL OR \"ReorderLevel\" >= 0");
            t.HasCheckConstraint("ck_items_reorder_quantity", "\"ReorderQuantity\" IS NULL OR \"ReorderQuantity\" > 0");
            t.HasCheckConstraint("ck_items_sku_normalized", "\"Sku\" = upper(btrim(\"Sku\")) AND \"Sku\" <> ''");
        });
        builder.Property(x => x.Sku).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.PartBrand).HasMaxLength(100);
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasOne(x => x.Category).WithMany(x => x.Items).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Fitments).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Images).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.Sku).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(x => x.CategoryId);
    }
}

public sealed class ItemImageConfiguration : IEntityTypeConfiguration<ItemImage>
{
    public void Configure(EntityTypeBuilder<ItemImage> builder)
    {
        builder.ToTable("item_images", t => t.HasCheckConstraint("ck_item_images_size", "\"ByteSize\" > 0 AND \"Width\" > 0 AND \"Height\" > 0"));
        builder.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        builder.Property(x => x.ThumbnailKey).HasMaxLength(300).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AltText).HasMaxLength(200);
        builder.HasIndex(x => x.ItemId).IsUnique().HasFilter("\"IsPrimary\"").HasDatabaseName("ux_item_images_one_primary");
        builder.HasIndex(x => new { x.ItemId, x.SortOrder });
    }
}

public sealed class ItemFitmentConfiguration : IEntityTypeConfiguration<ItemFitment>
{
    public void Configure(EntityTypeBuilder<ItemFitment> builder)
    {
        builder.ToTable("item_fitments", t =>
        {
            t.HasCheckConstraint("ck_item_fitments_year_order", "\"YearFrom\" IS NULL OR \"YearTo\" IS NULL OR \"YearFrom\" <= \"YearTo\"");
            t.HasCheckConstraint("ck_item_fitments_year_range", "(\"YearFrom\" IS NULL OR \"YearFrom\" >= 1950) AND (\"YearTo\" IS NULL OR \"YearTo\" >= 1950)");
        });
        builder.HasOne(x => x.VehicleModel).WithMany().HasForeignKey(x => x.VehicleModelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ItemId, x.VehicleModelId, x.YearFrom, x.YearTo }).IsUnique().AreNullsDistinct(false);
        builder.HasIndex(x => new { x.VehicleModelId, x.YearFrom, x.YearTo });
    }
}

public sealed class VehicleMakeConfiguration : IEntityTypeConfiguration<VehicleMake>
{
    public void Configure(EntityTypeBuilder<VehicleMake> builder)
    {
        builder.ToTable("vehicle_makes");
        builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
        builder.HasMany(x => x.Models).WithOne(x => x.Make).HasForeignKey(x => x.MakeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VehicleModelConfiguration : IEntityTypeConfiguration<VehicleModel>
{
    public void Configure(EntityTypeBuilder<VehicleModel> builder)
    {
        builder.ToTable("vehicle_models");
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.HasIndex(x => x.MakeId);
    }
}
