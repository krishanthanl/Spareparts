import { useInfiniteQuery } from '@tanstack/react-query';
import { mediaUrl, publicCatalog, type PublicCategory } from '../api/catalog';

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
}: {
  category: string;
  q: string;
  categories: PublicCategory[];
  onCategory: (slug: string) => void;
}) {
  const parts = useInfiniteQuery({
    queryKey: ['public-items', category, q],
    queryFn: ({ pageParam }) =>
      publicCatalog.items({ category, q, page: pageParam }),
    initialPageParam: 1,
    getNextPageParam: (last) =>
      last.page * last.pageSize < last.totalCount ? last.page + 1 : undefined,
  });
  const items = parts.data?.pages.flatMap((p) => p.items) ?? [];
  const total = parts.data?.pages[0]?.totalCount ?? 0;
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
            The parts list is offline. Call or email the counter and we’ll check
            stock for you.
          </p>
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => void parts.refetch()}
          >
            Try again
          </button>
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
            <li key={item.id} className="part-card">
              <div className="part-image">
                {item.thumbnailUrl ? (
                  <img
                    src={mediaUrl(item.thumbnailUrl)}
                    alt={item.name}
                    loading="lazy"
                    width={480}
                    height={360}
                  />
                ) : (
                  <span aria-hidden="true">No photo</span>
                )}
              </div>
              <div className="part-body">
                <span className="part-meta">
                  {item.categoryName} · <span className="sku">{item.sku}</span>
                </span>
                <h3>{item.name}</h3>
                {item.partBrand && (
                  <p className="part-brand">{item.partBrand}</p>
                )}
                {item.description && (
                  <p className="part-desc">{item.description}</p>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
      {parts.hasNextPage && (
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
