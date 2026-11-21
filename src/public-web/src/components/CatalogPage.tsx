import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useCategories, type PartsFilters } from '../api/catalog';
import { PartsGrid } from './PartsCatalog';
import { VehiclePicker } from './VehiclePicker';

const keys = [
  'category',
  'q',
  'brand',
  'makeId',
  'modelId',
  'year',
  'minPrice',
  'maxPrice',
  'availability',
  'sort',
] as const;
export function CatalogPage() {
  const [params, setParams] = useSearchParams();
  const [priceError, setPriceError] = useState(false);
  const categories = useCategories();
  const filters: PartsFilters = {};
  for (const key of keys) {
    const value = params.get(key);
    if (value) filters[key] = value;
  }
  function change(patch: PartsFilters) {
    setPriceError(false);
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(patch)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }
    next.delete('page');
    setParams(next);
  }
  const page = Math.max(
    1,
    Math.min(100000, Number.parseInt(params.get('page') ?? '1', 10) || 1),
  );
  return (
    <section className="store-section">
      <h1>Browse parts</h1>
      <VehiclePicker values={filters} change={change} />
      <details className="store-filters" open>
        <summary>Search and filter</summary>
        <form
          key={params.toString()}
          className="filter-form"
          onSubmit={(event) => {
            event.preventDefault();
            const values = Object.fromEntries(
              new FormData(event.currentTarget),
            ) as PartsFilters;
            if (
              values.minPrice &&
              values.maxPrice &&
              Number(values.maxPrice) < Number(values.minPrice)
            ) {
              setPriceError(true);
              return;
            }
            change(values);
          }}
          onChange={() => setPriceError(false)}
        >
          <label>
            Search
            <input
              type="search"
              name="q"
              maxLength={100}
              defaultValue={filters.q}
            />
          </label>
          <label>
            Category
            <select name="category" defaultValue={filters.category ?? ''}>
              <option value="">All categories</option>
              {filters.category &&
                !categories.data?.some(
                  (category) => category.slug === filters.category,
                ) && (
                  <option value={filters.category}>{filters.category}</option>
                )}
              {categories.data?.map((category) => (
                <option key={category.id} value={category.slug}>
                  {category.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Part brand
            <input name="brand" maxLength={120} defaultValue={filters.brand} />
          </label>
          <label>
            Min price (NZD)
            <input
              type="number"
              min="0"
              step="0.01"
              name="minPrice"
              defaultValue={filters.minPrice}
            />
          </label>
          <label>
            Max price (NZD)
            <input
              type="number"
              min="0"
              step="0.01"
              name="maxPrice"
              defaultValue={filters.maxPrice}
            />
          </label>
          <label>
            Availability
            <select
              name="availability"
              defaultValue={filters.availability ?? ''}
            >
              <option value="">Any availability</option>
              <option value="InStock">In stock</option>
              <option value="LowStock">Low stock</option>
              <option value="OutOfStock">Out of stock</option>
            </select>
          </label>
          <label>
            Sort
            <select name="sort" defaultValue={filters.sort ?? ''}>
              <option value="">Category</option>
              <option value="name">Name</option>
              <option value="price-asc">Price low to high</option>
              <option value="price-desc">Price high to low</option>
            </select>
          </label>
          <button className="btn btn-primary">Apply filters</button>
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => {
              setPriceError(false);
              setParams({});
            }}
          >
            Clear filters
          </button>
          {priceError && (
            <p role="alert">
              Maximum price must be at least the minimum price.
            </p>
          )}
        </form>
      </details>
      <PartsGrid
        q={filters.q ?? ''}
        category={filters.category ?? ''}
        categories={categories.data ?? []}
        onCategory={(category) => change({ category })}
        filters={filters}
        page={page}
        onPage={(value) => {
          const next = new URLSearchParams(params);
          if (value === 1) next.delete('page');
          else next.set('page', String(value));
          setParams(next);
        }}
      />
    </section>
  );
}
