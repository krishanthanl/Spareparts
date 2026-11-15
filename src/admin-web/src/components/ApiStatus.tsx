import type { AxiosInstance } from 'axios';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '../api/client';
export function ApiStatus({ client = apiClient }: { client?: AxiosInstance }) {
  const query = useQuery({
    queryKey: ['api-status', client.defaults.baseURL],
    queryFn: async () =>
      (await client.get<string>('/health/live', { responseType: 'text' })).data,
  });
  return (
    <section aria-label="Service connection">
      <p role="status">
        {query.isPending
          ? 'Checking connection'
          : query.isError
            ? 'Connection unavailable'
            : 'Connected'}
      </p>
      {query.isError && (
        <>
          <p>Check that the shop service is running, then try again.</p>
          <button
            disabled={query.isFetching}
            onClick={() => {
              void query.refetch();
            }}
          >
            Try again
          </button>
        </>
      )}
    </section>
  );
}
