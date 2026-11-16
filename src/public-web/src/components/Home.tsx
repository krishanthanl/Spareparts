import { useState, type FormEvent } from 'react';
import { shopContact } from '../lib/contact';

const categories = [
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

function PartSearch() {
  const [query, setQuery] = useState('');
  const [submitted, setSubmitted] = useState<string | null>(null);
  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    setSubmitted(query.trim());
  };
  return (
    <section className="search-band" aria-labelledby="search-title">
      <h2 id="search-title">Find a part</h2>
      <form className="search-form" role="search" onSubmit={onSubmit}>
        <label htmlFor="part-search" className="visually-hidden">
          Search by rego plate, part number or keyword
        </label>
        <input
          id="part-search"
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Rego, part no. or keyword"
          autoComplete="off"
        />
        <button type="submit">Search</button>
      </form>
      <p className="search-note" role="status">
        {submitted
          ? `Online search opens soon. Call or email us about “${submitted}” and we’ll find it.`
          : 'Search by NZ rego plate, OEM part number or keyword.'}
      </p>
    </section>
  );
}

export function Home() {
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
            <a className="btn btn-primary" href="#categories">
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

      <PartSearch />

      <section
        id="categories"
        className="categories"
        aria-labelledby="categories-title"
      >
        <div className="section-head">
          <h2 id="categories-title">Product categories</h2>
          <p>Everything under the bonnet, and the bits around it.</p>
        </div>
        <ul className="category-grid">
          {categories.map((c) => (
            <li key={c.code} className="category-card">
              <span className="category-code">{c.code}</span>
              <h3>{c.name}</h3>
              <p>{c.detail}</p>
            </li>
          ))}
        </ul>
      </section>

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
