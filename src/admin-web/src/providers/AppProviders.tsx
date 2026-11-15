import { useState, type PropsWithChildren } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ThemeProvider, createTheme } from '@mui/material/styles';
import CssBaseline from '@mui/material/CssBaseline';
const theme = createTheme({
  palette: {
    primary: { main: '#225c9a' },
    background: { default: '#f4f7fa' },
    text: { primary: '#18354a' },
  },
  typography: { fontFamily: 'Segoe UI, system-ui, sans-serif' },
  shape: { borderRadius: 8 },
});
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
  return (
    <QueryClientProvider client={client}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        {children}
      </ThemeProvider>
    </QueryClientProvider>
  );
}
