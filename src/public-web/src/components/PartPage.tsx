import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { Link, useParams } from 'react-router-dom';
import { publicCatalog, type ItemDetail } from '../api/catalog';
import { useCart } from '../lib/cart';
import { shopContact } from '../lib/contact';
import { availabilityLabel, formatMoney } from '../lib/shop';
import { PartCard, PartPhoto } from './PartsCatalog';

function PartDetail({ detail }: { detail: ItemDetail }) {
  const { item, images, fitments, related } = detail;
  const [imageIndex, setImageIndex] = useState(0);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const save = useCart((state) => state.save);
  const image = images[imageIndex];
  const enquiry = encodeURIComponent(`Parts enquiry: ${item.sku} ${item.name}`);
  const phone = shopContact.phone.replace(/[^+\d]/g, '');
  const email = shopContact.email;
  return (
    <section className="store-section">
      <title>{`${item.name} (${item.sku}) | Spareparts NZ`}</title>
      <meta
        name="description"
        content={
          item.description ||
          `View ${item.name}, part ${item.sku}, at Spareparts NZ.`
        }
      />
      <p>
        <Link to={`/parts?category=${encodeURIComponent(item.categorySlug)}`}>
          {item.categoryName}
        </Link>
      </p>
      <div className="part-detail">
        <div>
          <div className="part-gallery-image">
            <PartPhoto
              key={image?.id ?? item.id}
              path={image?.url ?? item.imageUrl}
              alt={image?.altText || item.name}
              width={image?.width}
              height={image?.height}
              eager
            />
          </div>
          {images.length > 1 && (
            <div className="gallery-options" aria-label="Part photos">
              {images.map((photo, i) => (
                <button
                  key={photo.id}
                  aria-label={`Show photo ${i + 1}`}
                  aria-pressed={i === imageIndex}
                  onClick={() => setImageIndex(i)}
                >
                  <PartPhoto
                    path={photo.thumbnailUrl}
                    alt=""
                    width={80}
                    height={60}
                  />
                </button>
              ))}
            </div>
          )}
        </div>
        <div>
          <p className="part-meta">
            {item.sku}
            {item.partBrand ? ` · ${item.partBrand}` : ''}
          </p>
          <h1>{item.name}</h1>
          <p className="part-price">
            {item.price == null
              ? 'Ask for price'
              : `${formatMoney(item.price)} NZD`}
          </p>
          <span className="stock-label" data-stock={item.availability}>
            {availabilityLabel[item.availability]}
          </span>
          {item.description && <p>{item.description}</p>}
          {item.price != null && item.availability !== 'OutOfStock' && (
            <button
              className="btn btn-primary"
              disabled={busy}
              onClick={async () => {
                const lines = useCart.getState().lines;
                const existing = lines.find((line) => line.itemId === item.id);
                const quantity = (existing?.quantity ?? 0) + 1;
                if ((!existing && lines.length >= 25) || quantity > 99) {
                  setMessage('Your cart has reached its limit.');
                  return;
                }
                setBusy(true);
                setMessage('');
                try {
                  const [result] = await publicCatalog.validate([
                    { itemId: item.id, quantity },
                  ]);
                  if (result?.accepted && result.item) {
                    save(result.item, quantity);
                    setMessage('Added to your cart.');
                  } else setMessage('The requested quantity is unavailable.');
                } catch {
                  setMessage('Could not check availability. Try again.');
                } finally {
                  setBusy(false);
                }
              }}
            >
              Add to cart
            </button>
          )}
          {message && (
            <p role="status">
              {message} <Link to="/cart">View cart</Link>
            </p>
          )}
          <h2>Ask about this part</h2>
          <p>Quote {item.sku} when you contact the shop.</p>
          <div className="enquiry-links">
            {phone && <a href={`tel:${phone}`}>Call the shop</a>}
            {email.includes('@') && (
              <a href={`mailto:${email}?subject=${enquiry}`}>
                Email about this part
              </a>
            )}
            {!phone && !email.includes('@') && (
              <Link to="/#contact">Shop contact details</Link>
            )}
          </div>
        </div>
      </div>
      <h2>Vehicle fitment</h2>
      {fitments.length ? (
        <ul>
          {fitments.map((row) => (
            <li key={row.id}>
              {row.make} {row.model}
              {row.yearFrom != null && row.yearTo != null
                ? ` · ${row.yearFrom}–${row.yearTo}`
                : row.yearFrom != null
                  ? ` · From ${row.yearFrom}`
                  : row.yearTo != null
                    ? ` · Through ${row.yearTo}`
                    : ''}
            </li>
          ))}
        </ul>
      ) : (
        <p>Ask the shop to confirm fitment.</p>
      )}
      {related.length > 0 && (
        <>
          <h2>Related parts</h2>
          <ul className="parts-grid">
            {related.map((part) => (
              <PartCard key={part.id} item={part} />
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

export function PartPage() {
  const { id = '' } = useParams();
  const query = useQuery({
    queryKey: ['part-detail', id],
    queryFn: ({ signal }) => publicCatalog.detail(id, signal),
  });
  if (query.isPending)
    return (
      <section className="store-section" role="status">
        <div className="skeleton detail-skeleton" />
        Loading part…
      </section>
    );
  if (query.isError) {
    const missing =
      isAxiosError(query.error) && query.error.response?.status === 404;
    return (
      <section className="store-section">
        <h1>{missing ? 'Part not found' : 'Could not load this part'}</h1>
        {!missing && (
          <button
            className="btn btn-ghost"
            onClick={() => void query.refetch()}
          >
            Try again
          </button>
        )}
        <p>
          <Link to="/parts">Browse parts</Link>
        </p>
      </section>
    );
  }
  return <PartDetail key={id} detail={query.data} />;
}
