using Application.Catalog;
using Application.Common;
using Infrastructure.Catalog;
using Infrastructure.Imaging;
using Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, StorageSettings storage)
    {
        services.AddSingleton(storage);
        services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IVehicleService, VehicleService>();
        services.AddScoped<IPublicCatalogService, PublicCatalogService>();
        return services;
    }
}
