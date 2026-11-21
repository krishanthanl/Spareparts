import {
  render,
  screen,
  fireEvent,
  waitFor,
  within,
} from '@testing-library/react';
import {
  AxiosError,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { App } from '../../App';
import { AppProviders } from '../../providers/AppProviders';
import { apiClient } from '../../api/client';
import { tokenStore, type AuthResponse, type MenuNode } from './session';
import { effectiveAccess } from '../users/api';

type Call = {
  url: string;
  method: string;
  headers: Record<string, unknown>;
  data?: unknown;
};
type Handler = (call: Call) => { status: number; data?: unknown };
let handler: Handler;
let calls: Call[];

beforeEach(() => {
  calls = [];
  tokenStore.set(null);
  apiClient.defaults.adapter = async (config: InternalAxiosRequestConfig) => {
    const call = {
      url: config.url ?? '',
      method: config.method ?? 'get',
      headers: Object.fromEntries(Object.entries(config.headers.toJSON())),
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

const session = (
  permissions: string[],
  mustChangePassword = false,
): AuthResponse => ({
  accessToken: 'token-' + permissions.length,
  expiresAt: new Date(Date.now() + 600000).toISOString(),
  user: {
    id: 'u1',
    email: 'kim@shop.nz',
    displayName: 'Kim',
    mustChangePassword,
    roles: ['StoreKeeper'],
    permissions,
  },
});

const menus: MenuNode[] = [
  { key: 'dashboard', title: 'Workspace', path: '/', icon: null, children: [] },
  {
    key: 'catalog',
    title: 'Catalogue',
    path: null,
    icon: null,
    children: [
      {
        key: 'catalog.items',
        title: 'Items',
        path: '/items',
        icon: null,
        children: [],
      },
    ],
  },
  {
    key: 'shop',
    title: 'Shop details',
    path: '/shop-details',
    icon: null,
    children: [],
  },
];

const renderApp = (path = '/') =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <AppProviders>
        <App />
      </AppProviders>
    </MemoryRouter>,
  );

it('shows sign-in when there is no session to restore', async () => {
  handler = () => ({ status: 401 });
  renderApp();
  expect(
    await screen.findByRole('heading', { name: 'Sign in' }),
  ).toBeInTheDocument();
  const refresh = calls.find((c) => c.url === '/api/auth/refresh')!;
  expect(refresh.headers['X-CSRF-Token']).toBe('1');
});

it('restores the session on reload and builds the sidebar from the allowed menus', async () => {
  handler = ({ url }) =>
    url === '/api/auth/refresh'
      ? { status: 200, data: session(['dashboard.view', 'catalog.manage']) }
      : url === '/api/auth/menus'
        ? { status: 200, data: menus }
        : { status: 404 };
  renderApp();
  expect(
    await screen.findByRole('heading', { name: 'Shop workspace' }),
  ).toBeInTheDocument();
  const nav = screen.getByRole('navigation');
  expect(within(nav).getByRole('link', { name: 'Items' })).toBeInTheDocument();
  expect(
    within(nav).queryByRole('link', { name: 'Users' }),
  ).not.toBeInTheDocument();
  fireEvent.click(within(nav).getByRole('link', { name: 'Shop details' }));
  expect(screen.getByText('Not GST registered')).toBeInTheDocument();
  // The token is kept in memory and sent as a bearer header, never put in storage.
  expect(
    calls.find((c) => c.url === '/api/auth/menus')!.headers.Authorization,
  ).toBe('Bearer token-2');
  expect(localStorage.length + sessionStorage.length).toBe(0);
});

it('blocks pages the user has no permission for', async () => {
  handler = ({ url }) =>
    url === '/api/auth/refresh'
      ? { status: 200, data: session(['dashboard.view']) }
      : url === '/api/auth/menus'
        ? { status: 200, data: [] }
        : { status: 404 };
  renderApp('/users');
  expect(
    await screen.findByRole('heading', { name: 'Not available' }),
  ).toBeInTheDocument();
  expect(calls.some((c) => c.url.startsWith('/api/users'))).toBe(false);
});

it('signs in and shows the generic error on a failed attempt', async () => {
  let attempts = 0;
  handler = ({ url }) => {
    if (url === '/api/auth/refresh') return { status: 401 };
    if (url === '/api/auth/login')
      return ++attempts === 1
        ? { status: 401 }
        : { status: 200, data: session(['dashboard.view']) };
    if (url === '/api/auth/menus') return { status: 200, data: menus };
    return { status: 404 };
  };
  renderApp();
  fireEvent.change(await screen.findByLabelText(/Email/), {
    target: { value: 'kim@shop.nz' },
  });
  fireEvent.change(screen.getByLabelText(/Password/), {
    target: { value: 'wrong-password' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Email or password is incorrect.',
  );
  fireEvent.change(screen.getByLabelText(/Password/), {
    target: { value: 'right-password' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
  expect(
    await screen.findByRole('heading', { name: 'Shop workspace' }),
  ).toBeInTheDocument();
});

it('makes a user with a temporary password choose a new one first', async () => {
  handler = ({ url }) =>
    url === '/api/auth/refresh'
      ? { status: 200, data: session([], true) }
      : { status: 404 };
  renderApp();
  expect(
    await screen.findByRole('heading', { name: 'Choose a new password' }),
  ).toBeInTheDocument();
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
});

it('refreshes once and retries when a call gets 401', async () => {
  let refreshes = 0;
  let itemCalls = 0;
  handler = ({ url }) => {
    if (url === '/api/auth/refresh')
      return { status: 200, data: session(['catalog.manage']) };
    if (url === '/api/items')
      return ++itemCalls === 1
        ? { status: 401 }
        : { status: 200, data: { items: [] } };
    return { status: 404 };
  };
  const original = handler;
  handler = (call) => {
    if (call.url === '/api/auth/refresh') refreshes++;
    return original(call);
  };
  const result = await apiClient.get('/api/items');
  expect(result.status).toBe(200);
  expect(refreshes).toBe(1);
});

it('shares one refresh between simultaneous 401s', async () => {
  let refreshes = 0;
  const failed = new Set<number>();
  handler = ({ url, headers }) => {
    if (url === '/api/auth/refresh') {
      refreshes++;
      return { status: 200, data: session(['catalog.manage']) };
    }
    const n = Number(url.split('/').pop());
    if (!headers.Authorization && !failed.has(n)) {
      failed.add(n);
      return { status: 401 };
    }
    return { status: 200, data: {} };
  };
  await Promise.all([1, 2, 3].map((n) => apiClient.get(`/api/items/${n}`)));
  await waitFor(() => expect(refreshes).toBe(1));
});

it('deny always wins over the role', () => {
  expect(effectiveAccess(true, 'Deny')).toBe(false);
  expect(effectiveAccess(false, 'Allow')).toBe(true);
  expect(effectiveAccess(true, 'Inherit')).toBe(true);
});
