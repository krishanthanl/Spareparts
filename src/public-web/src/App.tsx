import { Link, NavLink, Route, Routes } from 'react-router-dom';
function Home() {
  return (
    <section className="welcome">
      <div className="welcome-copy">
        <h1>Vehicle spare parts</h1>
        <p className="lead">The right part keeps you moving.</p>
        <p>
          Our online shop is being prepared. Parts browsing and online orders
          will be available when the shop opens.
        </p>
        <Link className="primary-link" to="/shop-details">
          Shop details
        </Link>
      </div>
      <div className="part-illustration" aria-hidden="true">
        <div className="part-disc">
          <div className="part-hub" />
        </div>
        <span>Spareparts</span>
      </div>
    </section>
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
  return (
    <div className="public-shell">
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="site-header">
        <Link className="wordmark" to="/">
          Spareparts<span>New Zealand</span>
        </Link>
        <nav aria-label="Main navigation">
          <NavLink end to="/">
            Home
          </NavLink>
          <NavLink to="/shop-details">Shop details</NavLink>
        </nav>
      </header>
      <main id="main" tabIndex={-1}>
        <Routes>
          <Route path="/" element={<Home />} />
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
      </main>
      <footer>
        <span>Vehicle spare parts</span>
        <span>Prices in NZD. No GST charged.</span>
      </footer>
    </div>
  );
}
