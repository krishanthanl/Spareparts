using Application.Catalog;
using Application.Common;
using Application.Identity;
using Domain.Identity;
using Infrastructure.Catalog;
using Infrastructure.Identity;
using Infrastructure.Imaging;
using Infrastructure.Inventory;
using Application.Inventory;
using Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<IGrnService, GrnService>();
        services.AddScoped<IStockDocumentService, StockDocumentService>();
        services.AddScoped<IStocktakeService, StocktakeService>();
        services.AddScoped<IStockQueryService, StockQueryService>();
        services.AddScoped<IPriceService, PriceService>();
        services.AddScoped<Application.Sales.ICustomerService, Sales.CustomerService>();
        services.AddScoped<Application.Sales.ISalesOrderService, Sales.SalesOrderService>();
        services.AddScoped<Application.Sales.ITakingsReportService, Sales.TakingsReportService>();
        services.AddScoped<Application.Sales.IInvoicePdfService, Sales.InvoicePdfRenderer>();
        services.TryAddSingleton(new Application.Sales.ShopSettings());
        return services;
    }

    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, SessionSettings settings)
    {
        services.AddSingleton(settings);
        services.AddMemoryCache();
        services.AddIdentityCore<User>(options => ConfigureIdentity(options, settings)).AddUserStore<UserStore>();
        services.AddScoped<IAccessService, AccessService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IdentitySeeder>();
        return services;
    }

    public static void ConfigureIdentity(Microsoft.AspNetCore.Identity.IdentityOptions options, SessionSettings settings)
    {
        options.User.RequireUniqueEmail = true;
        options.User.AllowedUserNameCharacters = "";
        options.Password.RequiredLength = PasswordRules.MinLength;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = settings.MaxFailedAttempts;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(settings.LockoutMinutes);
    }
}
