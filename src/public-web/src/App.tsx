import { lazy, Suspense, useEffect, useState } from 'react';
import { Link, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import { Home } from './components/Home';
import { useCart } from './lib/cart';

const CatalogPage = lazy(() =>
  import('./components/CatalogPage').then((m) => ({ default: m.CatalogPage })),
);
const PartPage = lazy(() =>
  import('./components/PartPage').then((m) => ({ default: m.PartPage })),
);
const CartPage = lazy(() =>
  import('./components/CartPage').then((m) => ({ default: m.CartPage })),
);
const CartDrawer = lazy(() =>
  import('./components/CartPage').then((m) => ({ default: m.CartDrawer })),
);
const DeliveryReturns = lazy(() =>
  import('./components/DeliveryReturns').then((m) => ({
    default: m.DeliveryReturns,
  })),
);

function StoreMetadata() {
  const { pathname, hash } = useLocation();
  useEffect(() => {
    if (hash === '#contact')
      document.getElementById('contact')?.scrollIntoView();
  }, [pathname, hash]);
  const detail = /^\/parts\/[^/]+$/.test(pathname);
  const title =
    pathname === '/parts'
      ? 'Browse parts'
      : pathname === '/cart'
        ? 'Your cart'
        : pathname === '/shop-details'
          ? 'Shop details'
          : pathname === '/delivery-and-returns'
            ? 'Delivery and returns'
            : pathname === '/'
              ? 'Vehicle spare parts'
              : 'Page not found';
  return (
    <>
      {!detail && <title>{`${title} | Spareparts NZ`}</title>}
      {!detail && (
        <meta
          name="description"
          content="Browse vehicle spare parts by make, model and year at Spareparts NZ."
        />
      )}
      {pathname !== '/cart' && (
        <link
          rel="canonical"
          href={new URL(pathname, window.location.origin).href}
        />
      )}
      {pathname === '/cart' && <meta name="robots" content="noindex" />}
    </>
  );
}
function ShopDetails() {
  return (
    <section className="details">
      <h1>Shop details</h1>
      <p>Vehicle spare parts from one New Zealand shop.</p>
      <dl>
        <dt>Shop currency</dt>
        <dd>NZD</dd>
        <dt>Shop timezone</dt>
        <dd>Pacific/Auckland</dd>
        <dt>GST status</dt>
        <dd>Not GST registered</dd>
      </dl>
      <p>No GST is charged.</p>
      <Link to="/">Back to home</Link>
    </section>
  );
}
export function App() {
  const [cartOpen, setCartOpen] = useState(false);
  const { key } = useLocation();
  const [drawerKey, setDrawerKey] = useState(key);
  const quantity = useCart((state) =>
    state.lines.reduce((sum, line) => sum + line.quantity, 0),
  );
  return (
    <div className="public-shell">
      <StoreMetadata />
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="site-header">
        <Link className="wordmark" to="/">
          <span className="wordmark-mark" aria-hidden="true" />
          <span className="wordmark-text">
            Spareparts<small>New Zealand</small>
          </span>
        </Link>
        <nav aria-label="Main navigation">
          <NavLink end to="/">
            Home
          </NavLink>
          <NavLink to="/parts">Parts</NavLink>
          <a href="/#contact">Contact</a>
          <NavLink to="/shop-details">Shop details</NavLink>
          <button
            className="cart-toggle"
            type="button"
            onClick={() => {
              setDrawerKey(key);
              setCartOpen(true);
            }}
          >
            Cart ({quantity})
          </button>
        </nav>
      </header>
      <main id="main" tabIndex={-1}>
        <Suspense
          fallback={
            <p className="store-section" role="status">
              Loading…
            </p>
          }
        >
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/parts" element={<CatalogPage />} />
            <Route path="/parts/:id" element={<PartPage />} />
            <Route path="/cart" element={<CartPage />} />
            <Route path="/delivery-and-returns" element={<DeliveryReturns />} />
            <Route path="/shop-details" element={<ShopDetails />} />
            <Route
              path="*"
              element={
                <section className="details">
                  <h1>Page not found</h1>
                  <Link to="/">Back to home</Link>
                </section>
              }
            />
          </Routes>
        </Suspense>
      </main>
      {cartOpen && drawerKey === key && (
        <Suspense fallback={null}>
          <CartDrawer close={() => setCartOpen(false)} />
        </Suspense>
      )}
      <footer>
        <span>Vehicle spare parts</span>
        <span>Prices in NZD. No GST charged.</span>
        <Link to="/delivery-and-returns">Delivery and returns</Link>
      </footer>
    </div>
  );
}
