import { useState, type PropsWithChildren } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

export function AppProviders({ children }: PropsWithChildren) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            retry: false,
            refetchOnWindowFocus: false,
            staleTime: 30000,
          },
        },
      }),
  );
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
