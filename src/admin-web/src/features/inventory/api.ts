import { apiClient } from '../../api/client';

export type Paged<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};
export type DocumentStatus = 'Draft' | 'Posted' | 'Reversed' | 'Cancelled';
export type DocumentKind = 'Adjustment' | 'Opening';
export type AdjustmentReason =
  'Damage' | 'Lost' | 'Found' | 'Count' | 'Correction' | 'Opening' | 'Other';
export type MovementType =
  | 'Opening'
  | 'Grn'
  | 'GrnReversal'
  | 'Adjustment'
  | 'Issue'
  | 'Return'
  | 'Reversal';

export type GrnLine = {
  id: string;
  lineNo: number;
  itemId: string;
  sku: string;
  itemName: string;
  quantity: number;
  unitCost: number;
  sellingPrice: number | null;
  currentPrice: number | null;
  lineTotal: number;
};
export type Grn = {
  id: string;
  grnNumber: string | null;
  supplierName: string;
  supplierReference: string | null;
  supplierDocumentDate: string | null;
  notes: string | null;
  status: DocumentStatus;
  postedAt: string | null;
  postedBy: string | null;
  reversedAt: string | null;
  reversalReason: string | null;
  total: number;
  lines: GrnLine[];
  version: number;
};
export type GrnSummary = {
  id: string;
  grnNumber: string | null;
  supplierName: string;
  supplierReference: string | null;
  status: DocumentStatus;
  lineCount: number;
  units: number;
  total: number;
  postedAt: string | null;
  updatedAt: string;
};
export type GrnInput = {
  supplierName: string;
  supplierReference: string | null;
  supplierDocumentDate: string | null;
  notes: string | null;
  lines: {
    itemId: string;
    quantity: number;
    unitCost: number;
    sellingPrice: number | null;
  }[];
  version?: number;
};

export type StockDocumentLine = {
  id: string;
  lineNo: number;
  itemId: string;
  sku: string;
  itemName: string;
  qtyChange: number;
  unitCost: number | null;
  sellingPrice: number | null;
};
export type StockDocument = {
  id: string;
  kind: DocumentKind;
  number: string | null;
  reason: AdjustmentReason;
  notes: string | null;
  status: DocumentStatus;
  postedAt: string | null;
  stocktakeId: string | null;
  lines: StockDocumentLine[];
  version: number;
};
export type StockDocumentSummary = {
  id: string;
  kind: DocumentKind;
  number: string | null;
  reason: AdjustmentReason;
  status: DocumentStatus;
  lineCount: number;
  netUnits: number;
  postedAt: string | null;
  updatedAt: string;
};
export type StockDocumentInput = {
  reason: AdjustmentReason;
  notes: string | null;
  lines: {
    itemId: string;
    qtyChange: number;
    unitCost: number | null;
    sellingPrice: number | null;
  }[];
  version?: number;
};
export type ImportRow = {
  line: number;
  sku: string;
  quantity: number | null;
  unitCost: number | null;
  sellingPrice: number | null;
  itemName: string | null;
  error: string | null;
};
export type ImportResult = {
  dryRun: boolean;
  valid: number;
  invalid: number;
  rows: ImportRow[];
  draftId: string | null;
};

export type StocktakeLine = {
  id: string;
  itemId: string;
  sku: string;
  itemName: string;
  countedQty: number;
  onHandNow: number;
  systemQtyAtPost: number | null;
  difference: number;
  movedSinceStart: boolean;
};
export type Stocktake = {
  id: string;
  number: string | null;
  notes: string | null;
  status: DocumentStatus;
  startedAt: string;
  postedAt: string | null;
  adjustmentId: string | null;
  lines: StocktakeLine[];
  version: number;
};
export type StocktakeSummary = {
  id: string;
  number: string | null;
  status: DocumentStatus;
  lineCount: number;
  startedAt: string;
  postedAt: string | null;
};

export type StockRow = {
  itemId: string;
  sku: string;
  name: string;
  categoryName: string;
  onHand: number;
  reorderLevel: number | null;
  reorderQuantity: number | null;
  sellingPrice: number | null;
  averageCost: number;
  stockValue: number;
};
export type StockReport = {
  asOfUtc: string | null;
  totalUnits: number;
  totalValue: number;
  items: Paged<StockRow>;
};
export type StockCardRow = {
  sequence: number;
  occurredAt: string;
  movementType: MovementType;
  qtyChange: number;
  balance: number;
  unitCost: number;
  averageCostAfter: number;
  refType: string;
  refId: string;
  documentNumber: string | null;
  createdBy: string;
};
export type StockCard = {
  itemId: string;
  sku: string;
  name: string;
  openingBalance: number;
  onHand: number;
  averageCost: number;
  sellingPrice: number | null;
  movements: StockCardRow[];
};
export type PriceHistoryRow = {
  sequence: number;
  effectiveFrom: string;
  sellingPrice: number;
  costPrice: number | null;
  sourceType: 'Opening' | 'Grn' | 'PriceChange';
  reason: string | null;
  createdBy: string;
};
export type ItemOption = {
  id: string;
  sku: string;
  name: string;
  onHand: number;
  sellingPrice: number | null;
};

const clean = (params: Record<string, unknown>) =>
  Object.fromEntries(
    Object.entries(params).filter(
      ([, v]) => v !== undefined && v !== '' && v !== null,
    ),
  );

type ListParams = { page?: number; q?: string; status?: DocumentStatus };
const base = (kind: DocumentKind) =>
  kind === 'Opening' ? '/api/opening-stock' : '/api/stock-adjustments';

export const inventoryApi = {
  grns: async (p: ListParams) =>
    (await apiClient.get<Paged<GrnSummary>>('/api/grns', { params: clean(p) }))
      .data,
  grn: async (id: string) => (await apiClient.get<Grn>(`/api/grns/${id}`)).data,
  createGrn: async (input: GrnInput) =>
    (await apiClient.post<Grn>('/api/grns', input)).data,
  updateGrn: async (id: string, input: GrnInput) =>
    (await apiClient.put<Grn>(`/api/grns/${id}`, input)).data,
  postGrn: async (id: string) =>
    (await apiClient.post<Grn>(`/api/grns/${id}/post`)).data,
  cancelGrn: async (id: string) =>
    (await apiClient.post<Grn>(`/api/grns/${id}/cancel`)).data,
  reverseGrn: async (id: string, reason: string) =>
    (await apiClient.post<Grn>(`/api/grns/${id}/reverse`, { reason })).data,
  suppliers: async (q: string) =>
    (
      await apiClient.get<string[]>('/api/suppliers/suggestions', {
        params: clean({ q }),
      })
    ).data,

  documents: async (kind: DocumentKind, p: ListParams) =>
    (
      await apiClient.get<Paged<StockDocumentSummary>>(base(kind), {
        params: clean(p),
      })
    ).data,
  document: async (kind: DocumentKind, id: string) =>
    (await apiClient.get<StockDocument>(`${base(kind)}/${id}`)).data,
  createDocument: async (kind: DocumentKind, input: StockDocumentInput) =>
    (await apiClient.post<StockDocument>(base(kind), input)).data,
  updateDocument: async (
    kind: DocumentKind,
    id: string,
    input: StockDocumentInput,
  ) => (await apiClient.put<StockDocument>(`${base(kind)}/${id}`, input)).data,
  postDocument: async (kind: DocumentKind, id: string) =>
    (await apiClient.post<StockDocument>(`${base(kind)}/${id}/post`)).data,
  cancelDocument: async (kind: DocumentKind, id: string) =>
    (await apiClient.post<StockDocument>(`${base(kind)}/${id}/cancel`)).data,
  importOpening: async (file: File, dryRun: boolean) => {
    const form = new FormData();
    form.append('file', file);
    return (
      await apiClient.post<ImportResult>('/api/opening-stock/import', form, {
        params: { dryRun },
      })
    ).data;
  },

  stocktakes: async (p: ListParams) =>
    (
      await apiClient.get<Paged<StocktakeSummary>>('/api/stocktakes', {
        params: clean(p),
      })
    ).data,
  stocktake: async (id: string) =>
    (await apiClient.get<Stocktake>(`/api/stocktakes/${id}`)).data,
  createStocktake: async (input: {
    notes: string | null;
    lines: { itemId: string; countedQty: number }[];
  }) => (await apiClient.post<Stocktake>('/api/stocktakes', input)).data,
  updateStocktake: async (
    id: string,
    input: {
      notes: string | null;
      lines: { itemId: string; countedQty: number }[];
      version: number;
    },
  ) => (await apiClient.put<Stocktake>(`/api/stocktakes/${id}`, input)).data,
  postStocktake: async (id: string) =>
    (await apiClient.post<Stocktake>(`/api/stocktakes/${id}/post`)).data,
  cancelStocktake: async (id: string) =>
    (await apiClient.post<Stocktake>(`/api/stocktakes/${id}/cancel`)).data,

  stock: async (p: {
    page?: number;
    q?: string;
    categoryId?: string;
    date?: string;
    sort?: string;
    onlyInStock?: boolean;
  }) =>
    (
      await apiClient.get<StockReport>('/api/stock', {
        params: clean({ pageSize: 50, ...p }),
      })
    ).data,
  lowStock: async () =>
    (await apiClient.get<StockRow[]>('/api/stock/low')).data,
  stockCard: async (id: string, from?: string, to?: string) =>
    (
      await apiClient.get<StockCard>(`/api/items/${id}/stock-card`, {
        params: clean({ from, to }),
      })
    ).data,
  priceHistory: async (id: string) =>
    (await apiClient.get<PriceHistoryRow[]>(`/api/items/${id}/price-history`))
      .data,
  changePrice: async (id: string, sellingPrice: number, reason: string) =>
    (
      await apiClient.post<PriceHistoryRow>(`/api/items/${id}/price`, {
        sellingPrice,
        reason,
      })
    ).data,
  searchItems: async (q: string) =>
    (
      await apiClient.get<Paged<ItemOption>>('/api/items', {
        params: clean({ q, pageSize: 20, isActive: true }),
      })
    ).data.items,
};

const qty = new Intl.NumberFormat('en-NZ', { maximumFractionDigits: 3 });
export const formatQty = (value: number) => qty.format(value);

/** Today's date in the shop's time zone, as yyyy-mm-dd. */
export const shopToday = () =>
  new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Pacific/Auckland',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());

/** Parses a typed number; returns null for empty or invalid input. */
export const parseNumber = (text: string) => {
  const trimmed = text.trim();
  if (trimmed === '') return null;
  const value = Number(trimmed);
  return Number.isFinite(value) ? value : null;
};
