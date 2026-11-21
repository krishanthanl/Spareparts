import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { apiClient } from '../api/client';
import { formatMoney } from '../lib/shop';

type CartLine = { itemId: string; quantity: number };
type Quote = {
  subtotal: number;
  deliveryCharge: number;
  total: number;
  deliveryEstimate: string | null;
};

export function CartQuote({ lines }: { lines: CartLine[] }) {
  const [fulfillment, setFulfillment] = useState('Delivery');
  const quote = useMutation({
    mutationFn: async (form: FormData) => {
      const response = await apiClient.post<Quote>('/api/checkout/quote', {
        lines,
        fulfillment,
        postcode: form.get('postcode'),
        isRural: form.has('rural'),
      });
      return response.data;
    },
  });
  const error = isAxiosError<{ detail?: string }>(quote.error)
    ? quote.error.response?.data?.detail
    : undefined;

  return (
    <section aria-labelledby="cart-quote-heading">
      <h2 id="cart-quote-heading">Delivery and pickup estimate</h2>
      <form
        className="cart-quantity"
        onChange={() => quote.reset()}
        onSubmit={(event) => {
          event.preventDefault();
          quote.mutate(new FormData(event.currentTarget));
        }}
      >
        <fieldset disabled={quote.isPending}>
          <label>
            Fulfillment
            <select
              value={fulfillment}
              onChange={(event) => setFulfillment(event.target.value)}
            >
              <option value="Delivery">Delivery</option>
              <option value="Pickup">Pickup</option>
            </select>
          </label>
          {fulfillment === 'Delivery' && (
            <>
              <label>
                NZ postcode
                <input
                  name="postcode"
                  autoComplete="postal-code"
                  inputMode="numeric"
                  pattern="[0-9]{4}"
                  maxLength={4}
                  required
                />
              </label>
              <label>
                <input type="checkbox" name="rural" /> Rural delivery
              </label>
            </>
          )}
          <button className="btn btn-ghost">Get estimate</button>
        </fieldset>
      </form>
      <div aria-live="polite">
        {quote.isPending && <p>Checking prices and availability…</p>}
        {quote.isError && (
          <p>{error || 'Could not get an estimate. Try again.'}</p>
        )}
        {quote.data && (
          <>
            <p>Parts {formatMoney(quote.data.subtotal)}</p>
            <p>
              {fulfillment === 'Delivery' ? 'Delivery' : 'Pickup'}{' '}
              {formatMoney(quote.data.deliveryCharge)}
            </p>
            <p>
              <strong>
                Estimated total (NZD) {formatMoney(quote.data.total)}
              </strong>
            </p>
            {quote.data.deliveryEstimate && (
              <p>{quote.data.deliveryEstimate}</p>
            )}
          </>
        )}
      </div>
    </section>
  );
}
