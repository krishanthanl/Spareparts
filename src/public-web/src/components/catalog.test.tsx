import {
  render,
  screen,
  fireEvent,
  waitFor,
  within,
} from '@testing-library/react';
import { AxiosHeaders, type AxiosResponse } from 'axios';
import { apiClient } from '../api/client';
import { AppProviders } from '../providers/AppProviders';
import { Home } from './Home';

const categories = [
  {
    id: 'c1',
    name: 'Headlights',
    slug: 'headlights',
    description: 'LED units',
    thumbnailUrl: '/media/c.webp',
    itemCount: 2,
  },
  {
    id: 'c2',
    name: 'Tail Lights',
    slug: 'tail-lights',
    description: null,
    thumbnailUrl: null,
    itemCount: 1,
  },
];
const item = (n: number, slug = 'headlights') => ({
  id: `i${n}`,
  sku: `TP-00${n}`,
  name: `Part ${n}`,
  description: 'Direct fit.',
  categoryName: 'Headlights',
  categorySlug: slug,
  partBrand: null,
  imageUrl: `/media/items/${n}.webp`,
  thumbnailUrl: `/media/items/${n}-thumb.webp`,
});

let requests: { url: string; params: Record<string, unknown> }[] = [];
let failItems = false;

beforeEach(() => {
  requests = [];
  failItems = false;
  Element.prototype.scrollIntoView = vi.fn();
  apiClient.defaults.adapter = async (config) => {
    const url = config.url ?? '';
    requests.push({ url, params: config.params ?? {} });
    const ok = (data: unknown): AxiosResponse => ({
      status: 200,
      data,
      statusText: '',
      headers: {},
      config: { ...config, headers: new AxiosHeaders() },
    });
    if (url === '/api/public/categories') return ok(categories);
    if (failItems) throw new Error('offline');
    const { category, q, page } = config.params as {
      category?: string;
      q?: string;
      page: number;
    };
    let items = [item(1), item(2), item(3, 'tail-lights')];
    if (category) items = items.filter((i) => i.categorySlug === category);
    if (q) items = items.filter((i) => i.sku.includes(q));
    return ok({ items, page, pageSize: 24, totalCount: items.length });
  };
});

const renderHome = () =>
  render(
    <AppProviders>
      <Home />
    </AppProviders>,
  );

it('shows live categories and parts with their photos', async () => {
  renderHome();
  expect(await screen.findByRole('img', { name: 'Part 1' })).toHaveAttribute(
    'src',
    'http://localhost:5080/media/items/1-thumb.webp',
  );
  expect(screen.getByText('3 parts')).toBeInTheDocument();
  expect(
    screen.getByRole('button', { name: /Headlights.*LED units/ }),
  ).toBeInTheDocument();
});

it('filters parts by category and search', async () => {
  renderHome();
  await screen.findByText('3 parts');
  const chips = screen.getByLabelText('Filter by category');
  fireEvent.click(within(chips).getByRole('button', { name: 'Tail Lights' }));
  expect(await screen.findByText('1 part')).toBeInTheDocument();
  expect(
    within(chips).getByRole('button', { name: 'Tail Lights' }),
  ).toHaveAttribute('aria-pressed', 'true');
  fireEvent.click(within(chips).getByRole('button', { name: 'All' }));
  fireEvent.change(screen.getByRole('searchbox'), {
    target: { value: 'TP-002' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Search' }));
  expect(
    await screen.findByText('1 part matching “TP-002”'),
  ).toBeInTheDocument();
  await waitFor(() => expect(requests.at(-1)?.params.q).toBe('TP-002'));
});

it('keeps the page useful when the parts list is offline', async () => {
  failItems = true;
  renderHome();
  expect(await screen.findByText(/parts list is offline/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
});
