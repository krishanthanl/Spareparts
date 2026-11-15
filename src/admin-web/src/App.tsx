import { Link, NavLink, Route, Routes } from 'react-router-dom';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Paper from '@mui/material/Paper';
import Button from '@mui/material/Button';
import { ApiStatus } from './components/ApiStatus';

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
export function App() {
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
        <nav aria-label="Main navigation">
          <NavLink end to="/">
            Workspace
          </NavLink>
          <NavLink to="/shop-details">Shop details</NavLink>
        </nav>
        <p className="sidebar-foot">New Zealand shop</p>
      </Box>
      <Box className="admin-content">
        <header className="topbar">
          <span>Shop administration</span>
          <span>NZD</span>
        </header>
        <Box component="main" id="main" className="main-content" tabIndex={-1}>
          <Routes>
            <Route path="/" element={<Overview />} />
            <Route path="/shop-details" element={<ShopDetails />} />
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
    </Box>
  );
}
