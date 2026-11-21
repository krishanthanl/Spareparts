import { apiClient } from '../../api/client';
import type { Paged } from '../inventory/api';
import type { DeliveryQuote, OrderStatus, PaymentMethod } from './api';

export type ShipmentStatus =
  'Pending' | 'Dispatched' | 'Delivered' | 'Cancelled';
export type ReturnDisposition = 'Restock' | 'WriteOff';
export type RefundStatus =
  | 'Requested'
  | 'Approved'
  | 'Processing'
  | 'Succeeded'
  | 'Failed'
  | 'Cancelled';

export const carriers = ['NZ Post', 'CourierPost', 'Mainfreight', 'Own van'];

export type DeliverySettings = {
  deliveryEnabled: boolean;
  pickupEnabled: boolean;
  freeDeliveryThreshold: number | null;
  isGstRegistered: boolean;
  taxTreatment: string;
  version: number;
  updatedAt: string | null;
};
export type DeliveryZone = {
  id: string;
  name: string;
  charge: number;
  freeDeliveryThreshold: number | null;
  ruralSurcharge: number;
  servicePromise: string | null;
  isActive: boolean;
  postcodes: { from: string; to: string }[];
  version: number;
  updatedAt: string;
};
export type DeliveryZoneInput = Omit<
  DeliveryZone,
  'id' | 'updatedAt' | 'version' | 'postcodes'
> & {
  postcodes: { from: string; to?: string | null }[];
  version?: number;
};

export type Shipment = {
  id: string;
  shipmentNumber: string;
  orderId: string;
  invoiceNumber: string | null;
  customerName: string | null;
  deliveryAddress: string | null;
  status: ShipmentStatus;
  carrier: string | null;
  trackingReference: string | null;
  notes: string | null;
  stockIssueId: string | null;
  issueNumber: string | null;
  dispatchedAt: string | null;
  dispatchedBy: string | null;
  deliveredAt: string | null;
  deliveredBy: string | null;
  cancelledAt: string | null;
  lines: {
    orderLineId: string;
    sku: string;
    description: string;
    quantity: number;
  }[];
  version: number;
  createdAt: string;
};

export type SalesReturn = {
  id: string;
  returnNumber: string;
  orderId: string;
  invoiceNumber: string | null;
  customerName: string | null;
  reason: string;
  postedAt: string;
  postedBy: string;
  creditNoteId: string | null;
  creditNoteNumber: string | null;
  creditTotal: number;
  lines: {
    orderLineId: string;
    sku: string;
    description: string;
    quantity: number;
    disposition: ReturnDisposition;
    unitCost: number;
    credit: number;
  }[];
};
export type ReturnableLine = {
  orderLineId: string;
  lineNo: number;
  sku: string;
  description: string;
  ordered: number;
  issued: number;
  returned: number;
  returnable: number;
  lineTotal: number;
  credited: number;
};

export type CreditNote = {
  id: string;
  creditNoteNumber: string;
  orderId: string;
  invoiceNumber: string | null;
  customerName: string | null;
  salesReturnId: string | null;
  returnNumber: string | null;
  reason: string;
  total: number;
  gstAmount: number;
  issuedAt: string;
  issuedBy: string;
  lines: {
    orderLineId: string | null;
    description: string;
    quantity: number;
    amount: number;
  }[];
};

export type Refund = {
  id: string;
  refundNumber: string;
  orderId: string;
  invoiceNumber: string | null;
  customerName: string | null;
  creditNoteId: string | null;
  creditNoteNumber: string | null;
  amount: number;
  method: PaymentMethod;
  reason: string;
  status: RefundStatus;
  requestedBy: string;
  requestedById: string;
  requestedAt: string;
  approvedBy: string | null;
  approvedAt: string | null;
  completedBy: string | null;
  completedAt: string | null;
  evidenceReference: string | null;
  failureReason: string | null;
  allocations: { paymentId: string; receiptNumber: string; amount: number }[];
  version: number;
};

export type InvoiceBalance = {
  orderId: string;
  invoiceNumber: string | null;
  status: OrderStatus;
  total: number;
  credits: number;
  netTotal: number;
  received: number;
  refunded: number;
  netPaid: number;
  balanceDue: number;
  owedToCustomer: number;
  pendingRefunds: number;
  refundable: number;
  refundableOnCancel: number;
  canCancel: boolean;
};

export type OrderAfterSale = {
  balance: InvoiceBalance;
  shipments: Shipment[];
  returnable: ReturnableLine[];
  returns: SalesReturn[];
  creditNotes: CreditNote[];
  refunds: Refund[];
};

export const refundStatusLabel: Record<RefundStatus, string> = {
  Requested: 'Waiting for approval',
  Approved: 'Approved, not paid yet',
  Processing: 'Processing',
  Succeeded: 'Paid out',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
};

const clean = (params: Record<string, unknown>) =>
  Object.fromEntries(
    Object.entries(params).filter(
      ([, v]) => v !== undefined && v !== '' && v !== null,
    ),
  );

const openBlob = (blob: Blob) => {
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
};

type ListParams = {
  page?: number;
  q?: string;
  status?: string;
  orderId?: string;
};

export const afterSaleApi = {
  forOrder: async (id: string) =>
    (await apiClient.get<OrderAfterSale>(`/api/orders/${id}/after-sale`)).data,

  settings: async () =>
    (await apiClient.get<DeliverySettings>('/api/delivery-settings')).data,
  saveSettings: async (input: {
    deliveryEnabled: boolean;
    pickupEnabled: boolean;
    freeDeliveryThreshold: number | null;
    version?: number;
  }) =>
    (await apiClient.put<DeliverySettings>('/api/delivery-settings', input))
      .data,
  zones: async () =>
    (await apiClient.get<DeliveryZone[]>('/api/delivery-zones')).data,
  createZone: async (input: DeliveryZoneInput) =>
    (await apiClient.post<DeliveryZone>('/api/delivery-zones', input)).data,
  updateZone: async (id: string, input: DeliveryZoneInput) =>
    (await apiClient.put<DeliveryZone>(`/api/delivery-zones/${id}`, input))
      .data,
  quote: async (postcode: string, isRural: boolean, goodsValue: number) =>
    (
      await apiClient.post<DeliveryQuote>('/api/delivery-quote', {
        postcode,
        isRural,
        goodsValue,
      })
    ).data,

  shipments: async (p: ListParams) =>
    (
      await apiClient.get<Paged<Shipment>>('/api/shipments', {
        params: clean({ pageSize: 25, ...p }),
      })
    ).data,
  createShipment: async (
    orderId: string,
    input: {
      carrier: string | null;
      trackingReference: string | null;
      notes: string | null;
      lines: { orderLineId: string; quantity: number }[] | null;
    },
  ) =>
    (await apiClient.post<Shipment>(`/api/orders/${orderId}/shipments`, input))
      .data,
  dispatch: async (
    id: string,
    input: {
      carrier: string | null;
      trackingReference: string | null;
      idempotencyKey: string;
    },
  ) =>
    (await apiClient.post<Shipment>(`/api/shipments/${id}/dispatch`, input))
      .data,
  deliver: async (id: string) =>
    (await apiClient.post<Shipment>(`/api/shipments/${id}/deliver`)).data,
  cancelShipment: async (id: string) =>
    (await apiClient.post<Shipment>(`/api/shipments/${id}/cancel`)).data,

  returns: async (p: ListParams) =>
    (
      await apiClient.get<Paged<SalesReturn>>('/api/sales-returns', {
        params: clean({ pageSize: 25, ...p }),
      })
    ).data,
  createReturn: async (
    orderId: string,
    input: {
      reason: string;
      lines: {
        orderLineId: string;
        quantity: number;
        disposition: ReturnDisposition;
      }[];
      idempotencyKey: string;
    },
  ) =>
    (await apiClient.post<SalesReturn>(`/api/orders/${orderId}/returns`, input))
      .data,

  creditNotes: async (p: ListParams) =>
    (
      await apiClient.get<Paged<CreditNote>>('/api/credit-notes', {
        params: clean({ pageSize: 25, ...p }),
      })
    ).data,
  createCreditNote: async (
    orderId: string,
    input: {
      reason: string;
      lines: { orderLineId: string; amount: number }[];
      deliveryAmount: number;
      idempotencyKey: string;
    },
  ) =>
    (
      await apiClient.post<CreditNote>(
        `/api/orders/${orderId}/credit-notes`,
        input,
      )
    ).data,
  openCreditNotePdf: async (id: string) =>
    openBlob(
      (
        await apiClient.get<Blob>(`/api/credit-notes/${id}/pdf`, {
          responseType: 'blob',
        })
      ).data,
    ),

  refunds: async (p: ListParams) =>
    (
      await apiClient.get<Paged<Refund>>('/api/refunds', {
        params: clean({ pageSize: 25, ...p }),
      })
    ).data,
  requestRefund: async (
    orderId: string,
    input: {
      amount: number;
      method: PaymentMethod;
      reason: string;
      creditNoteId: string | null;
      forCancellation: boolean;
      idempotencyKey: string;
    },
  ) =>
    (await apiClient.post<Refund>(`/api/orders/${orderId}/refunds`, input))
      .data,
  approveRefund: async (id: string) =>
    (await apiClient.post<Refund>(`/api/refunds/${id}/approve`)).data,
  completeRefund: async (
    id: string,
    input: {
      method: PaymentMethod;
      evidenceReference: string;
      idempotencyKey: string;
    },
  ) =>
    (await apiClient.post<Refund>(`/api/refunds/${id}/complete`, input)).data,
  failRefund: async (id: string, reason: string) =>
    (await apiClient.post<Refund>(`/api/refunds/${id}/fail`, { reason })).data,
  cancelRefund: async (id: string, reason: string) =>
    (await apiClient.post<Refund>(`/api/refunds/${id}/cancel`, { reason }))
      .data,
};
