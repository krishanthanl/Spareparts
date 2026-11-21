import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { publicCatalog } from '../api/catalog';
import { useCart, type SavedLine } from '../lib/cart';
import { formatMoney } from '../lib/shop';
import { CartQuote } from './CartQuote';

function CartLine({
  line,
  live,
  pending,
  unavailable,
}: {
  line: SavedLine;
  live: Awaited<ReturnType<typeof publicCatalog.validate>>[number] | undefined;
  pending: boolean;
  unavailable: boolean;
}) {
  const [message, setMessage] = useState('');
  const [saving, setSaving] = useState(false);
  const save = useCart((state) => state.save);
  const remove = useCart((state) => state.remove);
  const item = live?.item;
  return (
    <li className="cart-line">
      {item ? (
        <Link to={`/parts/${line.itemId}`}>{item.name}</Link>
      ) : (
        <strong>{line.name}</strong>
      )}
      <p>
        {item?.price == null
          ? `${formatMoney(line.price)} · saved price`
          : item.price !== line.price
            ? `${formatMoney(item.price)} · price changed from ${formatMoney(line.price)}`
            : formatMoney(item.price)}
      </p>
      {pending && <p role="status">Checking availability…</p>}
      {unavailable && <p role="status">Availability could not be checked.</p>}
      {live && !live.accepted && (
        <p role="status">
          {item?.price != null
            ? 'The requested quantity is unavailable.'
            : 'This part is unavailable for the cart.'}
        </p>
      )}
      <form
        key={line.quantity}
        className="cart-quantity"
        onSubmit={async (event) => {
          event.preventDefault();
          const quantity = Number(
            new FormData(event.currentTarget).get('quantity'),
          );
          setSaving(true);
          setMessage('');
          try {
            const [result] = await publicCatalog.validate([
              { itemId: line.itemId, quantity },
            ]);
            if (
              result?.accepted &&
              result.item &&
              useCart.getState().lines.some((row) => row.itemId === line.itemId)
            ) {
              save(result.item, quantity);
              setMessage('Quantity checked and saved.');
            } else setMessage('The requested quantity is unavailable.');
          } catch {
            setMessage('Could not check availability. Try again.');
          } finally {
            setSaving(false);
          }
        }}
      >
        <label>
          Quantity
          <input
            type="number"
            name="quantity"
            min={1}
            max={99}
            step={1}
            defaultValue={line.quantity}
            required
            disabled={saving}
          />
        </label>
        <button className="btn btn-ghost" disabled={saving}>
          Update
        </button>
        <button
          type="button"
          className="btn btn-ghost"
          disabled={saving}
          onClick={() => remove(line.itemId)}
        >
          Remove
        </button>
      </form>
      {message && <p role="status">{message}</p>}
    </li>
  );
}

function CartContents({ showQuote = false }: { showQuote?: boolean }) {
  const lines = useCart((state) => state.lines);
  const quantities = lines.map(({ itemId, quantity }) => ({
    itemId,
    quantity,
  }));
  const validation = useQuery({
    queryKey: ['cart-validation', quantities],
    queryFn: ({ signal }) => publicCatalog.validate(quantities, signal),
    enabled: quantities.length > 0,
    staleTime: 0,
    refetchOnMount: 'always',
  });
  if (!lines.length)
    return (
      <p>
        Your cart is empty. <Link to="/parts">Browse parts</Link>
      </p>
    );
  const total =
    validation.isSuccess && validation.data.every((result) => result.accepted)
      ? lines.reduce(
          (sum, line) =>
            sum +
            (validation.data.find((row) => row.itemId === line.itemId)?.item
              ?.price ?? 0) *
              line.quantity,
          0,
        )
      : null;
  return (
    <>
      <p>
        Your cart saves parts for reference. Contact the shop to arrange a
        purchase.
      </p>
      <ul className="cart-lines">
        {lines.map((line) => (
          <CartLine
            key={line.itemId}
            line={line}
            live={validation.data?.find((row) => row.itemId === line.itemId)}
            pending={validation.isFetching}
            unavailable={validation.isError}
          />
        ))}
      </ul>
      {total !== null && (
        <strong>Parts subtotal (NZD) {formatMoney(total)}</strong>
      )}
      {showQuote && total !== null && (
        <CartQuote key={JSON.stringify(quantities)} lines={quantities} />
      )}
      {showQuote && (
        <p>
          <Link to="/checkout">Checkout and order tracking</Link>
        </p>
      )}
      <p>
        <Link to="/#contact">Contact the shop</Link>
      </p>
    </>
  );
}

export function CartPage() {
  return (
    <section className="store-section">
      <h1>Your cart</h1>
      <CartContents showQuote />
    </section>
  );
}

export function CartDrawer({ close }: { close: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const element = dialog.current;
    element?.showModal();
    return () => element?.close();
  }, []);
  return (
    <dialog
      ref={dialog}
      className="cart-drawer"
      aria-labelledby="cart-title"
      onCancel={(event) => {
        event.preventDefault();
        close();
      }}
    >
      <header>
        <h2 id="cart-title">Your cart</h2>
        <button onClick={close} className="btn btn-ghost">
          Close
        </button>
      </header>
      <CartContents />
      <Link to="/cart" onClick={close}>
        View cart page
      </Link>
    </dialog>
  );
}
