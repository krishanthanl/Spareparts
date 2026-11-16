import { Link, NavLink, Route, Routes } from 'react-router-dom';
import { Home } from './components/Home';
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
          <span className="wordmark-mark" aria-hidden="true" />
          <span className="wordmark-text">
            Spareparts<small>New Zealand</small>
          </span>
        </Link>
        <nav aria-label="Main navigation">
          <NavLink end to="/">
            Home
          </NavLink>
          <a href="/#categories">Parts</a>
          <a href="/#contact">Contact</a>
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
