import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { Link } from 'react-router-dom';
import { apiClient } from '../api/client';
import { useCart } from '../lib/cart';
import { formatMoney } from '../lib/shop';

type Receipt = {
  orderId: string;
  status: string;
  paymentStatus: string;
  fulfillmentStatus: string;
  holdUntil: string;
  total: number;
  bankInstructions?: string;
  invoiceNumber?: string;
  shipments?: {
    status: string;
    carrier?: string;
    trackingReference?: string;
  }[];
};

function problem(error: unknown) {
  return isAxiosError<{ detail?: string; title?: string }>(error)
    ? (error.response?.data?.detail ??
        error.response?.data?.title ??
        'Try again.')
    : 'Try again.';
}

export function CheckoutPage() {
  const lines = useCart((state) => state.lines);
  const [fulfillment, setFulfillment] = useState('Delivery');
  const options = useQuery({
    queryKey: ['checkout-options'],
    queryFn: async () =>
      (await apiClient.get<{ bankTransfer: boolean }>('/api/checkout/options'))
        .data,
  });
  const review = useMutation({
    mutationFn: async (form: FormData) => {
      const quote = {
        lines: lines.map(({ itemId, quantity }) => ({ itemId, quantity })),
        fulfillment,
        postcode: form.get('postcode'),
        isRural: form.has('rural'),
      };
      const estimate = (
        await apiClient.post<{
          lines: {
            name: string;
            quantity: number;
            unitPrice: number;
            total: number;
          }[];
          total: number;
          deliveryCharge: number;
        }>('/api/checkout/quote', quote)
      ).data;
      return {
        estimate,
        request: {
          quote,
          requestKey: crypto.randomUUID(),
          name: form.get('name'),
          email: form.get('email'),
          phone: form.get('phone'),
          billingAddress: form.get('billingAddress'),
          deliveryAddress: form.get('deliveryAddress'),
          acceptedUnitPrices: estimate.lines.map((line) => line.unitPrice),
          acceptedDeliveryCharge: estimate.deliveryCharge,
        },
      };
    },
  });
  const place = useMutation({
    mutationFn: async (request: NonNullable<typeof review.data>['request']) =>
      (await apiClient.post<Receipt>('/api/checkout/orders', request)).data,
  });
  const track = useMutation({
    mutationFn: async ({ id, key }: { id: string; key: string }) =>
      (
        await apiClient.post<Receipt>(
          `/api/checkout/orders/${encodeURIComponent(id)}/access`,
          null,
          {
            headers: { 'Idempotency-Key': key },
          },
        )
      ).data,
  });
  const receipt = track.data ?? place.data;
  const trackingKey = track.data
    ? track.variables?.key
    : review.data?.request.requestKey;
  const invoice = useMutation({
    mutationFn: async ({ id, key }: { id: string; key: string }) => {
      const response = await apiClient.post<Blob>(
        `/api/checkout/orders/${encodeURIComponent(id)}/invoice`,
        null,
        {
          headers: { 'Idempotency-Key': key },
          responseType: 'blob',
        },
      );
      const url = URL.createObjectURL(response.data);
      const download = document.createElement('a');
      download.href = url;
      download.download = `${receipt?.invoiceNumber ?? 'invoice'}.pdf`;
      download.click();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    },
  });

  return (
    <section className="store-section">
      <h1>Checkout</h1>
      {receipt ? (
        <>
          <h2>
            Order{' '}
            {receipt.status === 'Draft'
              ? 'received'
              : receipt.status.toLowerCase()}
          </h2>
          <p>Order reference {receipt.orderId}</p>
          <p>
            Payment {receipt.paymentStatus} · Fulfillment{' '}
            {receipt.fulfillmentStatus}
          </p>
          <p>Total {formatMoney(receipt.total)}</p>
          {receipt.shipments?.map((shipment, index) => (
            <p key={index}>
              Shipment {shipment.status} {shipment.carrier}{' '}
              {shipment.trackingReference}
            </p>
          ))}
          {receipt.invoiceNumber && trackingKey && (
            <button
              className="btn btn-ghost"
              disabled={invoice.isPending}
              onClick={() =>
                invoice.mutate({ id: receipt.orderId, key: trackingKey })
              }
            >
              Download invoice
            </button>
          )}
          {invoice.isError && (
            <p role="alert">Could not download the invoice.</p>
          )}
          {receipt.status === 'Draft' && (
            <>
              <p>
                Transfer payment before{' '}
                {new Date(receipt.holdUntil).toLocaleString('en-NZ', {
                  timeZone: 'Pacific/Auckland',
                  timeZoneName: 'short',
                })}{' '}
                using the order reference above.
              </p>
              <p>{receipt.bankInstructions}</p>
            </>
          )}
          {place.data && review.data && (
            <p>
              Save this private tracking key to check your order later:{' '}
              <code>{review.data.request.requestKey}</code>
            </p>
          )}
        </>
      ) : review.data ? (
        <>
          <h2>Review your order</h2>
          <ul>
            {review.data.estimate.lines.map((line, index) => (
              <li key={review.data.request.quote.lines[index].itemId}>
                {line.quantity} × {line.name} · {formatMoney(line.total)}
              </li>
            ))}
          </ul>
          <p>Delivery {formatMoney(review.data.estimate.deliveryCharge)}</p>
          <p>Total {formatMoney(review.data.estimate.total)}</p>
          <p>Pay by bank transfer after placing your order.</p>
          {place.isError && <p role="alert">{problem(place.error)}</p>}
          <button
            className="btn"
            disabled={place.isPending}
            onClick={() => place.mutate(review.data!.request)}
          >
            Place order
          </button>
          <button
            className="btn btn-ghost"
            disabled={place.isPending}
            onClick={() => {
              place.reset();
              review.reset();
            }}
          >
            Start again
          </button>
        </>
      ) : options.data?.bankTransfer && lines.length ? (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            review.mutate(new FormData(event.currentTarget));
          }}
        >
          <fieldset disabled={review.isPending}>
            <legend>Contact and fulfillment</legend>
            <label>
              Name{' '}
              <input name="name" autoComplete="name" maxLength={200} required />
            </label>
            <label>
              Email{' '}
              <input
                type="email"
                name="email"
                autoComplete="email"
                maxLength={200}
                required
              />
            </label>
            <label>
              Phone{' '}
              <input
                type="tel"
                name="phone"
                autoComplete="tel"
                maxLength={50}
              />
            </label>
            <label>
              Fulfillment{' '}
              <select
                value={fulfillment}
                onChange={(event) => setFulfillment(event.target.value)}
              >
                <option>Delivery</option>
                <option>Pickup</option>
              </select>
            </label>
            {fulfillment === 'Delivery' && (
              <>
                <label>
                  Delivery address{' '}
                  <textarea name="deliveryAddress" maxLength={500} required />
                </label>
                <label>
                  NZ postcode{' '}
                  <input
                    name="postcode"
                    inputMode="numeric"
                    pattern="[0-9]{4}"
                    required
                  />
                </label>
                <label>
                  <input type="checkbox" name="rural" /> Rural delivery
                </label>
              </>
            )}
            <label>
              Billing address{' '}
              <textarea name="billingAddress" maxLength={500} required />
            </label>
            <button className="btn">Review order</button>
          </fieldset>
          {review.isError && <p role="alert">{problem(review.error)}</p>}
        </form>
      ) : (
        <p>
          {options.isPending
            ? 'Checking checkout availability…'
            : lines.length
              ? 'Online ordering is unavailable.'
              : 'Your cart is empty.'}{' '}
          <Link to="/cart">View cart</Link>
        </p>
      )}
      <form
        onSubmit={(event) => {
          event.preventDefault();
          const form = new FormData(event.currentTarget);
          track.mutate({
            id: String(form.get('id')).trim(),
            key: String(form.get('key')).trim(),
          });
        }}
      >
        <fieldset disabled={track.isPending}>
          <legend>Track an order</legend>
          <label>
            Order reference <input name="id" required />
          </label>
          <label>
            Private tracking key <input name="key" type="password" required />
          </label>
          <button className="btn btn-ghost">Check status</button>
        </fieldset>
        {track.isError && <p role="alert">{problem(track.error)}</p>}
      </form>
    </section>
  );
}
