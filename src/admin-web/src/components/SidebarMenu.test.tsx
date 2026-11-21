import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { MenuNode } from '../features/auth/session';
import { SidebarMenu } from './SidebarMenu';

const link = (key: string, title: string, path: string): MenuNode => ({
  key,
  title,
  path,
  icon: null,
  children: [],
});
const menus: MenuNode[] = [
  link('dashboard', 'Workspace', '/'),
  {
    key: 'inventory',
    title: 'Inventory',
    path: null,
    icon: null,
    children: [
      link('inventory.grn', 'Goods received', '/inventory/grn'),
      link('inventory.stock', 'Stock report', '/inventory/stock'),
    ],
  },
  {
    key: 'admin',
    title: 'Administration',
    path: null,
    icon: null,
    children: [link('admin.users', 'Users', '/users')],
  },
];

const renderAt = (path: string) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <SidebarMenu menus={menus} />
    </MemoryRouter>,
  );

beforeEach(() => localStorage.clear());

test('groups collapse and expand, and the choice is remembered', () => {
  const { unmount } = renderAt('/');
  const inventory = screen.getByRole('button', { name: /Inventory/ });
  expect(inventory).toHaveAttribute('aria-expanded', 'true');
  expect(
    screen.getByRole('link', { name: 'Goods received' }),
  ).toBeInTheDocument();

  fireEvent.click(inventory);
  expect(inventory).toHaveAttribute('aria-expanded', 'false');
  expect(
    screen.queryByRole('link', { name: 'Goods received' }),
  ).not.toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Users' })).toBeInTheDocument(); // other groups unaffected

  unmount();
  renderAt('/');
  expect(screen.getByRole('button', { name: /Inventory/ })).toHaveAttribute(
    'aria-expanded',
    'false',
  );

  fireEvent.click(screen.getByRole('button', { name: /Inventory/ }));
  expect(
    screen.getByRole('link', { name: 'Stock report' }),
  ).toBeInTheDocument();
});

test('the group holding the current page stays open', () => {
  localStorage.setItem('admin.nav.collapsed', JSON.stringify(['inventory']));
  renderAt('/inventory/grn/123');
  expect(screen.getByRole('button', { name: /Inventory/ })).toHaveAttribute(
    'aria-expanded',
    'true',
  );
  expect(
    screen.getByRole('link', { name: 'Goods received' }),
  ).toBeInTheDocument();
});

test('bad stored data is ignored', () => {
  localStorage.setItem('admin.nav.collapsed', '{not json');
  renderAt('/');
  expect(screen.getByRole('button', { name: /Inventory/ })).toHaveAttribute(
    'aria-expanded',
    'true',
  );
});
