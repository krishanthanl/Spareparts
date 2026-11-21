import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import Autocomplete from '@mui/material/Autocomplete';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { fieldError, type ApiProblem } from '../../api/problems';
import { formatMoney } from '../../lib/shop';
import { formatQty, parseNumber } from '../inventory/api';
import {
  fulfillmentLabel,
  paymentStatusLabel,
  salesApi,
  type Customer,
  type FulfillmentStatus,
  type OrderPreview,
  type OrderStatus,
  type PaymentStatus,
  type SaleItem,
} from './api';
import { newLine, type EditLine } from './draft';

export function OrderStatusChip({ status }: { status: OrderStatus }) {
  const color =
    status === 'Completed'
      ? 'success'
      : status === 'Confirmed'
        ? 'primary'
        : status === 'Draft'
          ? 'info'
          : 'default';
  return <Chip size="small" color={color} label={status} />;
}

export function PaymentChip({ status }: { status: PaymentStatus }) {
  const color =
    status === 'Paid'
      ? 'success'
      : status === 'Overpaid'
        ? 'warning'
        : status === 'PartiallyPaid'
          ? 'info'
          : 'default';
  return (
    <Chip
      size="small"
      variant="outlined"
      color={color}
      label={paymentStatusLabel[status]}
    />
  );
}

export function FulfillmentChip({ status }: { status: FulfillmentStatus }) {
  const color =
    status === 'Issued'
      ? 'success'
      : status === 'PartiallyIssued'
        ? 'info'
        : 'default';
  return (
    <Chip
      size="small"
      variant="outlined"
      color={color}
      label={fulfillmentLabel[status]}
    />
  );
}

/** Type a SKU or name; shows what can still be sold (on hand minus reserved). */
export function SaleItemPicker({
  onPick,
  autoFocus,
}: {
  onPick: (item: SaleItem) => void;
  autoFocus?: boolean;
}) {
  const [input, setInput] = useState('');
  const search = useQuery({
    queryKey: ['sale-items', input],
    queryFn: () => salesApi.items(input),
    enabled: input.trim().length >= 1,
  });
  return (
    <Autocomplete
      size="small"
      sx={{ minWidth: 280, flex: 1 }}
      options={search.data ?? []}
      value={null}
      inputValue={input}
      filterOptions={(x) => x}
      loading={search.isFetching}
      getOptionLabel={(o) => `${o.sku} — ${o.name}`}
      getOptionDisabled={(o) => o.sellingPrice === null}
      isOptionEqualToValue={(a, b) => a.itemId === b.itemId}
      noOptionsText={input ? 'No matching items' : 'Type a SKU or name'}
      onInputChange={(_, text, reason) => reason !== 'reset' && setInput(text)}
      onChange={(_, item) => {
        if (item) {
          onPick(item);
          setInput('');
        }
      }}
      renderOption={(props, o) => (
        <li {...props} key={o.itemId}>
          <span className="mono">{o.sku}</span>&nbsp;{o.name}
          <span
            className="muted"
            style={{ marginLeft: 'auto', paddingLeft: 12 }}
          >
            {formatQty(o.available)} available
            {o.reserved > 0 && ` (${formatQty(o.reserved)} reserved)`} ·{' '}
            {o.sellingPrice === null ? 'no price' : formatMoney(o.sellingPrice)}
          </span>
        </li>
      )}
      renderInput={(params) => (
        <TextField
          {...params}
          label="Add item (SKU or name)"
          autoFocus={autoFocus}
        />
      )}
    />
  );
}

export function CustomerPicker({
  value,
  onChange,
  disabled,
}: {
  value: Customer | null;
  onChange: (customer: Customer | null) => void;
  disabled?: boolean;
}) {
  const [input, setInput] = useState('');
  const search = useQuery({
    queryKey: ['customer-search', input],
    queryFn: () => salesApi.customers({ q: input }),
    enabled: !disabled,
  });
  return (
    <Autocomplete
      size="small"
      sx={{ minWidth: 260, flex: 1 }}
      options={search.data?.items ?? []}
      value={value}
      disabled={disabled}
      filterOptions={(x) => x}
      getOptionLabel={(c) => c.name + (c.phone ? ` · ${c.phone}` : '')}
      isOptionEqualToValue={(a, b) => a.id === b.id}
      onInputChange={(_, text) => setInput(text)}
      onChange={(_, c) => onChange(c)}
      renderInput={(params) => (
        <TextField
          {...params}
          label="Customer"
          placeholder="Search, or leave empty for a cash sale"
        />
      )}
    />
  );
}

export function LineEditor({
  lines,
  setLines,
  preview,
  problem,
  canDiscount,
  autoFocus,
}: {
  lines: EditLine[];
  setLines: (lines: EditLine[]) => void;
  preview: OrderPreview | undefined;
  problem: ApiProblem | null;
  canDiscount: boolean;
  autoFocus?: boolean;
}) {
  const update = (key: string, patch: Partial<EditLine>) =>
    setLines(lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));
  const numberField = (
    l: EditLine,
    i: number,
    field: 'quantity' | 'unitPrice' | 'discount',
    label: string,
    width: number,
    disabled = false,
  ) => {
    const apiField = field === 'discount' ? 'discountAmount' : field;
    return (
      <TextField
        size="small"
        value={l[field]}
        disabled={disabled}
        onChange={(e) => update(l.key, { [field]: e.target.value })}
        error={!!fieldError(problem, `lines[${i}].${apiField}`)}
        helperText={fieldError(problem, `lines[${i}].${apiField}`)}
        slotProps={{
          htmlInput: {
            inputMode: 'decimal',
            'aria-label': `${label} of ${l.sku}`,
            style: { textAlign: 'right', width },
          },
        }}
      />
    );
  };
  return (
    <>
      <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
        <SaleItemPicker
          autoFocus={autoFocus}
          onPick={(item) => {
            const existing = lines.find((l) => l.itemId === item.itemId);
            if (existing)
              update(existing.key, {
                quantity: String((parseNumber(existing.quantity) ?? 0) + 1),
              });
            else setLines([...lines, newLine(item)]);
          }}
        />
      </Stack>
      <TableContainer>
        <Table size="small" aria-label="Order lines">
          <TableHead>
            <TableRow>
              <TableCell>Item</TableCell>
              <TableCell align="right">Qty</TableCell>
              <TableCell align="right">Unit price</TableCell>
              {canDiscount && <TableCell align="right">Discount $</TableCell>}
              <TableCell align="right">Amount</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {lines.map((l, i) => {
              const priced = preview?.lines.find((p) => p.itemId === l.itemId);
              const qty = parseNumber(l.quantity) ?? 0;
              const short = priced && priced.available < qty;
              const reduced =
                (parseNumber(l.unitPrice) ?? l.listPrice) < l.listPrice ||
                (parseNumber(l.discount) ?? 0) > 0;
              return (
                <TableRow key={l.key}>
                  <TableCell>
                    <span className="mono">{l.sku}</span> {l.name}
                    <Typography
                      variant="body2"
                      className="muted"
                      color={short ? 'error' : undefined}
                    >
                      {priced && `${formatQty(priced.available)} available`}
                      {short && ' — not enough to confirm'}
                      {l.listPrice > 0 && ` · list ${formatMoney(l.listPrice)}`}
                    </Typography>
                    {reduced && (
                      <TextField
                        size="small"
                        label="Reason for lower price"
                        value={l.discountReason}
                        onChange={(e) =>
                          update(l.key, { discountReason: e.target.value })
                        }
                        error={
                          !!fieldError(problem, `lines[${i}].discountReason`)
                        }
                        helperText={fieldError(
                          problem,
                          `lines[${i}].discountReason`,
                        )}
                        sx={{ mt: 1, minWidth: 240 }}
                      />
                    )}
                  </TableCell>
                  <TableCell align="right">
                    {numberField(l, i, 'quantity', 'Quantity', 70)}
                  </TableCell>
                  <TableCell align="right">
                    {numberField(l, i, 'unitPrice', 'Unit price', 90)}
                  </TableCell>
                  {canDiscount && (
                    <TableCell align="right">
                      {numberField(l, i, 'discount', 'Discount', 80)}
                    </TableCell>
                  )}
                  <TableCell align="right">
                    {priced ? formatMoney(priced.lineTotal) : '—'}
                  </TableCell>
                  <TableCell>
                    <Button
                      size="small"
                      color="error"
                      onClick={() =>
                        setLines(lines.filter((x) => x.key !== l.key))
                      }
                    >
                      Remove
                    </Button>
                  </TableCell>
                </TableRow>
              );
            })}
            {lines.length === 0 && (
              <TableRow>
                <TableCell colSpan={6}>No items yet. Add one above.</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
    </>
  );
}

export function Totals({
  subtotal,
  discounts,
  delivery,
  total,
  paid,
  showDelivery,
}: {
  subtotal: number;
  discounts: number;
  delivery: number;
  total: number;
  paid?: number;
  showDelivery: boolean;
}) {
  const row = (label: string, value: string, strong = false) => (
    <Stack direction="row" spacing={2} sx={{ justifyContent: 'flex-end' }}>
      <Typography sx={{ minWidth: 140, textAlign: 'right' }}>
        {strong ? <strong>{label}</strong> : label}
      </Typography>
      <Typography sx={{ minWidth: 100, textAlign: 'right' }}>
        {strong ? <strong>{value}</strong> : value}
      </Typography>
    </Stack>
  );
  return (
    <Stack spacing={0.5} sx={{ mt: 2 }} aria-label="Totals">
      {row('Subtotal', formatMoney(subtotal))}
      {discounts > 0 && row('Discounts', '−' + formatMoney(discounts))}
      {showDelivery && row('Delivery', formatMoney(delivery))}
      {row('Total NZD', formatMoney(total), true)}
      {paid !== undefined && row('Paid', formatMoney(paid))}
      {paid !== undefined &&
        row(
          paid > total ? 'Owed to customer' : 'Balance due',
          formatMoney(Math.abs(total - paid)),
          true,
        )}
      <Typography variant="body2" className="muted" sx={{ textAlign: 'right' }}>
        Not GST registered — no GST charged
      </Typography>
    </Stack>
  );
}
