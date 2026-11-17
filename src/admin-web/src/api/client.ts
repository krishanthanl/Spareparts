import axios, { type AxiosInstance } from 'axios';
export const createApiClient = (baseURL: string) =>
  axios.create({
    baseURL,
    timeout: 10000,
    // Sends the HttpOnly refresh cookie to /api/auth. Nothing readable is stored in the browser.
    withCredentials: true,
    headers: { Accept: 'application/json, text/plain' },
  });
export const apiClient: AxiosInstance = createApiClient(
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080',
);
