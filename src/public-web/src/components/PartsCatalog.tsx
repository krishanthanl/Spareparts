import { useState } from 'react';
import { useInfiniteQuery } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { Link } from 'react-router-dom';
import {
  mediaUrl,
  publicCatalog,
  type PartsFilters,
  type PublicCategory,
  type PublicItem,
} from '../api/catalog';
import { availabilityLabel, formatMoney } from '../lib/shop';

export function PartPhoto({
  path,
  alt,
  width = 480,
  height = 360,
  eager = false,
}: {
  path: string | null;
  alt: string;
  width?: number;
  height?: number;
  eager?: boolean;
}) {
  const [failed, setFailed] = useState(false);
  if (!path || failed)
    return <span className="photo-placeholder">No photo available</span>;
  return (
    <img
      src={mediaUrl(path)}
      alt={alt}
      width={width}
      height={height}
      loading={eager ? 'eager' : 'lazy'}
      onError={() => setFailed(true)}
    />
  );
}

export function PartCard({ item }: { item: PublicItem }) {
  return (
    <li className="part-card">
      <div className="part-image">
        <PartPhoto
          key={item.thumbnailUrl}
          path={item.thumbnailUrl}
          alt={item.name}
        />
      </div>
      <div className="part-body">
        <span className="part-meta">
          {item.categoryName} · <span className="sku">{item.sku}</span>
        </span>
        <h3>
          <Link to={`/parts/${item.id}`}>{item.name}</Link>
        </h3>
        {item.partBrand && <p className="part-brand">{item.partBrand}</p>}
        <p className="part-price">
          {item.price == null ? 'Ask for price' : formatMoney(item.price)}
        </p>
        <span className="stock-label" data-stock={item.availability}>
          {availabilityLabel[item.availability]}
        </span>
      </div>
    </li>
  );
}

export function CategoryGrid({
  categories,
  selected,
  onSelect,
}: {
  categories: PublicCategory[];
  selected: string;
  onSelect: (slug: string) => void;
}) {
  return (
    <ul className="category-grid">
      {categories.map((c, index) => (
        <li key={c.id}>
          <button
            type="button"
            className="category-card"
            aria-pressed={selected === c.slug}
            onClick={() => onSelect(selected === c.slug ? '' : c.slug)}
          >
            {c.thumbnailUrl && (
              <img
                className="category-image"
                src={mediaUrl(c.thumbnailUrl)}
                alt=""
                loading="lazy"
              />
            )}
            <span className="category-code">
              {String(index + 1).padStart(2, '0')} · {c.itemCount} parts
            </span>
            <h3>{c.name}</h3>
            {c.description && <p>{c.description}</p>}
          </button>
        </li>
      ))}
    </ul>
  );
}

export function PartsGrid({
  category,
  q,
  categories,
  onCategory,
  filters,
  page = 1,
  onPage,
}: {
  category: string;
  q: string;
  categories: PublicCategory[];
  onCategory: (slug: string) => void;
  filters?: PartsFilters;
  page?: number;
  onPage?: (value: number) => void;
}) {
  const parts = useInfiniteQuery({
    queryKey: ['public-items', category, q, filters, page],
    queryFn: ({ pageParam, signal }) =>
      publicCatalog.items({ category, q, ...filters, page: pageParam }, signal),
    initialPageParam: page,
    getNextPageParam: (last) =>
      last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined,
  });
  const items = parts.data?.pages.flatMap((p) => p.items) ?? [];
  const total = parts.data?.pages[0]?.totalCount ?? 0;
  const invalidFilters =
    isAxiosError(parts.error) && parts.error.response?.status === 400;
  const label = categories.find((c) => c.slug === category)?.name;

  return (
    <section id="parts" className="parts" aria-labelledby="parts-title">
      <div className="section-head">
        <h2 id="parts-title">{label ?? 'All parts'}</h2>
        <p role="status">
          {parts.isPending
            ? 'Loading parts…'
            : parts.isError
              ? 'Parts can’t be shown right now.'
              : `${total} part${total === 1 ? '' : 's'}${q ? ` matching “${q}”` : ''}`}
        </p>
      </div>
      {categories.length > 0 && (
        <div className="filter-chips" aria-label="Filter by category">
          <button
            type="button"
            aria-pressed={category === ''}
            onClick={() => onCategory('')}
          >
            All
          </button>
          {categories.map((c) => (
            <button
              key={c.id}
              type="button"
              aria-pressed={category === c.slug}
              onClick={() => onCategory(c.slug)}
            >
              {c.name}
            </button>
          ))}
        </div>
      )}
      {parts.isPending && (
        <ul className="parts-grid" aria-hidden="true">
          {Array.from({ length: 8 }, (_, i) => (
            <li key={i} className="part-card skeleton" />
          ))}
        </ul>
      )}
      {parts.isError && (
        <div className="parts-empty">
          <p>
            {invalidFilters
              ? 'The search filters are invalid. Change or clear the filters to try again.'
              : 'The parts list is offline. Call or email the counter and we’ll check stock for you.'}
          </p>
          {!invalidFilters && (
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => void parts.refetch()}
            >
              Try again
            </button>
          )}
        </div>
      )}
      {parts.isSuccess && items.length === 0 && (
        <div className="parts-empty">
          <p>
            No parts found{q ? ` for “${q}”` : ''}. Try a different word, or ask
            us — we can often get it in.
          </p>
        </div>
      )}
      {items.length > 0 && (
        <ul className="parts-grid">
          {items.map((item) => (
            <PartCard key={item.id} item={item} />
          ))}
        </ul>
      )}
      {onPage && (page > 1 || parts.hasNextPage) && (
        <nav className="parts-more pagination" aria-label="Parts pages">
          <button disabled={page <= 1} onClick={() => onPage(page - 1)}>
            Previous
          </button>
          <span>Page {page}</span>
          <button
            disabled={!parts.hasNextPage}
            onClick={() => onPage(page + 1)}
          >
            Next
          </button>
        </nav>
      )}
      {!onPage && parts.hasNextPage && (
        <div className="parts-more">
          <button
            type="button"
            className="btn btn-primary"
            disabled={parts.isFetchingNextPage}
            onClick={() => void parts.fetchNextPage()}
          >
            {parts.isFetchingNextPage
              ? 'Loading…'
              : `Show more (${total - items.length} left)`}
          </button>
        </div>
      )}
    </section>
  );
}
