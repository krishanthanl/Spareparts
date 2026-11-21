namespace Application.Identity;

/// <summary>
/// Stable permission keys. Each one is also the name of an authorization policy.
/// Keys for later phases exist now so roles can be set up once (ADR 0003).
/// </summary>
public static class PermissionKeys
{
    public const string DashboardView = "dashboard.view";
    public const string CatalogManage = "catalog.manage";
    public const string CatalogDeletedView = "catalog.deleted.view";
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string PermissionsManage = "permissions.manage";
    public const string InventoryGrn = "inventory.grn";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventoryReports = "inventory.reports";
    public const string InventoryPrice = "inventory.price";
    public const string InventoryReverse = "inventory.reverse";
    public const string SalesInvoice = "sales.invoice";
    public const string SalesDiscount = "sales.discount";
    public const string SalesCancel = "sales.cancel";
    public const string SalesIssue = "sales.issue";
    public const string SalesReports = "sales.reports";
    public const string PaymentsRecord = "payments.record";
    public const string PaymentsVoid = "payments.void";
    public const string CustomersManage = "customers.manage";
    public const string SalesReturn = "sales.return";
    public const string DeliveryConfigure = "delivery.configure";
    public const string FulfillmentUpdate = "fulfillment.update";
    public const string RefundsCreate = "refunds.create";
    public const string RefundsApprove = "refunds.approve";
    public const string RefundsExecute = "refunds.execute";
    public const string CreditNotesIssue = "creditnotes.issue";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(DashboardView, "General", "Open the admin workspace"),
        new(CatalogManage, "Catalogue", "Manage items, categories and vehicles"),
        new(CatalogDeletedView, "Catalogue", "See and restore deleted catalogue records"),
        new(UsersView, "Administration", "See staff users"),
        new(UsersManage, "Administration", "Create, edit, deactivate and reset staff users"),
        new(RolesManage, "Administration", "Manage roles and their permissions"),
        new(PermissionsManage, "Administration", "Set per-user menu and permission overrides"),
        new(InventoryGrn, "Inventory", "Receive stock (GRN)"),
        new(InventoryAdjust, "Inventory", "Adjust stock"),
        new(InventoryReports, "Inventory", "View stock reports"),
        new(InventoryPrice, "Inventory", "Change selling prices"),
        new(InventoryReverse, "Inventory", "Reverse posted goods received"),
        new(SalesInvoice, "Sales", "Create invoices"),
        new(SalesDiscount, "Sales", "Give discounts or sell below the selling price"),
        new(SalesCancel, "Sales", "Cancel confirmed invoices"),
        new(SalesIssue, "Sales", "Issue stock for orders"),
        new(SalesReports, "Sales", "View daily takings"),
        new(PaymentsRecord, "Sales", "Record payments"),
        new(PaymentsVoid, "Sales", "Void recorded payments"),
        new(CustomersManage, "Sales", "Add and edit customers"),
        new(SalesReturn, "Sales", "Process returns"),
        new(DeliveryConfigure, "Delivery", "Configure delivery options"),
        new(FulfillmentUpdate, "Delivery", "Update order fulfilment"),
        new(RefundsCreate, "Refunds", "Create refund requests"),
        new(RefundsApprove, "Refunds", "Approve refunds"),
        new(RefundsExecute, "Refunds", "Pay out approved refunds"),
        new(CreditNotesIssue, "Sales", "Issue credit notes without a return (price adjustments, delivery)"),
    ];

    private static readonly HashSet<string> Known = All.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
    public static bool IsKnown(string key) => Known.Contains(key);
}

public sealed record PermissionDefinition(string Key, string Group, string Description);
