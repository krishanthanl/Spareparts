import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { App } from './App';
import { AppProviders } from './providers/AppProviders';
import { createApiClient } from './api/client';
import { ApiStatus } from './components/ApiStatus';
import { ErrorBoundary } from './components/ErrorBoundary';
import axios from 'axios';

it('renders navigation and the shop setup without business features', () => {
  render(
    <MemoryRouter>
      <AppProviders>
        <App />
      </AppProviders>
    </MemoryRouter>,
  );
  expect(
    screen.getByRole('heading', { name: 'Shop workspace' }),
  ).toBeInTheDocument();
  fireEvent.click(
    within(screen.getByRole('navigation')).getByRole('link', {
      name: 'Shop details',
    }),
  );
  expect(screen.getByText('Not GST registered')).toBeInTheDocument();
  expect(screen.getByText('Pacific/Auckland')).toBeInTheDocument();
});
it('shows a safe network error and offers retry', async () => {
  const client = createApiClient('http://localhost:5080');
  client.defaults.adapter = async () => {
    throw new axios.AxiosError('internal private detail');
  };
  render(
    <AppProviders>
      <ApiStatus client={client} />
    </AppProviders>,
  );
  expect(screen.getByRole('status')).toHaveTextContent('Checking connection');
  expect(await screen.findByText('Connection unavailable')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  expect(screen.queryByText('internal private detail')).not.toBeInTheDocument();
});
it('contains a rendering failure and provides recovery', () => {
  const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
  function Broken(): never {
    throw new Error('private rendering detail');
  }
  render(
    <ErrorBoundary>
      <Broken />
    </ErrorBoundary>,
  );
  expect(screen.getByRole('alert')).toHaveTextContent(
    'This page could not be displayed',
  );
  expect(
    screen.getByRole('button', { name: 'Reload page' }),
  ).toBeInTheDocument();
  expect(
    screen.queryByText('private rendering detail'),
  ).not.toBeInTheDocument();
  spy.mockRestore();
});
