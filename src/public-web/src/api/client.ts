import axios from 'axios';
export const createApiClient = (baseURL: string) =>
  axios.create({
    baseURL,
    timeout: 10000,
    headers: { Accept: 'application/json, text/plain' },
  });
export const apiClient = createApiClient(
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080',
);
