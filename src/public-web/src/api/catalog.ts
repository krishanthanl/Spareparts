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
  price: number | null;
  availability: 'InStock' | 'LowStock' | 'OutOfStock';
};

export type PartsFilters = Partial<
  Record<
    | 'category'
    | 'q'
    | 'brand'
    | 'makeId'
    | 'modelId'
    | 'year'
    | 'minPrice'
    | 'maxPrice'
    | 'availability'
    | 'sort',
    string
  >
>;
export type VehicleOption = { id: string; name: string };
export type ItemDetail = {
  item: PublicItem;
  images: {
    id: string;
    url: string;
    thumbnailUrl: string;
    width: number;
    height: number;
    altText: string | null;
  }[];
  fitments: {
    id: string;
    make: string;
    model: string;
    yearFrom: number | null;
    yearTo: number | null;
  }[];
  related: PublicItem[];
};
export type CartQuantity = { itemId: string; quantity: number };
export type CartResult = {
  itemId: string;
  item: PublicItem | null;
  accepted: boolean;
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
  items: async (
    params: PartsFilters & { page: number },
    signal?: AbortSignal,
  ) =>
    (
      await apiClient.get<Paged<PublicItem>>('/api/public/items', {
        params: { ...params, pageSize: PAGE_SIZE },
        signal,
      })
    ).data,
  detail: async (id: string, signal?: AbortSignal) =>
    (
      await apiClient.get<ItemDetail>(
        `/api/public/items/${encodeURIComponent(id)}`,
        { signal },
      )
    ).data,
  makes: async () =>
    (await apiClient.get<VehicleOption[]>('/api/public/vehicles/makes')).data,
  models: async (makeId: string, signal?: AbortSignal) =>
    (
      await apiClient.get<VehicleOption[]>('/api/public/vehicles/models', {
        params: { makeId },
        signal,
      })
    ).data,
  validate: async (lines: CartQuantity[], signal?: AbortSignal) =>
    (
      await apiClient.post<CartResult[]>(
        '/api/public/cart/validate',
        { lines },
        { signal },
      )
    ).data,
};

export function useCategories() {
  return useQuery({
    queryKey: ['public-categories'],
    queryFn: publicCatalog.categories,
    staleTime: 60_000,
  });
}
