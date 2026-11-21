import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import {
  AxiosError,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AppProviders } from '../../providers/AppProviders';
import { apiClient } from '../../api/client';
import { AuthContext } from '../auth/useAuth';
import { OrderListPage, OrderPage } from './OrderPages';
import { DailyTakingsPage } from './SalesPages';
import type { Order } from './api';

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

const invoice = (patch: Partial<Order> = {}): Order => ({
  id: 'o1',
  invoiceNumber: 'INV-000007',
  source: 'Telephone',
  status: 'Confirmed',
  paymentStatus: 'PartiallyPaid',
  fulfillmentStatus: 'NotIssued',
  paymentTerms: 'Immediate',
  customerId: null,
  customerName: 'Sam Driver',
  customerPhone: '021 555',
  customerEmail: null,
  customerCompany: null,
  billingAddress: null,
  deliveryMethod: 'Pickup',
  deliveryAddress: null,
  deliveryCharge: 0,
  subtotal: 50,
  lineDiscountTotal: 0,
  orderDiscount: 0,
  discountReason: null,
  total: 50,
  amountPaid: 20,
  balanceDue: 30,
  gstAmount: 0,
  notes: null,
  confirmedAt: '2026-11-20T01:00:00Z',
  confirmedBy: 'Alex',
  cancelledAt: null,
  cancelReason: null,
  lines: [
    {
      id: 'l1',
      lineNo: 1,
      itemId: 'i1',
      sku: 'BP-1',
      description: 'Brake pads',
      quantity: 2,
      listPrice: 25,
      unitPrice: 25,
      lineDiscount: 0,
      orderDiscountShare: 0,
      lineTotal: 50,
      discountReason: null,
      qtyIssued: 0,
      qtyToIssue: 2,
      available: 3,
    },
  ],
  issues: [],
  payments: [
    {
      id: 'p1',
      receiptNumber: 'RCT-000003',
      method: 'Card',
      amount: 20,
      reference: null,
      receivedAt: '2026-11-20T01:05:00Z',
      receivedBy: 'Alex',
      voidsPaymentId: null,
      voidReason: null,
      isVoided: false,
    },
  ],
  version: 3,
  createdAt: '2026-11-20T00:59:00Z',
  updatedAt: '2026-11-20T01:05:00Z',
  ...patch,
});

test('order list sends the filters and shows cash sales', async () => {
  handler = () => ({
    status: 200,
    data: {
      items: [
        {
          id: 'o1',
          invoiceNumber: 'INV-000001',
          source: 'Counter',
          status: 'Completed',
          paymentStatus: 'Paid',
          fulfillmentStatus: 'Issued',
          customerName: null,
          customerPhone: null,
          total: 25,
          amountPaid: 25,
          balanceDue: 0,
          confirmedAt: '2026-11-20T01:00:00Z',
          updatedAt: '2026-11-20T01:00:00Z',
        },
      ],
      page: 1,
      pageSize: 25,
      totalCount: 1,
    },
  });
  renderAt('/sales/invoices', '/sales/invoices', <OrderListPage />);
  expect(await screen.findByText('INV-000001')).toBeInTheDocument();
  expect(screen.getByText('Cash sale')).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText('Payment'), {
    target: { value: 'Unpaid' },
  });
  await waitFor(() =>
    expect(calls.at(-1)?.params).toMatchObject({
      paymentStatus: 'Unpaid',
      page: 1,
    }),
  );
});

test('a confirmed invoice offers issue and payment, and payment sends an idempotency key', async () => {
  handler = (call) =>
    call.url.endsWith('/payments')
      ? {
          status: 200,
          data: invoice({
            amountPaid: 50,
            balanceDue: 0,
            paymentStatus: 'Paid',
          }),
        }
      : { status: 200, data: invoice() };
  renderAt('/sales/invoices/o1', '/sales/invoices/:id', <OrderPage />, [
    'sales.invoice',
    'sales.issue',
    'payments.record',
  ]);
  expect(await screen.findByText('Invoice INV-000007')).toBeInTheDocument();
  expect(
    screen.getByRole('button', { name: 'Issue stock' }),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: 'Cancel invoice' }),
  ).not.toBeInTheDocument(); // needs sales.cancel
  expect(
    screen.getByText('Not GST registered — no GST charged'),
  ).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Record payment' }));
  expect(screen.getByLabelText('Amount $')).toHaveValue('30.00');
  fireEvent.click(screen.getByRole('button', { name: /^Record \$30\.00/ }));
  await waitFor(() =>
    expect(calls.some((c) => c.url === '/api/orders/o1/payments')).toBe(true),
  );
  const body = JSON.parse(
    calls.find((c) => c.url === '/api/orders/o1/payments')!.data as string,
  );
  expect(body).toMatchObject({ method: 'Cash', amount: 30 });
  expect(body.idempotencyKey).toMatch(/[0-9a-f-]{36}/);
});

test('a draft opens in the editor with server-calculated totals', async () => {
  handler = (call) =>
    call.url === '/api/orders/preview'
      ? {
          status: 200,
          data: {
            lines: [
              {
                itemId: 'i1',
                sku: 'BP-1',
                description: 'Brake pads',
                quantity: 2,
                listPrice: 25,
                unitPrice: 25,
                lineDiscount: 0,
                orderDiscountShare: 0,
                lineTotal: 50,
                available: 3,
              },
            ],
            subtotal: 50,
            lineDiscountTotal: 0,
            orderDiscount: 0,
            deliveryCharge: 0,
            total: 50,
            needsDiscountPermission: false,
          },
        }
      : call.url.startsWith('/api/customers')
        ? {
            status: 200,
            data: { items: [], page: 1, pageSize: 25, totalCount: 0 },
          }
        : {
            status: 200,
            data: invoice({
              status: 'Draft',
              invoiceNumber: null,
              payments: [],
              amountPaid: 0,
            }),
          };
  renderAt('/sales/invoices/o1', '/sales/invoices/:id', <OrderPage />, [
    'sales.invoice',
  ]);
  expect(await screen.findByText('Draft order')).toBeInTheDocument();
  expect(
    await screen.findByText('3 available · list $25.00'),
  ).toBeInTheDocument();
  expect(screen.getAllByText('$50.00').length).toBeGreaterThan(0);
  expect(screen.queryByLabelText('Order discount $')).not.toBeInTheDocument(); // needs sales.discount
});

test('daily takings shows totals by method', async () => {
  handler = () => ({
    status: 200,
    data: {
      from: '2026-11-20',
      to: '2026-11-20',
      methods: [
        { method: 'Cash', received: 120, voided: 0, net: 120 },
        { method: 'Card', received: 80, voided: 20, net: 60 },
      ],
      received: 200,
      voided: 20,
      refundsPaid: 0,
      net: 180,
      days: [
        {
          date: '2026-11-20',
          methods: [],
          received: 200,
          voided: 20,
          refundsPaid: 0,
          net: 180,
          payments: [],
        },
      ],
    },
  });
  renderAt('/sales/takings', '/sales/takings', <DailyTakingsPage />);
  expect(await screen.findByText('EFTPOS / card')).toBeInTheDocument();
  expect(
    screen.getByText('$180.00', { selector: 'strong' }),
  ).toBeInTheDocument();
});
