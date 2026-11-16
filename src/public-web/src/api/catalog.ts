import { useQuery } from '@tanstack/react-query';
import { apiClient } from './client';

export type PublicCategory = {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  thumbnailUrl: string | null;
  itemCount: number;
};

export type PublicItem = {
  id: string;
  sku: string;
  name: string;
  description: string | null;
  categoryName: string;
  categorySlug: string;
  partBrand: string | null;
  imageUrl: string | null;
  thumbnailUrl: string | null;
};

export type Paged<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export const PAGE_SIZE = 24;

/** Image paths come back API-relative (/media/...). */
export const mediaUrl = (path: string | null) =>
  path ? new URL(path, apiClient.defaults.baseURL).toString() : undefined;

export const publicCatalog = {
  categories: async () =>
    (await apiClient.get<PublicCategory[]>('/api/public/categories')).data,
  items: async (params: { category?: string; q?: string; page: number }) =>
    (
      await apiClient.get<Paged<PublicItem>>('/api/public/items', {
        params: {
          category: params.category || undefined,
          q: params.q || undefined,
          page: params.page,
          pageSize: PAGE_SIZE,
        },
      })
    ).data,
};

export function useCategories() {
  return useQuery({
    queryKey: ['public-categories'],
    queryFn: publicCatalog.categories,
    staleTime: 5 * 60_000,
  });
}
