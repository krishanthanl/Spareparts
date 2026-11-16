import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { AppProviders } from '../../providers/AppProviders';
import { apiClient } from '../../api/client';
import { describeError, fieldError, mediaUrl } from './api';
import { CategoriesPage } from './CategoriesPage';
import { ItemsPage } from './ItemsPage';

type Handler = (
  url: string,
  method: string,
  data?: unknown,
) => { status: number; data: unknown };
let handler: Handler;

beforeEach(() => {
  apiClient.defaults.adapter = async (config) => {
    const { status, data } = handler(
      config.url ?? '',
      config.method ?? 'get',
      config.data,
    );
    const response: AxiosResponse = {
      status,
      data,
      statusText: '',
      headers: {},
      config: { ...config, headers: new AxiosHeaders() },
    };
    if (status >= 400)
      throw new AxiosError(
        'request failed',
        String(status),
        config,
        null,
        response,
      );
    return response;
  };
});

const page = <T,>(items: T[]) => ({
  items,
  page: 1,
  pageSize: 25,
  totalCount: items.length,
});

it('explains that sign-in is needed when the API answers 401', async () => {
  handler = () => ({ status: 401, data: {} });
  render(
    <AppProviders>
      <CategoriesPage />
    </AppProviders>,
  );
  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Sign-in is needed',
  );
});

it('lists items with thumbnails, categories and deleted state', async () => {
  handler = (url) =>
    url === '/api/categories'
      ? { status: 200, data: page([]) }
      : {
          status: 200,
          data: page([
            {
              id: '1',
              sku: 'TP-001',
              name: 'Smoked LED Headlight',
              categoryId: 'c',
              categoryName: 'Headlights',
              partBrand: null,
              thumbnailUrl: '/media/items/a/b-thumb.webp',
              isActive: true,
              isDeleted: false,
              updatedAt: '',
            },
            {
              id: '2',
              sku: 'TP-002',
              name: 'Old part',
              categoryId: 'c',
              categoryName: 'Headlights',
              partBrand: null,
              thumbnailUrl: null,
              isActive: true,
              isDeleted: true,
              updatedAt: '',
            },
          ]),
        };
  render(
    <AppProviders>
      <ItemsPage />
    </AppProviders>,
  );
  expect(await screen.findByText('TP-001')).toBeInTheDocument();
  expect(screen.getByText('Deleted')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Restore' })).toBeInTheDocument();
  expect(screen.getByText('2 item(s)')).toBeInTheDocument();
});

it('asks before deleting a category', async () => {
  const calls: string[] = [];
  handler = (url, method) => {
    calls.push(`${method} ${url}`);
    return method === 'delete'
      ? { status: 204, data: '' }
      : {
          status: 200,
          data: page([
            {
              id: 'c1',
              name: 'Brakes',
              slug: 'brakes',
              description: null,
              imageUrl: null,
              thumbnailUrl: null,
              sortOrder: 0,
              isActive: true,
              isDeleted: false,
              itemCount: 0,
              version: 1,
              updatedAt: '',
            },
          ]),
        };
  };
  render(
    <AppProviders>
      <CategoriesPage />
    </AppProviders>,
  );
  fireEvent.click(await screen.findByRole('button', { name: 'Delete' }));
  expect(calls).not.toContain('delete /api/categories/c1');
  expect(screen.getByText('Delete Brakes?')).toBeInTheDocument();
  fireEvent.click(screen.getAllByRole('button', { name: 'Delete' }).at(-1)!);
  await waitFor(() => expect(calls).toContain('delete /api/categories/c1'));
});

it('shows server field errors next to the category name', async () => {
  handler = (_url, method) =>
    method === 'post'
      ? {
          status: 400,
          data: { errors: { Name: ['Name must contain letters or numbers.'] } },
        }
      : { status: 200, data: page([]) };
  render(
    <AppProviders>
      <CategoriesPage />
    </AppProviders>,
  );
  fireEvent.click(await screen.findByRole('button', { name: 'New category' }));
  fireEvent.change(screen.getByRole('textbox', { name: /name/i }), {
    target: { value: '!!!' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  expect(
    await screen.findByText('Name must contain letters or numbers.'),
  ).toBeInTheDocument();
});

it('never shows raw server errors', () => {
  const error = new AxiosError('boom', '500', undefined, null, {
    status: 500,
    data: { detail: 'Npgsql password=secret' },
  } as AxiosResponse);
  expect(describeError(error).message).toBe('Something went wrong. Try again.');
  expect(fieldError(describeError(error), 'Name')).toBeUndefined();
  expect(mediaUrl('/media/x.webp')).toBe('http://localhost:5080/media/x.webp');
});
