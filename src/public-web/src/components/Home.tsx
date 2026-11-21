import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useCategories } from '../api/catalog';
import { shopContact } from '../lib/contact';
import { CategoryGrid, PartsGrid } from './PartsCatalog';
import { VehiclePicker } from './VehiclePicker';
import type { PartsFilters } from '../api/catalog';

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

/**
 * Hero photo (Unsplash License, credit in IMAGE-CREDITS.md). Decorative, so it has empty alt text.
 * Falls back to the drawn ute if the image can't load.
 */
function HeroVisual() {
  const [failed, setFailed] = useState(false);
  if (failed) return <HeroUte />;
  return (
    <div className="hero-visual hero-photo">
      <img
        src="/hero/hero-pickup-1600.webp"
        srcSet="/hero/hero-pickup-800.webp 800w, /hero/hero-pickup-1600.webp 1600w"
        sizes="(max-width: 700px) 100vw, 58vw"
        width={1600}
        height={602}
        alt=""
        fetchPriority="high"
        decoding="async"
        onError={() => setFailed(true)}
      />
    </div>
  );
}

/** Drawn side-on double-cab ute, used if the hero photo fails to load. */
function HeroUte() {
  return (
    <div className="hero-visual" aria-hidden="true">
      <svg viewBox="0 0 680 300" role="presentation" focusable="false">
        <defs>
          <linearGradient id="ute-body" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor="#4a5058" />
            <stop offset="0.55" stopColor="#2b2f35" />
            <stop offset="1" stopColor="#1a1c20" />
          </linearGradient>
          <linearGradient id="ute-glass" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#5c6670" />
            <stop offset="1" stopColor="#15181c" />
          </linearGradient>
          <linearGradient id="ute-beam" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" stopColor="#ffc400" stopOpacity="0.55" />
            <stop offset="1" stopColor="#ffc400" stopOpacity="0" />
          </linearGradient>
        </defs>
        <ellipse
          cx="330"
          cy="278"
          rx="310"
          ry="10"
          fill="#000"
          opacity="0.55"
        />
        <path d="M604 156 L680 128 L680 214 L604 172 Z" fill="url(#ute-beam)" />
        {/* Body */}
        <path
          d="M36 206 L36 150 L44 142 L262 142 L278 82 Q282 74 292 74 L400 72 Q418 72 428 82 L468 132 L580 144 Q600 148 604 164 L606 206 Q606 222 590 222 L556 222 A56 56 0 0 0 444 222 L206 222 A56 56 0 0 0 94 222 L52 222 Q36 222 36 206 Z"
          fill="url(#ute-body)"
          stroke="#5a616a"
          strokeWidth="2"
        />
        {/* Windows */}
        <path d="M288 86 L338 84 L338 128 L276 128 Z" fill="url(#ute-glass)" />
        <path
          d="M348 84 L402 82 Q412 82 418 90 L450 128 L348 128 Z"
          fill="url(#ute-glass)"
        />
        {/* Panel lines, tray rail, shoulder highlight */}
        <path
          d="M343 132 V214 M458 136 V214 M262 146 V214 M44 152 H262"
          stroke="#14161a"
          strokeWidth="3"
        />
        <path
          d="M60 170 H596"
          stroke="#ffffff"
          strokeOpacity="0.12"
          strokeWidth="2"
        />
        <rect x="320" y="150" width="16" height="4" rx="2" fill="#8b929b" />
        <rect x="434" y="152" width="16" height="4" rx="2" fill="#8b929b" />
        <path d="M452 126 L470 124 L472 136 L456 138 Z" fill="#1a1c20" />
        {/* Lights and bumper */}
        <path d="M584 150 L603 158 L604 172 L584 168 Z" fill="#ffc400" />
        <rect x="34" y="152" width="9" height="26" rx="2" fill="#d94a2b" />
        <rect x="560" y="196" width="48" height="10" rx="3" fill="#14161a" />
        <rect x="30" y="196" width="26" height="10" rx="3" fill="#14161a" />
        {/* Wheels */}
        {[150, 500].map((cx) => (
          <g key={cx}>
            <circle cx={cx} cy="226" r="48" fill="#0b0c0e" />
            <circle
              cx={cx}
              cy="226"
              r="30"
              fill="#2b2f35"
              stroke="#ffc400"
              strokeWidth="5"
            />
            <circle
              cx={cx}
              cy="226"
              r="9"
              fill="#14161a"
              stroke="#8b929b"
              strokeWidth="2"
            />
            {[0, 72, 144, 216, 288].map((a) => (
              <rect
                key={a}
                x={cx - 3}
                y="200"
                width="6"
                height="15"
                rx="2"
                fill="#5a616a"
                transform={`rotate(${a} ${cx} 226)`}
              />
            ))}
          </g>
        ))}
      </svg>
    </div>
  );
}

function PartSearch({
  initialQuery,
  onSearch,
}: {
  initialQuery: string;
  onSearch: (q: string) => void;
}) {
  const [query, setQuery] = useState(initialQuery);
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
  const [params, setParams] = useSearchParams();
  const [vehicle, setVehicle] = useState<PartsFilters>({});
  const q = params.get('q') ?? '';
  const category = params.get('category') ?? '';
  function update(key: string, value: string) {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next);
  }
  const categories = useCategories();
  const live = categories.data ?? [];
  const pick = (slug: string) => {
    update('category', slug);
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
            <Link className="btn btn-primary" to="/parts">
              Browse parts
            </Link>
            <a className="btn btn-ghost" href="#contact">
              Talk to us
            </a>
          </div>
        </div>
        <HeroVisual />
      </section>

      <PartSearch
        key={q}
        initialQuery={q}
        onSearch={(value) => update('q', value)}
      />
      <section className="store-section home-finder">
        <VehiclePicker
          values={vehicle}
          change={(patch) =>
            setVehicle((current) => ({ ...current, ...patch }))
          }
        />
        <Link
          className="btn btn-primary"
          to={`/parts?${new URLSearchParams(Object.entries(vehicle).filter(([, value]) => value))}`}
        >
          Show matching parts
        </Link>
      </section>

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
          <CategoryGrid
            categories={live.slice(0, 8)}
            selected={category}
            onSelect={pick}
          />
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
        onCategory={(value) => update('category', value)}
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
