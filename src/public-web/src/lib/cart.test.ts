import { readCart, useCart } from './cart';
import type { PublicItem } from '../api/catalog';

const id = '00000000-0000-4000-8000-000000000001';
const item: PublicItem = {
  id,
  sku: 'BR-1',
  name: 'Brake pad',
  description: null,
  categoryName: 'Brakes',
  categorySlug: 'brakes',
  partBrand: null,
  thumbnailUrl: null,
  imageUrl: null,
  price: 25,
  availability: 'InStock',
};

beforeEach(() => {
  useCart.setState({ lines: [] });
  localStorage.clear();
});

it('persists valid quantities and display snapshots across hydration', async () => {
  useCart.getState().save(item, 2);
  useCart.setState({ lines: [] });
  localStorage.setItem(
    'spareparts-cart',
    JSON.stringify({
      version: 1,
      state: {
        lines: [{ itemId: id, quantity: 2, name: 'Brake pad', price: 25 }],
      },
    }),
  );
  await useCart.persist.rehydrate();
  expect(useCart.getState().lines).toEqual([
    { itemId: id, quantity: 2, name: 'Brake pad', price: 25 },
  ]);
  useCart.getState().remove(id);
  expect(useCart.getState().lines).toEqual([]);
});

it('drops malformed saved lines and refuses invalid or unpriced items', () => {
  expect(
    readCart({
      lines: [
        null,
        { itemId: '__proto__', quantity: 1 },
        { itemId: id, quantity: 100, price: 25, name: 'Pad' },
      ],
    }),
  ).toEqual([]);
  useCart.getState().save(item, 0);
  useCart.getState().save({ ...item, price: null }, 1);
  expect(useCart.getState().lines).toEqual([]);
});
