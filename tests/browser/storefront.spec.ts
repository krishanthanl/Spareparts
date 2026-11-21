import { expect, test } from '@playwright/test';

const id = '00000000-0000-4000-8000-000000000001';
const makeId = '00000000-0000-4000-8000-000000000002';
const modelId = '00000000-0000-4000-8000-000000000003';
const origin = process.env.STOREFRONT_TEST_ORIGIN ?? 'http://localhost:5174';
const part = {
  id,
  sku: 'BP-01',
  name: 'Ceramic brake pads',
  description: 'Front axle set',
  categoryName: 'Brakes',
  categorySlug: 'brakes',
  partBrand: 'Example',
  imageUrl: '/media/broken.webp',
  thumbnailUrl: '/media/broken.webp',
  price: 45,
  availability: 'LowStock',
};

test('storefront filters, opens a direct item link, and revalidates the cart', async ({
  page,
}) => {
  const errors: string[] = [];
  let price = 45;
  page.on('pageerror', (error) => errors.push(error.message));
  await page.route('**/api/public/**', async (route) => {
    const cors = {
      'access-control-allow-origin': '*',
      'access-control-allow-methods': 'GET, POST, OPTIONS',
      'access-control-allow-headers': 'content-type',
    };
    if (route.request().method() === 'OPTIONS') {
      await route.fulfill({ status: 204, headers: cors });
      return;
    }
    const url = new URL(route.request().url());
    const path = url.pathname.split('/').slice(3).join('/');
    let result: unknown;
    switch (path) {
      case 'categories':
        result = [
          { id: 'brakes', name: 'Brakes', slug: 'brakes', itemCount: 1 },
        ];
        break;
      case 'vehicles/makes':
        result = [{ id: makeId, name: 'Toyota' }];
        break;
      case 'vehicles/models':
        result = [{ id: modelId, name: 'Corolla' }];
        break;
      case 'items':
        result = { items: [part], totalCount: 1, page: 1, pageSize: 24 };
        break;
      case `items/${id}`:
        result = {
          item: part,
          images: [],
          related: [],
          fitments: [
            {
              id: 'f1',
              make: 'Toyota',
              model: 'Corolla',
              yearFrom: 2019,
              yearTo: 2022,
            },
          ],
        };
        break;
      case 'cart/validate':
        result = [
          {
            itemId: id,
            item: { ...part, price },
            accepted: route.request().postDataJSON().lines[0].quantity <= 2,
          },
        ];
        break;
      default:
        await route.abort();
        return;
    }
    await route.fulfill({
      status: 200,
      json: result,
      headers: cors,
    });
  });
  await page.route('**/media/broken.webp', (route) =>
    route.fulfill({ status: 404 }),
  );
  await page.goto(`${origin}/parts`);
  await page
    .getByRole('combobox', { name: 'Make', exact: true })
    .selectOption(makeId);
  await page
    .getByRole('combobox', { name: 'Model', exact: true })
    .selectOption(modelId);
  await page
    .getByRole('combobox', { name: 'Year', exact: true })
    .selectOption('2022');
  await page.getByLabel('Min price (NZD)').fill('60');
  await page.getByLabel('Max price (NZD)').fill('50');
  await page.getByRole('button', { name: 'Apply filters' }).click();
  await expect(page.getByRole('alert')).toHaveText(
    'Maximum price must be at least the minimum price.',
  );
  await page.getByLabel('Min price (NZD)').fill('');
  const filtered = page.waitForRequest((request) => {
    const url = new URL(request.url());
    return (
      url.pathname === '/api/public/items' &&
      url.searchParams.get('brand') === 'Example'
    );
  });
  await page.getByLabel('Part brand').fill('Example');
  await page.getByRole('button', { name: 'Apply filters' }).click();
  const search = new URL((await filtered).url()).searchParams;
  expect(search.get('year')).toBe('2022');
  expect(search.get('maxPrice')).toBe('50');
  expect(search.has('minPrice')).toBe(false);
  await expect(page).toHaveURL(/brand=Example/);
  await page.getByRole('link', { name: part.name }).click();
  await expect(page.getByRole('heading', { name: part.name })).toBeVisible();
  await expect(page.getByText('No photo available')).toBeVisible();
  await page.reload();
  await expect(page.getByText('Toyota Corolla · 2019–2022')).toBeVisible();
  await page.getByRole('button', { name: 'Add to cart' }).click();
  await expect(page.getByRole('button', { name: 'Cart (1)' })).toBeVisible();
  price = 48;
  await page.getByRole('button', { name: 'Cart (1)' }).click();
  const drawer = page.getByRole('dialog');
  await expect(drawer.getByText(/price changed from/)).toBeVisible();
  await drawer.getByLabel('Quantity').fill('3');
  await drawer.getByRole('button', { name: 'Update' }).click();
  await expect(
    drawer.getByText('The requested quantity is unavailable.'),
  ).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(drawer).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('button', { name: 'Cart (1)' })).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
  expect(errors).toEqual([]);
});
