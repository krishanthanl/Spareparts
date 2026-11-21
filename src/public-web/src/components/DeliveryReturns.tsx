import { Link } from 'react-router-dom';

export function DeliveryReturns() {
  return (
    <section className="store-section">
      <h1>Delivery and returns</h1>
      <h2>Delivery</h2>
      <p>
        {import.meta.env.VITE_DELIVERY_INFORMATION ||
          'Contact the shop to confirm delivery coverage, estimated timing and charges for your address.'}
      </p>
      <h2>Returns and refunds</h2>
      <p>
        {import.meta.env.VITE_RETURNS_INFORMATION ||
          'Contact the shop about a return or refund and have your invoice details handy.'}
      </p>
      <p>
        <Link to="/#contact">Contact the shop</Link>
      </p>
    </section>
  );
}
