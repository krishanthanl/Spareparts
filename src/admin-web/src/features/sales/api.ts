import { apiClient } from '../../api/client';
import type { Paged } from '../inventory/api';

export type OrderStatus = 'Draft' | 'Confirmed' | 'Completed' | 'Cancelled';
export type PaymentStatus = 'Unpaid' | 'PartiallyPaid' | 'Paid' | 'Overpaid';
export type FulfillmentStatus = 'NotIssued' | 'PartiallyIssued' | 'Issued';
export type SalesSource = 'Counter' | 'Telephone' | 'Admin' | 'Online';
export type PaymentTerms = 'Immediate' | 'OnDelivery';
export type DeliveryMethod = 'Pickup' | 'Delivery';
export type PaymentMethod = 'Cash' | 'Card' | 'BankTransfer' | 'Other';

export const paymentMethods: { value: PaymentMethod; label: string }[] = [
  { value: 'Cash', label: 'Cash' },
  { value: 'Card', label: 'EFTPOS / card' },
  { value: 'BankTransfer', label: 'Bank transfer' },
  { value: 'Other', label: 'Other' },
];
export const methodLabel = (m: PaymentMethod) =>
  paymentMethods.find((x) => x.value === m)?.label ?? m;
export const sources: { value: SalesSource; label: string }[] = [
  { value: 'Counter', label: 'Counter' },
  { value: 'Telephone', label: 'Phone order' },
  { value: 'Admin', label: 'Other (staff)' },
];
export const sourceLabel = (s: SalesSource) =>
  s === 'Online' ? 'Online' : (sources.find((x) => x.value === s)?.label ?? s);
export const paymentStatusLabel: Record<PaymentStatus, string> = {
  Unpaid: 'Unpaid',
  PartiallyPaid: 'Part paid',
  Paid: 'Paid',
  Overpaid: 'Overpaid',
};
export const fulfillmentLabel: Record<FulfillmentStatus, string> = {
  NotIssued: 'Not issued',
  PartiallyIssued: 'Part issued',
  Issued: 'Issued',
};

export type Customer = {
  id: string;
  name: string;
  phone: string | null;
  email: string | null;
  company: string | null;
  billingAddress: string | null;
  deliveryAddress: string | null;
  notes: string | null;
  isActive: boolean;
  version: number;
  updatedAt: string;
};
export type CustomerInput = Omit<Customer, 'id' | 'version' | 'updatedAt'> & {
  version?: number;
};

export type OrderLineInput = {
  itemId: string;
  quantity: number;
  unitPrice?: number | null;
  discountAmount?: number | null;
  discountPercent?: number | null;
  discountReason?: string | null;
};
export type OrderInput = {
  source: SalesSource;
  customerId: string | null;
  customerName: string | null;
  customerPhone: string | null;
  customerEmail: string | null;
  customerCompany: string | null;
  billingAddress: string | null;
  paymentTerms: PaymentTerms;
  deliveryMethod: DeliveryMethod;
  deliveryAddress: string | null;
  deliveryCharge: number;
  orderDiscount: number;
  discountReason: string | null;
  notes: string | null;
  lines: OrderLineInput[];
  version?: number;
  deliveryPostcode?: string | null;
  deliveryIsRural?: boolean;
  deliveryChargeReason?: string | null;
};

export type OrderLine = {
  id: string;
  lineNo: number;
  itemId: string;
  sku: string;
  description: string;
  quantity: number;
  listPrice: number;
  unitPrice: number;
  lineDiscount: number;
  orderDiscountShare: number;
  lineTotal: number;
  discountReason: string | null;
  qtyIssued: number;
  qtyToIssue: number;
  available: number | null;
};
export type StockIssue = {
  id: string;
  issueNumber: string;
  postedAt: string;
  postedBy: string;
  lines: {
    orderLineId: string;
    itemId: string;
    sku: string;
    quantity: number;
  }[];
};
export type Payment = {
  id: string;
  receiptNumber: string;
  method: PaymentMethod;
  amount: number;
  reference: string | null;
  receivedAt: string;
  receivedBy: string;
  voidsPaymentId: string | null;
  voidReason: string | null;
  isVoided: boolean;
};
export type Order = {
  id: string;
  invoiceNumber: string | null;
  source: SalesSource;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  fulfillmentStatus: FulfillmentStatus;
  paymentTerms: PaymentTerms;
  customerId: string | null;
  customerName: string | null;
  customerPhone: string | null;
  customerEmail: string | null;
  customerCompany: string | null;
  billingAddress: string | null;
  deliveryMethod: DeliveryMethod;
  deliveryAddress: string | null;
  deliveryCharge: number;
  subtotal: number;
  lineDiscountTotal: number;
  orderDiscount: number;
  discountReason: string | null;
  total: number;
  amountPaid: number;
  balanceDue: number;
  gstAmount: number;
  notes: string | null;
  confirmedAt: string | null;
  confirmedBy: string | null;
  cancelledAt: string | null;
  cancelReason: string | null;
  lines: OrderLine[];
  issues: StockIssue[];
  payments: Payment[];
  version: number;
  createdAt: string;
  updatedAt: string;
  creditTotal: number;
  refundedTotal: number;
  deliveryPostcode: string | null;
  deliveryIsRural: boolean;
  deliveryZoneName: string | null;
  deliveryPromise: string | null;
  deliveryChargeReason: string | null;
};
export type OrderSummary = {
  id: string;
  invoiceNumber: string | null;
  source: SalesSource;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  fulfillmentStatus: FulfillmentStatus;
  customerName: string | null;
  customerPhone: string | null;
  total: number;
  amountPaid: number;
  balanceDue: number;
  confirmedAt: string | null;
  updatedAt: string;
};
export type OrderPreview = {
  lines: {
    itemId: string;
    sku: string;
    description: string;
    quantity: number;
    listPrice: number;
    unitPrice: number;
    lineDiscount: number;
    orderDiscountShare: number;
    lineTotal: number;
    available: number;
  }[];
  subtotal: number;
  lineDiscountTotal: number;
  orderDiscount: number;
  deliveryCharge: number;
  total: number;
  needsDiscountPermission: boolean;
  deliveryQuote: DeliveryQuote | null;
};
export type SaleItem = {
  itemId: string;
  sku: string;
  name: string;
  sellingPrice: number | null;
  onHand: number;
  reserved: number;
  available: number;
};
export type TakingsMethod = {
  method: PaymentMethod;
  received: number;
  voided: number;
  net: number;
  refunded: number;
};
export type TakingsPayment = {
  paymentId: string;
  receiptNumber: string;
  invoiceNumber: string | null;
  orderId: string;
  customerName: string | null;
  method: PaymentMethod;
  amount: number;
  reference: string | null;
  receivedAt: string;
  receivedBy: string;
  isVoid: boolean;
};
export type TakingsDay = {
  date: string;
  methods: TakingsMethod[];
  received: number;
  voided: number;
  refundsPaid: number;
  net: number;
  payments: TakingsPayment[];
  refunds: TakingsRefund[] | null;
  creditsIssued: number;
};
export type TakingsReport = {
  from: string;
  to: string;
  days: TakingsDay[];
  methods: TakingsMethod[];
  received: number;
  voided: number;
  refundsPaid: number;
  net: number;
  creditsIssued: number;
};
export type TakingsRefund = {
  refundId: string;
  refundNumber: string;
  invoiceNumber: string | null;
  orderId: string;
  customerName: string | null;
  method: PaymentMethod;
  amount: number;
  evidenceReference: string | null;
  completedAt: string;
  completedBy: string;
};

export type OrderListParams = {
  page?: number;
  pageSize?: number;
  q?: string;
  status?: OrderStatus;
  paymentStatus?: PaymentStatus;
  fulfillmentStatus?: FulfillmentStatus;
  source?: SalesSource;
  from?: string;
  to?: string;
};

const clean = (params: Record<string, unknown>) =>
  Object.fromEntries(
    Object.entries(params).filter(
      ([, v]) => v !== undefined && v !== '' && v !== null,
    ),
  );

/** Opens a PDF returned by an authenticated request in a new tab. */
const openBlob = (blob: Blob) => {
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
};

const download = (blob: Blob, name: string) => {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
};

export const salesApi = {
  orders: async (p: OrderListParams) =>
    (
      await apiClient.get<Paged<OrderSummary>>('/api/orders', {
        params: clean(p),
      })
    ).data,
  order: async (id: string) =>
    (await apiClient.get<Order>(`/api/orders/${id}`)).data,
  items: async (q: string) =>
    (await apiClient.get<SaleItem[]>('/api/orders/items', { params: { q } }))
      .data,
  preview: async (input: OrderInput) =>
    (await apiClient.post<OrderPreview>('/api/orders/preview', input)).data,
  create: async (input: OrderInput) =>
    (await apiClient.post<Order>('/api/orders', input)).data,
  update: async (id: string, input: OrderInput) =>
    (await apiClient.put<Order>(`/api/orders/${id}`, input)).data,
  confirm: async (id: string) =>
    (await apiClient.post<Order>(`/api/orders/${id}/confirm`)).data,
  cancel: async (id: string, reason: string) =>
    (await apiClient.post<Order>(`/api/orders/${id}/cancel`, { reason })).data,
  duplicate: async (id: string) =>
    (await apiClient.post<Order>(`/api/orders/${id}/duplicate`)).data,
  issue: async (
    id: string,
    lines: { orderLineId: string; quantity: number }[] | null,
    idempotencyKey: string,
  ) =>
    (
      await apiClient.post<Order>(`/api/orders/${id}/issues`, {
        lines,
        idempotencyKey,
      })
    ).data,
  pay: async (
    id: string,
    input: {
      method: PaymentMethod;
      amount: number;
      reference: string | null;
      idempotencyKey: string;
    },
  ) => (await apiClient.post<Order>(`/api/orders/${id}/payments`, input)).data,
  voidPayment: async (
    paymentId: string,
    reason: string,
    idempotencyKey: string,
  ) =>
    (
      await apiClient.post<Order>(`/api/payments/${paymentId}/void`, {
        reason,
        idempotencyKey,
      })
    ).data,
  quickSale: async (input: {
    order: OrderInput;
    method: PaymentMethod;
    amount: number | null;
    reference: string | null;
    idempotencyKey: string;
  }) => (await apiClient.post<Order>('/api/orders/quick-sale', input)).data,
  openPdf: async (id: string) =>
    openBlob(
      (
        await apiClient.get<Blob>(`/api/orders/${id}/pdf`, {
          responseType: 'blob',
        })
      ).data,
    ),

  customers: async (p: {
    q?: string;
    page?: number;
    includeInactive?: boolean;
  }) =>
    (
      await apiClient.get<Paged<Customer>>('/api/customers', {
        params: clean({ pageSize: 25, ...p }),
      })
    ).data,
  createCustomer: async (input: CustomerInput) =>
    (await apiClient.post<Customer>('/api/customers', input)).data,
  updateCustomer: async (id: string, input: CustomerInput) =>
    (await apiClient.put<Customer>(`/api/customers/${id}`, input)).data,

  takings: async (from: string, to: string) =>
    (
      await apiClient.get<TakingsReport>('/api/reports/daily-takings', {
        params: { from, to },
      })
    ).data,
  takingsCsv: async (from: string, to: string) =>
    download(
      (
        await apiClient.get<Blob>('/api/reports/daily-takings', {
          params: { from, to, format: 'csv' },
          responseType: 'blob',
        })
      ).data,
      `takings-${from}-to-${to}.csv`,
    ),
};

export const blankOrder = (source: SalesSource = 'Telephone'): OrderInput => ({
  source,
  customerId: null,
  customerName: null,
  customerPhone: null,
  customerEmail: null,
  customerCompany: null,
  billingAddress: null,
  paymentTerms: 'Immediate',
  deliveryMethod: 'Pickup',
  deliveryAddress: null,
  deliveryCharge: 0,
  orderDiscount: 0,
  discountReason: null,
  notes: null,
  lines: [],
});

export type DeliveryQuote = {
  delivers: boolean;
  zoneName: string | null;
  charge: number | null;
  free: boolean;
  ruralSurcharge: number;
  servicePromise: string | null;
  message: string | null;
};
