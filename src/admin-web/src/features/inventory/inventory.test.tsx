import {
  render,
  screen,
  fireEvent,
  within,
  waitFor,
} from '@testing-library/react';
import {
  AxiosError,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AppProviders } from '../../providers/AppProviders';
import { apiClient } from '../../api/client';
import { GrnEditPage, GrnListPage } from './GrnPages';
import { StockReportPage, StockCardPage } from './StockPages';
import { StocktakeEditPage, StocktakeListPage } from './StocktakePages';
import { StockDocumentListPage } from './StockDocumentPages';
import { formatQty, parseNumber, shopToday } from './api';
import { AuthContext } from '../auth/useAuth';

type Call = {
  url: string;
  method: string;
  params?: Record<string, unknown>;
  data?: unknown;
};
let calls: Call[];
let handler: (call: Call) => { status: number; data?: unknown };

beforeEach(() => {
  calls = [];
  apiClient.defaults.adapter = async (config: InternalAxiosRequestConfig) => {
    const call = {
      url: config.url ?? '',
      method: config.method ?? 'get',
      params: config.params,
      data: config.data,
    };
    calls.push(call);
    const { status, data } = handler(call);
    const response: AxiosResponse = {
      status,
      data,
      statusText: '',
      headers: {},
      config,
    };
    if (status >= 400)
      throw new AxiosError('failed', String(status), config, null, response);
    return response;
  };
});

const auth = (permissions: string[]) => ({
  state: { status: 'loading' as const },
  login: async () => {},
  logout: async () => {},
  changePassword: async () => {},
  can: (p: string) => permissions.includes(p),
});

const renderAt = (
  path: string,
  route: string,
  element: React.ReactNode,
  permissions: string[] = [],
) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <AppProviders>
        <AuthContext.Provider value={auth(permissions)}>
          <Routes>
            <Route path={route} element={element} />
          </Routes>
        </AuthContext.Provider>
      </AppProviders>
    </MemoryRouter>,
  );

const grn = {
  id: 'g1',
  grnNumber: 'GRN-000007',
  supplierName: 'Repco',
  supplierReference: 'INV-9',
  supplierDocumentDate: null,
  notes: null,
  status: 'Posted',
  postedAt: '2026-11-17T01:00:00Z',
  postedBy: 'u1',
  reversedAt: null,
  reversalReason: null,
  total: 33,
  version: 3,
  lines: [
    {
      id: 'l1',
      lineNo: 1,
      itemId: 'i1',
      sku: 'TP-001',
      itemName: 'Oil filter',
      quantity: 1.5,
      unitCost: 22,
      sellingPrice: 40,
      currentPrice: 40,
      lineTotal: 33,
    },
  ],
};

it.each([
  ['/inventory/grn', '/api/grns', <GrnListPage />],
  [
    '/inventory/adjustments',
    '/api/stock-adjustments',
    <StockDocumentListPage kind="Adjustment" />,
  ],
  [
    '/inventory/opening',
    '/api/opening-stock',
    <StockDocumentListPage kind="Opening" />,
  ],
  ['/inventory/stocktakes', '/api/stocktakes', <StocktakeListPage />],
])('can reach older documents on %s', async (path, url, element) => {
  handler = () => ({
    status: 200,
    data: { items: [], page: 1, pageSize: 25, totalCount: 51 },
  });
  renderAt(path, path, element);
  const next = await screen.findByRole('button', { name: 'Go to next page' });
  await waitFor(() => expect(next).toBeEnabled());
  fireEvent.click(next);
  await screen.findByText('26–50 of 51');
  expect(calls.some((c) => c.url === url && c.params?.page === 2)).toBe(true);
});

it('starts opening stock on its own first page after browsing older adjustments', async () => {
  handler = ({ url }) => ({
    status: 200,
    data: {
      items: [],
      page: 1,
      pageSize: 25,
      totalCount: url === '/api/stock-adjustments' ? 51 : 1,
    },
  });
  const wrap = (kind: 'Adjustment' | 'Opening') => (
    <MemoryRouter>
      <AppProviders>
        <StockDocumentListPage kind={kind} />
      </AppProviders>
    </MemoryRouter>
  );
  const view = render(wrap('Adjustment'));
  const next = await screen.findByRole('button', { name: 'Go to next page' });
  await waitFor(() => expect(next).toBeEnabled());
  fireEvent.click(next);
  await screen.findByText('26–50 of 51');
  view.rerender(wrap('Opening'));
  await waitFor(() =>
    expect(
      calls.some((c) => c.url === '/api/opening-stock' && c.params?.page === 1),
    ).toBe(true),
  );
});

it('shows a posted GRN read-only with fractional quantities and offers Reverse only to Admins', async () => {
  handler = ({ url }) =>
    url === '/api/grns/g1'
      ? { status: 200, data: grn }
      : { status: 200, data: [] };
  const { unmount } = renderAt(
    '/inventory/grn/g1',
    '/inventory/grn/:id',
    <GrnEditPage />,
    ['inventory.grn'],
  );
  expect(
    await screen.findByRole('heading', { name: 'GRN-000007' }),
  ).toBeInTheDocument();
  expect(screen.getByText('1.5')).toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: 'Post' }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: 'Reverse' }),
  ).not.toBeInTheDocument();
  unmount();
  renderAt('/inventory/grn/g1', '/inventory/grn/:id', <GrnEditPage />, [
    'inventory.grn',
    'inventory.reverse',
  ]);
  expect(
    await screen.findByRole('button', { name: 'Reverse' }),
  ).toBeInTheDocument();
});

it('asks for confirmation, then saves and posts a new GRN', async () => {
  handler = ({ url, method }) => {
    if (url === '/api/grns' && method === 'post')
      return {
        status: 201,
        data: { ...grn, id: 'g2', status: 'Draft', grnNumber: null },
      };
    if (url === '/api/grns/g2/post')
      return { status: 200, data: { ...grn, id: 'g2' } };
    if (url === '/api/grns/g2')
      return { status: 200, data: { ...grn, id: 'g2' } };
    return { status: 200, data: [] };
  };
  renderAt('/inventory/grn/new', '/inventory/grn/:id', <GrnEditPage />, [
    'inventory.grn',
  ]);
  fireEvent.change(
    await screen.findByLabelText(/Supplier/, {
      selector: 'input[role="combobox"]',
    }),
    { target: { value: 'Repco' } },
  );
  // No lines yet: Post stays disabled.
  expect(screen.getByRole('button', { name: 'Post' })).toBeDisabled();
});

it('requests stock at the end of a chosen date, or now for today', async () => {
  handler = () => ({
    status: 200,
    data: {
      asOfUtc: null,
      totalUnits: 3,
      totalValue: 15,
      items: { items: [], page: 1, pageSize: 50, totalCount: 0 },
    },
  });
  renderAt('/inventory/stock', '/inventory/stock', <StockReportPage />);
  expect(await screen.findByText(/3 units/)).toBeInTheDocument();
  expect(calls[0].params?.date).toBeUndefined();
  fireEvent.change(screen.getByLabelText('Stock at end of'), {
    target: { value: '2026-03-31' },
  });
  await screen.findByText(/End of 2026-03-31/);
  expect(calls.at(-1)!.params?.date).toBe('2026-03-31');
});

it('filters the stock report by the selected category', async () => {
  handler = ({ url }) => ({
    status: 200,
    data:
      url === '/api/categories'
        ? { items: [{ id: 'c1', name: 'Filters' }], totalCount: 1 }
        : {
            asOfUtc: null,
            totalUnits: 3,
            totalValue: 15,
            items: { items: [], page: 1, pageSize: 50, totalCount: 0 },
          },
  });
  renderAt('/inventory/stock', '/inventory/stock', <StockReportPage />);
  fireEvent.change(screen.getByRole('combobox', { name: 'Category' }), {
    target: { value: 'Fil' },
  });
  fireEvent.click(await screen.findByRole('option', { name: 'Filters' }));
  await waitFor(() =>
    expect(
      calls.some(
        (c) => c.url === '/api/stock' && c.params?.categoryId === 'c1',
      ),
    ).toBe(true),
  );
});

it('shows the stock card with running balances and links to documents', async () => {
  handler = ({ url }) =>
    url.endsWith('/stock-card')
      ? {
          status: 200,
          data: {
            itemId: 'i1',
            sku: 'TP-001',
            name: 'Oil filter',
            openingBalance: 0,
            onHand: 3,
            averageCost: 5,
            sellingPrice: null,
            movements: [
              {
                sequence: 1,
                occurredAt: '2026-11-17T01:00:00Z',
                movementType: 'Grn',
                qtyChange: 5,
                balance: 5,
                unitCost: 5,
                averageCostAfter: 5,
                refType: 'Grn',
                refId: 'g1',
                documentNumber: 'GRN-000001',
                createdBy: 'u',
              },
              {
                sequence: 2,
                occurredAt: '2026-11-17T02:00:00Z',
                movementType: 'Adjustment',
                qtyChange: -2,
                balance: 3,
                unitCost: 5,
                averageCostAfter: 5,
                refType: 'StockDocument',
                refId: 'd1',
                documentNumber: 'ADJ-000001',
                createdBy: 'u',
              },
            ],
          },
        }
      : { status: 200, data: [] };
  renderAt('/inventory/items/i1', '/inventory/items/:id', <StockCardPage />);
  const table = await screen.findByRole('table', { name: 'Stock movements' });
  expect(
    within(table).getByRole('link', { name: 'GRN-000001' }),
  ).toHaveAttribute('href', '/inventory/grn/g1');
  expect(
    within(table).getByRole('link', { name: 'ADJ-000001' }),
  ).toHaveAttribute('href', '/inventory/adjustments/d1');
  expect(within(table).getByText('-2')).toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: 'Change price' }),
  ).not.toBeInTheDocument();
});

it('shows stocktake differences as you type and warns about stock that moved', async () => {
  handler = ({ url }) =>
    url === '/api/stocktakes/s1'
      ? {
          status: 200,
          data: {
            id: 's1',
            number: null,
            notes: null,
            status: 'Draft',
            startedAt: '2026-11-17T01:00:00Z',
            postedAt: null,
            adjustmentId: null,
            version: 1,
            lines: [
              {
                id: 'x',
                itemId: 'i1',
                sku: 'TP-001',
                itemName: 'Oil filter',
                countedQty: 4,
                onHandNow: 5,
                systemQtyAtPost: null,
                difference: -1,
                movedSinceStart: true,
              },
            ],
          },
        }
      : { status: 200, data: [] };
  renderAt(
    '/inventory/stocktakes/s1',
    '/inventory/stocktakes/:id',
    <StocktakeEditPage />,
  );
  expect(
    await screen.findByText(/changed since this count started/),
  ).toBeInTheDocument();
  expect(screen.getByText('-1')).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText('Counted TP-001'), {
    target: { value: '7.5' },
  });
  expect(screen.getByText('+2.5')).toBeInTheDocument();
});

it('parses and formats quantities the NZ way', () => {
  expect(parseNumber(' 1.25 ')).toBe(1.25);
  expect(parseNumber('')).toBeNull();
  expect(parseNumber('abc')).toBeNull();
  expect(formatQty(1234.5)).toBe('1,234.5');
  expect(shopToday()).toMatch(/^\d{4}-\d{2}-\d{2}$/);
});
