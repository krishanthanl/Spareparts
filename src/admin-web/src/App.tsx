import { useState, type ReactNode } from 'react';
import { Link, Route, Routes } from 'react-router-dom';
import { SidebarMenu } from './components/SidebarMenu';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Paper from '@mui/material/Paper';
import Button from '@mui/material/Button';
import { ApiStatus } from './components/ApiStatus';
import { CategoriesPage } from './features/catalog/CategoriesPage';
import { ItemsPage } from './features/catalog/ItemsPage';
import { VehiclesPage } from './features/catalog/VehiclesPage';
import Dialog from '@mui/material/Dialog';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import { AuthProvider } from './features/auth/AuthProvider';
import { useAuth } from './features/auth/useAuth';
import { ChangePasswordForm } from './features/auth/ChangePasswordForm';
import { LoginPage } from './features/auth/LoginPage';
import { RolesPage } from './features/users/RolesPage';
import { UserPermissionsPage } from './features/users/UserPermissionsPage';
import { UsersPage } from './features/users/UsersPage';
import { GrnEditPage, GrnListPage } from './features/inventory/GrnPages';
import {
  StockDocumentEditPage,
  StockDocumentListPage,
} from './features/inventory/StockDocumentPages';
import {
  StocktakeEditPage,
  StocktakeListPage,
} from './features/inventory/StocktakePages';
import {
  LowStockPage,
  StockCardPage,
  StockReportPage,
} from './features/inventory/StockPages';
import { OrderListPage, OrderPage } from './features/sales/OrderPages';
import {
  CreditNotesPage,
  DeliverySettingsPage,
  RefundsPage,
  ReturnsPage,
  ShipmentsPage,
} from './features/sales/AfterSalePages';
import {
  CustomersPage,
  DailyTakingsPage,
  QuickSalePage,
} from './features/sales/SalesPages';

function ShopDetails() {
  return (
    <>
      <Typography variant="h4" component="h1">
        Shop details
      </Typography>
      <p>The working defaults for your New Zealand shop.</p>
      <Paper component="dl" className="shop-details" elevation={0}>
        <dt>Stock location</dt>
        <dd>One shop, one location</dd>
        <dt>Currency</dt>
        <dd>NZD</dd>
        <dt>Shop timezone</dt>
        <dd>Pacific/Auckland</dd>
        <dt>GST status</dt>
        <dd>Not GST registered</dd>
      </Paper>
    </>
  );
}
function Overview() {
  return (
    <>
      <Typography variant="h3" component="h1">
        Shop workspace
      </Typography>
      <p className="intro">A clear place to run your spare parts shop.</p>
      <Paper className="welcome-panel" elevation={0}>
        <Typography variant="h5" component="h2">
          Your workspace is taking shape
        </Typography>
        <p>
          Shop operations will be available here as each part of the system is
          completed.
        </p>
        <Button component={Link} to="/shop-details" variant="outlined">
          Review shop details
        </Button>
      </Paper>
      <div className="connection-panel">
        <Typography component="h2" variant="h6">
          Service connection
        </Typography>
        <ApiStatus />
      </div>
    </>
  );
}
function Forbidden() {
  return (
    <>
      <Typography variant="h4" component="h1">
        Not available
      </Typography>
      <p>Your account does not have permission to open this page.</p>
      <Link to="/">Return to workspace</Link>
    </>
  );
}

/** Hides a page the user may not use. The API enforces the same permission on every call. */
function RequirePermission({
  permission,
  children,
}: {
  permission: string;
  children: ReactNode;
}) {
  const { can } = useAuth();
  return can(permission) ? children : <Forbidden />;
}

function Workspace() {
  const { state, logout } = useAuth();
  const [changing, setChanging] = useState(false);
  if (state.status !== 'signedIn') return null;
  const { user, menus } = state;
  return (
    <Box className="admin-shell">
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <Box component="aside" className="sidebar">
        <Link to="/" className="wordmark">
          <span className="brand-mark" aria-hidden="true">
            S
          </span>
          Spareparts
        </Link>
        <p className="sidebar-description">Shop administration</p>
        <SidebarMenu menus={menus} />
        <p className="sidebar-foot">New Zealand shop</p>
      </Box>
      <Box className="admin-content">
        <header className="topbar">
          <span>
            {user.displayName}
            {user.roles.length > 0 && (
              <span className="muted"> · {user.roles.join(', ')}</span>
            )}
          </span>
          <span className="topbar-actions">
            <Button size="small" onClick={() => setChanging(true)}>
              Change password
            </Button>
            <Button size="small" onClick={() => void logout()}>
              Sign out
            </Button>
          </span>
        </header>
        <Box component="main" id="main" className="main-content" tabIndex={-1}>
          <Routes>
            <Route
              path="/"
              element={
                <RequirePermission permission="dashboard.view">
                  <Overview />
                </RequirePermission>
              }
            />
            <Route
              path="/items"
              element={
                <RequirePermission permission="catalog.manage">
                  <ItemsPage />
                </RequirePermission>
              }
            />
            <Route
              path="/categories"
              element={
                <RequirePermission permission="catalog.manage">
                  <CategoriesPage />
                </RequirePermission>
              }
            />
            <Route
              path="/vehicles"
              element={
                <RequirePermission permission="catalog.manage">
                  <VehiclesPage />
                </RequirePermission>
              }
            />
            <Route
              path="/users"
              element={
                <RequirePermission permission="users.view">
                  <UsersPage />
                </RequirePermission>
              }
            />
            <Route
              path="/users/:id/permissions"
              element={
                <RequirePermission permission="permissions.manage">
                  <UserPermissionsPage />
                </RequirePermission>
              }
            />
            <Route
              path="/roles"
              element={
                <RequirePermission permission="roles.manage">
                  <RolesPage />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/grn"
              element={
                <RequirePermission permission="inventory.grn">
                  <GrnListPage />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/grn/:id"
              element={
                <RequirePermission permission="inventory.grn">
                  <GrnEditPage />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/adjustments"
              element={
                <RequirePermission permission="inventory.adjust">
                  <StockDocumentListPage kind="Adjustment" />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/adjustments/:id"
              element={
                <RequirePermission permission="inventory.adjust">
                  <StockDocumentEditPage kind="Adjustment" />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/opening"
              element={
                <RequirePermission permission="inventory.adjust">
                  <StockDocumentListPage kind="Opening" />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/opening/:id"
              element={
                <RequirePermission permission="inventory.adjust">
                  <StockDocumentEditPage kind="Opening" />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/stocktakes"
              element={
                <RequirePermission permission="inventory.adjust">
                  <StocktakeListPage />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/stocktakes/:id"
              element={
                <RequirePermission permission="inventory.adjust">
                  <StocktakeEditPage />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/stock"
              element={
                <RequirePermission permission="inventory.reports">
                  <StockReportPage />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/low-stock"
              element={
                <RequirePermission permission="inventory.reports">
                  <LowStockPage />
                </RequirePermission>
              }
            />
            <Route
              path="/inventory/items/:id"
              element={
                <RequirePermission permission="inventory.reports">
                  <StockCardPage />
                </RequirePermission>
              }
            />
            <Route
              path="/sales/invoices"
              element={
                <RequirePermission permission="sales.invoice">
                  <OrderListPage />
                </RequirePermission>
              }
            />
            <Route
              path="/sales/invoices/:id"
              element={
                <RequirePermission permission="sales.invoice">
                  <OrderPage />
                </RequirePermission>
              }
            />
            <Route
              path="/sales/quick-sale"
              element={
                <RequirePermission permission="payments.record">
                  <QuickSalePage />
                </RequirePermission>
              }
            />
            <Route
              path="/sales/customers"
              element={
                <RequirePermission permission="sales.invoice">
                  <CustomersPage />
                </RequirePermission>
              }
            />
            <Route
              path="/sales/takings"
              element={
                <RequirePermission permission="sales.reports">
                  <DailyTakingsPage />
                </RequirePermission>
              }
            />
            <Route
              path="/sales/returns"
              element={
                <RequirePermission permission="sales.return">
                  <ReturnsPage />
                </RequirePermission>
              }
            />
            <Route
              path="/sales/credit-notes"
              element={
                <RequirePermission permission="sales.invoice">
                  <CreditNotesPage />
                </RequirePermission>
              }
            />
            <Route
              path="/sales/refunds"
              element={
                <RequirePermission permission="refunds.create">
                  <RefundsPage />
                </RequirePermission>
              }
            />
            <Route
              path="/sales/shipments"
              element={
                <RequirePermission permission="fulfillment.update">
                  <ShipmentsPage />
                </RequirePermission>
              }
            />
            <Route
              path="/sales/delivery"
              element={
                <RequirePermission permission="delivery.configure">
                  <DeliverySettingsPage />
                </RequirePermission>
              }
            />
            <Route
              path="/shop-details"
              element={
                <RequirePermission permission="dashboard.view">
                  <ShopDetails />
                </RequirePermission>
              }
            />
            <Route
              path="*"
              element={
                <>
                  <h1>Page not found</h1>
                  <Link to="/">Return to workspace</Link>
                </>
              }
            />
          </Routes>
        </Box>
        <footer className="admin-footer">One shop. One stock location.</footer>
      </Box>
      <Dialog
        open={changing}
        onClose={() => setChanging(false)}
        fullWidth
        maxWidth="xs"
      >
        <DialogTitle>Change password</DialogTitle>
        <DialogContent>
          <p>Your other signed-in devices will be signed out.</p>
          <ChangePasswordForm onDone={() => setChanging(false)} />
        </DialogContent>
      </Dialog>
    </Box>
  );
}

function ForcedPasswordChange() {
  const { logout } = useAuth();
  return (
    <main className="login-page" id="main">
      <Paper className="login-card">
        <Typography variant="h4" component="h1">
          Choose a new password
        </Typography>
        <p>
          Your password was set by an administrator. Choose your own before you
          continue.
        </p>
        <ChangePasswordForm />
        <Button sx={{ mt: 1 }} onClick={() => void logout()}>
          Sign out
        </Button>
      </Paper>
    </main>
  );
}

function Gate() {
  const { state } = useAuth();
  if (state.status === 'loading')
    return (
      <p className="login-page" role="status">
        Loading…
      </p>
    );
  if (state.status === 'signedOut') return <LoginPage />;
  if (state.user.mustChangePassword) return <ForcedPasswordChange />;
  return <Workspace />;
}

export function App() {
  return (
    <AuthProvider>
      <Gate />
    </AuthProvider>
  );
}
