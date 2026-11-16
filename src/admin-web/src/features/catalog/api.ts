import axios from 'axios';
import { apiClient } from '../../api/client';

export type Paged<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export type Category = {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  imageUrl: string | null;
  thumbnailUrl: string | null;
  sortOrder: number;
  isActive: boolean;
  isDeleted: boolean;
  itemCount: number;
  version: number;
  updatedAt: string;
};

export type CategoryInput = {
  name: string;
  description: string | null;
  sortOrder: number;
  isActive: boolean;
  version?: number;
};

export type ItemSummary = {
  id: string;
  sku: string;
  name: string;
  categoryId: string;
  categoryName: string;
  partBrand: string | null;
  thumbnailUrl: string | null;
  isActive: boolean;
  isDeleted: boolean;
  updatedAt: string;
};

export type Fitment = {
  id?: string;
  vehicleModelId: string;
  make?: string;
  model?: string;
  yearFrom: number | null;
  yearTo: number | null;
};

export type ItemImage = {
  id: string;
  url: string;
  thumbnailUrl: string;
  width: number;
  height: number;
  isPrimary: boolean;
  sortOrder: number;
  altText: string | null;
};

export type Item = {
  id: string;
  sku: string;
  name: string;
  description: string | null;
  categoryId: string;
  categoryName: string;
  partBrand: string | null;
  reorderLevel: number | null;
  reorderQuantity: number | null;
  isActive: boolean;
  isDeleted: boolean;
  version: number;
  updatedAt: string;
  fitments: Fitment[];
  images: ItemImage[];
};

export type ItemInput = {
  sku: string;
  name: string;
  description: string | null;
  categoryId: string;
  partBrand: string | null;
  reorderLevel: number | null;
  reorderQuantity: number | null;
  isActive: boolean;
  fitments: {
    vehicleModelId: string;
    yearFrom: number | null;
    yearTo: number | null;
  }[];
  version?: number;
};

export type VehicleModel = { id: string; makeId: string; name: string };
export type VehicleMake = { id: string; name: string; models: VehicleModel[] };

export type ListParams = {
  page?: number;
  pageSize?: number;
  sort?: string;
  q?: string;
  includeDeleted?: boolean;
  categoryId?: string;
};

const clean = (params: ListParams) =>
  Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== ''),
  );

/** Image URLs come back as API-relative paths such as /media/items/... */
export const mediaUrl = (path: string | null | undefined) =>
  path ? new URL(path, apiClient.defaults.baseURL).toString() : undefined;

export const catalogApi = {
  categories: async (params: ListParams) =>
    (
      await apiClient.get<Paged<Category>>('/api/categories', {
        params: clean(params),
      })
    ).data,
  createCategory: async (input: CategoryInput) =>
    (await apiClient.post<Category>('/api/categories', input)).data,
  updateCategory: async (id: string, input: CategoryInput) =>
    (await apiClient.put<Category>(`/api/categories/${id}`, input)).data,
  deleteCategory: async (id: string) => {
    await apiClient.delete(`/api/categories/${id}`);
  },
  restoreCategory: async (id: string) =>
    (await apiClient.post<Category>(`/api/categories/${id}/restore`)).data,
  setCategoryImage: async (id: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return (await apiClient.put<Category>(`/api/categories/${id}/image`, form))
      .data;
  },
  items: async (params: ListParams) =>
    (
      await apiClient.get<Paged<ItemSummary>>('/api/items', {
        params: clean(params),
      })
    ).data,
  item: async (id: string) =>
    (await apiClient.get<Item>(`/api/items/${id}`)).data,
  createItem: async (input: ItemInput) =>
    (await apiClient.post<Item>('/api/items', input)).data,
  updateItem: async (id: string, input: ItemInput) =>
    (await apiClient.put<Item>(`/api/items/${id}`, input)).data,
  deleteItem: async (id: string) => {
    await apiClient.delete(`/api/items/${id}`);
  },
  restoreItem: async (id: string) =>
    (await apiClient.post<Item>(`/api/items/${id}/restore`)).data,
  addImage: async (id: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return (await apiClient.post<Item>(`/api/items/${id}/images`, form)).data;
  },
  setPrimaryImage: async (id: string, imageId: string) =>
    (await apiClient.put<Item>(`/api/items/${id}/images/${imageId}/primary`))
      .data,
  removeImage: async (id: string, imageId: string) =>
    (await apiClient.delete<Item>(`/api/items/${id}/images/${imageId}`)).data,
  makes: async () =>
    (await apiClient.get<VehicleMake[]>('/api/vehicle-makes')).data,
  createMake: async (name: string) =>
    (await apiClient.post<VehicleMake>('/api/vehicle-makes', { name })).data,
  createModel: async (makeId: string, name: string) =>
    (
      await apiClient.post<VehicleModel>(
        `/api/vehicle-makes/${makeId}/models`,
        { name },
      )
    ).data,
};

export type ApiProblem = {
  message: string;
  fieldErrors: Record<string, string[]>;
  signInRequired: boolean;
};

/** Turns API errors into safe, readable text. Never shows raw server output. */
export function describeError(error: unknown): ApiProblem {
  if (axios.isAxiosError(error)) {
    const status = error.response?.status;
    const body = error.response?.data as
      | { detail?: string; title?: string; errors?: Record<string, string[]> }
      | undefined;
    if (status === 401 || status === 403)
      return {
        message:
          status === 401
            ? 'Sign-in is needed to manage the catalogue. Staff sign-in arrives with Phase 1.'
            : 'Your account does not have permission to manage the catalogue.',
        fieldErrors: {},
        signInRequired: true,
      };
    if (status === 400 && body?.errors)
      return {
        message: 'Check the highlighted fields.',
        fieldErrors: body.errors,
        signInRequired: false,
      };
    if ((status === 404 || status === 409) && body?.detail)
      return { message: body.detail, fieldErrors: {}, signInRequired: false };
    if (!error.response)
      return {
        message:
          'The shop service is not reachable. Check it is running, then try again.',
        fieldErrors: {},
        signInRequired: false,
      };
  }
  return {
    message: 'Something went wrong. Try again.',
    fieldErrors: {},
    signInRequired: false,
  };
}

/** Matches ASP.NET field keys case-insensitively, e.g. "Sku" or "Fitments[0].YearTo". */
export const fieldError = (problem: ApiProblem | null, field: string) => {
  if (!problem) return undefined;
  const key = Object.keys(problem.fieldErrors).find(
    (k) => k.toLowerCase() === field.toLowerCase(),
  );
  return key ? problem.fieldErrors[key][0] : undefined;
};
