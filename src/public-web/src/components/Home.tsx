import { useState, type FormEvent } from 'react';
import { useCategories } from '../api/catalog';
import { shopContact } from '../lib/contact';
import { CategoryGrid, PartsGrid } from './PartsCatalog';

/** Shown only when the live catalogue can't be reached. */
const fallbackCategories = [
  { code: '01', name: 'Engine', detail: 'Gaskets, belts, mounts, sensors' },
  { code: '02', name: 'Brakes', detail: 'Pads, rotors, calipers, lines' },
  { code: '03', name: 'Suspension', detail: 'Shocks, struts, arms, bushes' },
  {
    code: '04',
    name: 'Electrical',
    detail: 'Batteries, alternators, starters',
  },
  { code: '05', name: 'Filters & service', detail: 'Oil, air, fuel, cabin' },
  { code: '06', name: 'Cooling', detail: 'Radiators, pumps, hoses, fans' },
  { code: '07', name: 'Exhaust', detail: 'Mufflers, pipes, clamps, O2' },
  { code: '08', name: 'Body & lighting', detail: 'Lamps, mirrors, panels' },
];

const scrollToParts = () =>
  document.getElementById('parts')?.scrollIntoView({ behavior: 'smooth' });

function PartSearch({ onSearch }: { onSearch: (q: string) => void }) {
  const [query, setQuery] = useState('');
  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    onSearch(query.trim().slice(0, 100));
    scrollToParts();
  };
  return (
    <section className="search-band" aria-labelledby="search-title">
      <h2 id="search-title">Find a part</h2>
      <form className="search-form" role="search" onSubmit={onSubmit}>
        <label htmlFor="part-search" className="visually-hidden">
          Search by part name, part number or brand
        </label>
        <input
          id="part-search"
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Part name, part no. or brand"
          autoComplete="off"
          maxLength={100}
        />
        <button type="submit">Search</button>
      </form>
      <p className="search-note">
        Search by part name, part number or brand. Not sure? Give us your rego
        and we’ll find it.
      </p>
    </section>
  );
}

export function Home() {
  const [q, setQ] = useState('');
  const [category, setCategory] = useState('');
  const categories = useCategories();
  const live = categories.data ?? [];
  const pick = (slug: string) => {
    setCategory(slug);
    scrollToParts();
  };
  return (
    <>
      <section className="hero" aria-labelledby="hero-title">
        <div className="hero-copy">
          <p className="eyebrow">NZ-wide parts supply · Workshops & DIY</p>
          <h1 id="hero-title">Vehicle spare parts</h1>
          <p className="hero-lead">
            The right part, first time. Stocked for Kiwi mechanics and car
            owners — from daily drivers to work utes.
          </p>
          <div className="hero-actions">
            <a className="btn btn-primary" href="#parts">
              Browse parts
            </a>
            <a className="btn btn-ghost" href="#contact">
              Talk to us
            </a>
          </div>
        </div>
        <ul className="hero-stats" aria-label="Why buy from us">
          <li>
            <strong>NZD</strong>
            <span>Prices in NZ dollars</span>
          </li>
          <li>
            <strong>0% GST</strong>
            <span>Not GST registered</span>
          </li>
          <li>
            <strong>Trade</strong>
            <span>Workshop accounts</span>
          </li>
        </ul>
      </section>

      <PartSearch onSearch={setQ} />

      <section
        id="categories"
        className="categories"
        aria-labelledby="categories-title"
      >
        <div className="section-head">
          <h2 id="categories-title">Product categories</h2>
          <p>Everything under the bonnet, and the bits around it.</p>
        </div>
        {live.length > 0 ? (
          <CategoryGrid categories={live} selected={category} onSelect={pick} />
        ) : (
          <ul className="category-grid">
            {fallbackCategories.map((c) => (
              <li key={c.code}>
                <div className="category-card">
                  <span className="category-code">{c.code}</span>
                  <h3>{c.name}</h3>
                  <p>{c.detail}</p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      <PartsGrid
        category={category}
        q={q}
        categories={live}
        onCategory={setCategory}
      />

      <section id="contact" className="contact" aria-labelledby="contact-title">
        <div className="section-head">
          <h2 id="contact-title">Contact the counter</h2>
          <p>Not sure what fits? Give us the rego and we’ll sort it.</p>
        </div>
        <dl className="contact-grid">
          <div>
            <dt>Phone</dt>
            <dd>{shopContact.phone}</dd>
          </div>
          <div>
            <dt>Email</dt>
            <dd>{shopContact.email}</dd>
          </div>
          <div>
            <dt>Counter hours</dt>
            <dd>{shopContact.hours}</dd>
          </div>
          <div>
            <dt>Location</dt>
            <dd>{shopContact.location}</dd>
          </div>
        </dl>
      </section>
    </>
  );
}
