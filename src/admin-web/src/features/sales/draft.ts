import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { parseNumber } from '../inventory/api';
import { salesApi, type Order, type OrderInput, type SaleItem } from './api';

export type EditLine = {
  key: string;
  itemId: string;
  sku: string;
  name: string;
  quantity: string;
  unitPrice: string;
  listPrice: number;
  discount: string;
  discountReason: string;
};

export const toLineInputs = (lines: EditLine[]) =>
  lines.map((l) => ({
    itemId: l.itemId,
    quantity: parseNumber(l.quantity) ?? 0,
    unitPrice: parseNumber(l.unitPrice),
    discountAmount: parseNumber(l.discount),
    discountReason: l.discountReason || null,
  }));

export const newLine = (item: SaleItem): EditLine => ({
  key: crypto.randomUUID(),
  itemId: item.itemId,
  sku: item.sku,
  name: item.name,
  quantity: '1',
  unitPrice: String(item.sellingPrice ?? ''),
  listPrice: item.sellingPrice ?? 0,
  discount: '',
  discountReason: '',
});

/**
 * Server-calculated totals for the current draft, refreshed after a short pause in typing.
 * The page never does its own money maths (ADR 0005, D9).
 */
export function usePreview(input: OrderInput, enabled: boolean) {
  const key = JSON.stringify(input);
  const [debounced, setDebounced] = useState(key);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(key), 300);
    return () => clearTimeout(t);
  }, [key]);
  return useQuery({
    queryKey: ['order-preview', debounced],
    queryFn: () => salesApi.preview(JSON.parse(debounced) as OrderInput),
    enabled: enabled && input.lines.length > 0,
    retry: false,
    placeholderData: (previous) => previous,
  });
}

export type Header = Omit<
  OrderInput,
  'lines' | 'version' | 'deliveryCharge' | 'orderDiscount'
> & {
  deliveryCharge: string;
  orderDiscount: string;
};

export const toHeader = (o: OrderInput | Order): Header => ({
  source: o.source,
  customerId: o.customerId,
  customerName: o.customerName,
  customerPhone: o.customerPhone,
  customerEmail: o.customerEmail,
  customerCompany: o.customerCompany,
  billingAddress: o.billingAddress,
  paymentTerms: o.paymentTerms,
  deliveryMethod: o.deliveryMethod,
  deliveryAddress: o.deliveryAddress,
  deliveryCharge: o.deliveryCharge ? String(o.deliveryCharge) : '',
  orderDiscount: o.orderDiscount ? String(o.orderDiscount) : '',
  discountReason: o.discountReason,
  notes: o.notes,
  deliveryPostcode: o.deliveryPostcode ?? null,
  deliveryIsRural: o.deliveryIsRural ?? false,
  deliveryChargeReason: o.deliveryChargeReason ?? null,
});

export const toEditLines = (o: Order): EditLine[] =>
  o.lines.map((l) => ({
    key: l.id,
    itemId: l.itemId,
    sku: l.sku,
    name: l.description,
    quantity: String(l.quantity),
    unitPrice: String(l.unitPrice),
    listPrice: l.listPrice,
    discount: l.lineDiscount ? String(l.lineDiscount) : '',
    discountReason: l.discountReason ?? '',
  }));

export const buildInput = (
  h: Header,
  lines: EditLine[],
  version?: number,
): OrderInput => ({
  ...h,
  customerName: h.customerName || null,
  customerPhone: h.customerPhone || null,
  customerEmail: h.customerEmail || null,
  customerCompany: h.customerCompany || null,
  billingAddress: h.billingAddress || null,
  deliveryAddress:
    h.deliveryMethod === 'Delivery' ? h.deliveryAddress || null : null,
  deliveryCharge:
    h.deliveryMethod === 'Delivery' ? (parseNumber(h.deliveryCharge) ?? 0) : 0,
  orderDiscount: parseNumber(h.orderDiscount) ?? 0,
  discountReason: h.discountReason || null,
  notes: h.notes || null,
  deliveryPostcode:
    h.deliveryMethod === 'Delivery' ? h.deliveryPostcode?.trim() || null : null,
  deliveryIsRural: h.deliveryMethod === 'Delivery' && !!h.deliveryIsRural,
  deliveryChargeReason:
    h.deliveryMethod === 'Delivery' ? h.deliveryChargeReason || null : null,
  lines: toLineInputs(lines),
  version,
});
