using Application.Identity;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using P = Application.Identity.PermissionKeys;

namespace Infrastructure.Identity;

/// <summary>
/// Seeds system roles (with default permissions, only when the role is first created) and keeps the
/// admin menu in step with code. Never creates users: the first SuperAdmin comes from tools/AdminBootstrap.
/// Safe to run repeatedly and from several instances.
/// </summary>
public sealed class IdentitySeeder(AppDbContext db, TimeProvider clock)
{
    private sealed record RoleSeed(string Name, string Description, string[] Permissions);
    private sealed record MenuSeed(string Key, string? Parent, string Title, string? Path, string? Icon, int Sort, string? Permission, bool Enabled = true);

    private static readonly RoleSeed[] RoleSeeds =
    [
        new(Role.SuperAdmin, "Full access. Can manage other SuperAdmins.", []),
        new(Role.Admin, "Runs the shop system. Everything except managing SuperAdmins.", P.All.Select(x => x.Key).ToArray()),
        new(Role.StoreKeeper, "Catalogue and stock.", [P.DashboardView, P.CatalogManage, P.InventoryGrn, P.InventoryAdjust, P.InventoryReports, P.FulfillmentUpdate]),
        new(Role.Cashier, "Sales and returns.", [P.DashboardView, P.SalesInvoice, P.SalesReturn, P.RefundsCreate]),
    ];

    // Parents come before children. Menus for later phases are seeded switched off.
    private static readonly MenuSeed[] MenuSeeds =
    [
        new("dashboard", null, "Workspace", "/", "dashboard", 10, P.DashboardView),
        new("catalog", null, "Catalogue", null, "inventory_2", 20, null),
        new("catalog.items", "catalog", "Items", "/items", null, 10, P.CatalogManage),
        new("catalog.categories", "catalog", "Categories", "/categories", null, 20, P.CatalogManage),
        new("catalog.vehicles", "catalog", "Vehicles", "/vehicles", null, 30, P.CatalogManage),
        new("inventory", null, "Inventory", null, "warehouse", 30, null),
        new("inventory.grn", "inventory", "Goods received", "/inventory/grn", null, 10, P.InventoryGrn),
        new("inventory.adjustments", "inventory", "Adjustments", "/inventory/adjustments", null, 20, P.InventoryAdjust),
        new("inventory.opening", "inventory", "Opening stock", "/inventory/opening", null, 30, P.InventoryAdjust),
        new("inventory.stocktakes", "inventory", "Stocktake", "/inventory/stocktakes", null, 40, P.InventoryAdjust),
        new("inventory.stock", "inventory", "Stock report", "/inventory/stock", null, 50, P.InventoryReports),
        new("inventory.low", "inventory", "Low stock", "/inventory/low-stock", null, 60, P.InventoryReports),
        new("sales", null, "Sales", null, "point_of_sale", 40, null, Enabled: false),
        new("sales.invoices", "sales", "Invoices", "/sales/invoices", null, 10, P.SalesInvoice, Enabled: false),
        new("sales.returns", "sales", "Returns", "/sales/returns", null, 20, P.SalesReturn, Enabled: false),
        new("admin", null, "Administration", null, "admin_panel_settings", 90, null),
        new("admin.users", "admin", "Users", "/users", null, 10, P.UsersView),
        new("admin.roles", "admin", "Roles", "/roles", null, 20, P.RolesManage),
        new("shop", null, "Shop details", "/shop-details", "storefront", 100, P.DashboardView),
    ];

    public static IReadOnlyList<string> DefaultPermissions(string role) => RoleSeeds.Single(x => x.Name == role).Permissions;

    public async Task SeedAsync(CancellationToken ct)
    {
        var existingRoles = await db.Roles.Select(x => x.NormalizedName).ToListAsync(ct);
        foreach (var seed in RoleSeeds.Where(x => !existingRoles.Contains(x.Name.ToUpperInvariant())))
        {
            var role = new Role { Name = seed.Name, NormalizedName = seed.Name.ToUpperInvariant(), Description = seed.Description, IsSystem = true };
            db.Roles.Add(role);
            foreach (var key in seed.Permissions) db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = key });
        }

        // Keys added in later phases reach existing system roles once. A key an admin later removes is not re-added.
        var seeded = (await db.PermissionSeeds.Select(x => x.PermissionKey).ToListAsync(ct)).ToHashSet();
        var newKeys = PermissionKeys.All.Select(x => x.Key).Where(k => !seeded.Contains(k)).ToList();
        if (newKeys.Count > 0)
        {
            var roles = await db.Roles.Where(r => r.IsSystem).Include(r => r.Permissions).ToListAsync(ct);
            foreach (var role in roles)
            {
                var defaults = RoleSeeds.SingleOrDefault(s => s.Name.ToUpperInvariant() == role.NormalizedName)?.Permissions ?? [];
                foreach (var key in newKeys.Where(k => defaults.Contains(k) && role.Permissions.All(p => p.PermissionKey != k)))
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = key });
            }
            foreach (var key in newKeys) db.PermissionSeeds.Add(new PermissionSeed { PermissionKey = key });
        }

        var menus = await db.Menus.ToDictionaryAsync(x => x.Key, ct);
        foreach (var seed in MenuSeeds)
        {
            if (!menus.TryGetValue(seed.Key, out var menu))
            {
                menu = new Menu { Key = seed.Key };
                db.Menus.Add(menu);
                menus[seed.Key] = menu;
            }
            menu.ParentId = seed.Parent is null ? null : menus[seed.Parent].Id;
            menu.Title = seed.Title;
            menu.Path = seed.Path;
            menu.Icon = seed.Icon;
            menu.SortOrder = seed.Sort;
            menu.RequiredPermission = seed.Permission;
            menu.IsEnabled = seed.Enabled;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes refresh tokens whose sign-in ended more than 60 days ago.</summary>
    public Task<int> PurgeExpiredTokensAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddDays(-60);
        return db.RefreshTokens.Where(x => x.FamilyExpiresAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
