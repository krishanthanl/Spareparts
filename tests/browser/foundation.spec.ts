import { test, expect } from '@playwright/test';

for (const app of [
  { name: 'public', port: 5174, heading: 'Vehicle spare parts' },
]) {
  test(
    app.name + ' shell navigates without browser errors',
    async ({ page }, testInfo) => {
      const errors: string[] = [];
      page.on('pageerror', (error) => errors.push(error.message));
      await page.goto('http://localhost:' + app.port);
      await expect(
        page.getByRole('heading', { name: app.heading }),
      ).toBeVisible();
      await page.screenshot({
        path: testInfo.outputPath(app.name + '-home.png'),
        fullPage: true,
      });
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= window.innerWidth,
        ),
      ).toBe(true);
      await page
        .getByRole('navigation')
        .getByRole('link', { name: 'Shop details' })
        .click();
      await expect(page.getByText('Not GST registered')).toBeVisible();
      await expect(page.getByText('Pacific/Auckland')).toBeVisible();
      await page.reload();
      await expect(
        page.getByRole('heading', { name: 'Shop details' }),
      ).toBeVisible();
      expect(errors).toEqual([]);
    },
  );
}

test('admin opens on sign-in without browser errors', async ({
  page,
}, testInfo) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto('http://localhost:5173/items');
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
  await expect(page.getByRole('navigation')).toHaveCount(0);
  await page.screenshot({
    path: testInfo.outputPath('admin-sign-in.png'),
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
  expect(
    await page.evaluate(() => localStorage.length + sessionStorage.length),
  ).toBe(0);
  expect(errors).toEqual([]);
});

test('API serves health and safe unknown-route errors', async ({ request }) => {
  const live = await request.get('http://localhost:5080/health/live');
  expect(live.status()).toBe(200);
  expect(live.headers()['x-correlation-id']).toBeTruthy();
  const ready = await request.get('http://localhost:5080/health/ready');
  expect(ready.status()).toBe(503);
  const unknown = await request.get('http://localhost:5080/missing');
  expect(unknown.status()).toBe(404);
  expect(unknown.headers()['content-type']).toContain(
    'application/problem+json',
  );
});
