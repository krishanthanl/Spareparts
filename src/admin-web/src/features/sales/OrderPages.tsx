import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import Checkbox from '@mui/material/Checkbox';
import FormControlLabel from '@mui/material/FormControlLabel';
import { AfterSalePanel } from './AfterSalePanel';
import { describeError, fieldError, type ApiProblem } from '../../api/problems';
import { formatMoney, formatShopDate } from '../../lib/shop';
import { ProblemAlert } from '../catalog/ProblemAlert';
import { useAuth } from '../auth/useAuth';
import { DocumentPagination } from '../inventory/components';
import { formatQty, parseNumber } from '../inventory/api';
import {
  blankOrder,
  methodLabel,
  paymentMethods,
  salesApi,
  sourceLabel,
  sources,
  type Customer,
  type DeliveryMethod,
  type DeliveryQuote,
  type FulfillmentStatus,
  type Order,
  type OrderStatus,
  type PaymentMethod,
  type PaymentStatus,
  type PaymentTerms,
  type SalesSource,
} from './api';
import {
  CustomerPicker,
  FulfillmentChip,
  LineEditor,
  OrderStatusChip,
  PaymentChip,
  Totals,
} from './components';
import {
  buildInput,
  toEditLines,
  toHeader,
  usePreview,
  type EditLine,
  type Header,
} from './draft';

const select = { select: { native: true } } as const;

export function OrderListPage() {
  const [page, setPage] = useState(1);
  const [q, setQ] = useState('');
  const [status, setStatus] = useState<OrderStatus | ''>('');
  const [payment, setPayment] = useState<PaymentStatus | ''>('');
  const [fulfillment, setFulfillment] = useState<FulfillmentStatus | ''>('');
  const [source, setSource] = useState<SalesSource | ''>('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const list = useQuery({
    queryKey: [
      'orders',
      q,
      status,
      payment,
      fulfillment,
      source,
      from,
      to,
      page,
    ],
    queryFn: () =>
      salesApi.orders({
        q,
        status: status || undefined,
        paymentStatus: payment || undefined,
        fulfillmentStatus: fulfillment || undefined,
        source: source || undefined,
        from: from || undefined,
        to: to || undefined,
        page,
        pageSize: 25,
      }),
  });
  const reset =
    <T,>(set: (v: T) => void) =>
    (v: T) => {
      set(v);
      setPage(1);
    };
  return (
    <>
      <Stack
        direction="row"
        sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}
      >
        <Typography variant="h4" component="h1">
          Orders and invoices
        </Typography>
        <Stack direction="row" spacing={1}>
          <Button variant="outlined" component={Link} to="/sales/quick-sale">
            Counter sale
          </Button>
          <Button variant="contained" component={Link} to="/sales/invoices/new">
            New order
          </Button>
        </Stack>
      </Stack>
      <p>
        A draft becomes an invoice with its own number when you confirm it.
        Confirming reserves the stock; issuing hands it over.
      </p>
      <Stack
        direction="row"
        spacing={2}
        useFlexGap
        sx={{ mb: 2, flexWrap: 'wrap' }}
      >
        <TextField
          size="small"
          label="Search number, customer or phone"
          value={q}
          onChange={(e) => reset(setQ)(e.target.value)}
          sx={{ minWidth: 260 }}
        />
        <TextField
          size="small"
          select
          label="Status"
          value={status}
          slotProps={select}
          sx={{ minWidth: 130 }}
          onChange={(e) => reset(setStatus)(e.target.value as OrderStatus | '')}
        >
          <option value="">All</option>
          {['Draft', 'Confirmed', 'Completed', 'Cancelled'].map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </TextField>
        <TextField
          size="small"
          select
          label="Payment"
          value={payment}
          slotProps={select}
          sx={{ minWidth: 130 }}
          onChange={(e) =>
            reset(setPayment)(e.target.value as PaymentStatus | '')
          }
        >
          <option value="">All</option>
          <option value="Unpaid">Unpaid</option>
          <option value="PartiallyPaid">Part paid</option>
          <option value="Paid">Paid</option>
          <option value="Overpaid">Overpaid</option>
        </TextField>
        <TextField
          size="small"
          select
          label="Stock"
          value={fulfillment}
          slotProps={select}
          sx={{ minWidth: 130 }}
          onChange={(e) =>
            reset(setFulfillment)(e.target.value as FulfillmentStatus | '')
          }
        >
          <option value="">All</option>
          <option value="NotIssued">Not issued</option>
          <option value="PartiallyIssued">Part issued</option>
          <option value="Issued">Issued</option>
        </TextField>
        <TextField
          size="small"
          select
          label="Source"
          value={source}
          slotProps={select}
          sx={{ minWidth: 130 }}
          onChange={(e) => reset(setSource)(e.target.value as SalesSource | '')}
        >
          <option value="">All</option>
          {[...sources, { value: 'Online', label: 'Online' }].map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </TextField>
        <TextField
          size="small"
          type="date"
          label="From"
          value={from}
          onChange={(e) => reset(setFrom)(e.target.value)}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          size="small"
          type="date"
          label="To"
          value={to}
          onChange={(e) => reset(setTo)(e.target.value)}
          slotProps={{ inputLabel: { shrink: true } }}
        />
      </Stack>
      <ProblemAlert problem={list.isError ? describeError(list.error) : null} />
      <TableContainer component={Paper} elevation={0}>
        <Table size="small" aria-label="Orders">
          <TableHead>
            <TableRow>
              <TableCell>Number</TableCell>
              <TableCell>Customer</TableCell>
              <TableCell>Source</TableCell>
              <TableCell>Status</TableCell>
              <TableCell align="right">Total</TableCell>
              <TableCell align="right">Balance</TableCell>
              <TableCell>Date</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((o) => (
              <TableRow key={o.id} hover>
                <TableCell>
                  <Link to={`/sales/invoices/${o.id}`}>
                    {o.invoiceNumber ?? 'Draft'}
                  </Link>
                </TableCell>
                <TableCell>
                  {o.customerName ?? <span className="muted">Cash sale</span>}
                  {o.customerPhone && (
                    <span className="muted"> · {o.customerPhone}</span>
                  )}
                </TableCell>
                <TableCell>{sourceLabel(o.source)}</TableCell>
                <TableCell>
                  <Stack
                    direction="row"
                    spacing={0.5}
                    useFlexGap
                    sx={{ flexWrap: 'wrap' }}
                  >
                    <OrderStatusChip status={o.status} />
                    {(o.status === 'Confirmed' || o.status === 'Completed') && (
                      <>
                        <PaymentChip status={o.paymentStatus} />
                        <FulfillmentChip status={o.fulfillmentStatus} />
                      </>
                    )}
                  </Stack>
                </TableCell>
                <TableCell align="right">{formatMoney(o.total)}</TableCell>
                <TableCell align="right">
                  {o.balanceDue === 0 ? '—' : formatMoney(o.balanceDue)}
                </TableCell>
                <TableCell>
                  {formatShopDate(o.confirmedAt ?? o.updatedAt)}
                </TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={7}>No orders match.</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
      <DocumentPagination
        page={page}
        total={list.data?.totalCount ?? 0}
        onPageChange={setPage}
      />
    </>
  );
}

/** Customer, delivery and payment-terms fields shared by the order editor and the counter sale. */
export function OrderHeaderFields({
  header,
  setHeader,
  problem,
  showSource = true,
  quote,
}: {
  header: Header;
  setHeader: (h: Header) => void;
  problem: ApiProblem | null;
  showSource?: boolean;
  quote?: DeliveryQuote | null;
}) {
  const [customer, setCustomer] = useState<Customer | null>(null);
  const set = (patch: Partial<Header>) => setHeader({ ...header, ...patch });
  const text = (
    field: keyof Header,
    label: string,
    flex = 1,
    multiline = false,
  ) => (
    <TextField
      label={label}
      value={(header[field] as string | null) ?? ''}
      onChange={(e) => set({ [field]: e.target.value } as Partial<Header>)}
      error={!!fieldError(problem, field)}
      helperText={fieldError(problem, field)}
      multiline={multiline}
      minRows={multiline ? 2 : undefined}
      sx={{ flex }}
    />
  );
  return (
    <Stack spacing={2}>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
        {showSource && (
          <TextField
            select
            label="Order type"
            value={header.source}
            slotProps={select}
            sx={{ minWidth: 150 }}
            onChange={(e) => set({ source: e.target.value as SalesSource })}
          >
            {sources.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </TextField>
        )}
        <CustomerPicker
          value={customer}
          onChange={(c) => {
            setCustomer(c);
            set(
              c
                ? {
                    customerId: c.id,
                    customerName: c.name,
                    customerPhone: c.phone,
                    customerEmail: c.email,
                    customerCompany: c.company,
                    billingAddress: c.billingAddress,
                    deliveryAddress:
                      header.deliveryAddress || c.deliveryAddress,
                  }
                : { customerId: null },
            );
          }}
        />
      </Stack>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
        {text('customerName', 'Name', 2)}
        {text('customerPhone', 'Phone')}
        {text('customerEmail', 'Email', 2)}
        {text('customerCompany', 'Company', 2)}
      </Stack>
      {text('billingAddress', 'Billing address', 1, true)}
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
        <TextField
          select
          label="Delivery"
          value={header.deliveryMethod}
          slotProps={select}
          sx={{ minWidth: 150 }}
          onChange={(e) =>
            set({ deliveryMethod: e.target.value as DeliveryMethod })
          }
        >
          <option value="Pickup">Pickup</option>
          <option value="Delivery">Delivery</option>
        </TextField>
        <TextField
          select
          label="Payment"
          value={header.paymentTerms}
          slotProps={select}
          sx={{ minWidth: 190 }}
          onChange={(e) =>
            set({ paymentTerms: e.target.value as PaymentTerms })
          }
          error={!!fieldError(problem, 'paymentTerms')}
          helperText={fieldError(problem, 'paymentTerms')}
        >
          <option value="Immediate">Pay now / on receipt</option>
          <option value="OnDelivery">Pay on delivery</option>
        </TextField>
        {header.deliveryMethod === 'Delivery' &&
          text('deliveryAddress', 'Delivery address', 3, true)}
      </Stack>
      {header.deliveryMethod === 'Delivery' && (
        <Stack
          direction={{ xs: 'column', md: 'row' }}
          spacing={2}
          useFlexGap
          sx={{ alignItems: { md: 'flex-start' }, flexWrap: 'wrap' }}
        >
          <TextField
            label="Postcode"
            value={header.deliveryPostcode ?? ''}
            onChange={(e) => set({ deliveryPostcode: e.target.value })}
            error={!!fieldError(problem, 'deliveryPostcode')}
            helperText={fieldError(problem, 'deliveryPostcode')}
            slotProps={{ htmlInput: { inputMode: 'numeric', maxLength: 4 } }}
            sx={{ maxWidth: 120 }}
          />
          <FormControlLabel
            control={
              <Checkbox
                checked={!!header.deliveryIsRural}
                onChange={(e) => set({ deliveryIsRural: e.target.checked })}
              />
            }
            label="Rural delivery (RD)"
          />
          <TextField
            label="Delivery charge $"
            value={header.deliveryCharge}
            onChange={(e) => set({ deliveryCharge: e.target.value })}
            error={!!fieldError(problem, 'deliveryCharge')}
            helperText={
              fieldError(problem, 'deliveryCharge') ??
              (quote?.delivers && quote.charge !== null
                ? `Quote ${formatMoney(quote.charge)}`
                : undefined)
            }
            slotProps={{ htmlInput: { inputMode: 'decimal' } }}
            sx={{ maxWidth: 160 }}
          />
          {quote?.delivers && quote.charge !== null && (
            <Button
              size="small"
              sx={{ mt: 1 }}
              onClick={() =>
                set({
                  deliveryCharge: String(quote.charge),
                  deliveryChargeReason: null,
                })
              }
            >
              Use quote
            </Button>
          )}
          <Stack sx={{ flex: 2, minWidth: 240 }}>
            {text('deliveryChargeReason', 'Reason if not the quote')}
          </Stack>
        </Stack>
      )}
      {header.deliveryMethod === 'Delivery' && quote && (
        <Alert severity={quote.delivers ? 'info' : 'warning'}>
          {quote.delivers
            ? `${quote.zoneName}: ${formatMoney(quote.charge ?? 0)}${quote.free ? ' (free delivery threshold reached)' : ''}${quote.ruralSurcharge ? `, includes ${formatMoney(quote.ruralSurcharge)} rural surcharge` : ''}${quote.servicePromise ? ` · ${quote.servicePromise}` : ''}`
            : `${quote.message} An admin can still deliver with a charge and a reason.`}
        </Alert>
      )}
    </Stack>
  );
}

export function OrderPage() {
  const { id = 'new' } = useParams();
  const isNew = id === 'new';
  const existing = useQuery({
    queryKey: ['order', id],
    queryFn: () => salesApi.order(id),
    enabled: !isNew,
  });
  if (isNew) return <OrderEditor />;
  if (!existing.data)
    return existing.isError ? (
      <ProblemAlert problem={describeError(existing.error)} />
    ) : (
      <p role="status">Loading…</p>
    );
  return existing.data.status === 'Draft' ? (
    <OrderEditor
      key={existing.data.id + existing.data.version}
      order={existing.data}
    />
  ) : (
    <InvoiceView order={existing.data} />
  );
}

function OrderEditor({ order }: { order?: Order }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { can } = useAuth();
  const canDiscount = can('sales.discount');
  const [header, setHeader] = useState<Header>(toHeader(order ?? blankOrder()));
  const [lines, setLines] = useState<EditLine[]>(
    order ? toEditLines(order) : [],
  );
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const input = buildInput(header, lines, order?.version);
  const preview = usePreview(input, true);

  const save = async (then?: (o: Order) => Promise<Order>) => {
    setBusy(true);
    setProblem(null);
    try {
      let saved = order
        ? await salesApi.update(order.id, input)
        : await salesApi.create(input);
      if (then) saved = await then(saved);
      queryClient.setQueryData(['order', saved.id], saved);
      void queryClient.invalidateQueries({ queryKey: ['orders'] });
      if (!order) navigate(`/sales/invoices/${saved.id}`, { replace: true });
    } catch (error) {
      setProblem(describeError(error));
    } finally {
      setBusy(false);
    }
  };

  const discard = async () => {
    if (!order) return navigate('/sales/invoices');
    setBusy(true);
    try {
      await salesApi.cancel(order.id, 'Draft discarded');
      void queryClient.invalidateQueries({ queryKey: ['orders'] });
      navigate('/sales/invoices');
    } catch (error) {
      setProblem(describeError(error));
      setBusy(false);
    }
  };

  const p = preview.data;
  return (
    <>
      <Button component={Link} to="/sales/invoices" size="small">
        ← Orders and invoices
      </Button>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', mb: 1 }}>
        <Typography variant="h4" component="h1">
          {order ? 'Draft order' : 'New order'}
        </Typography>
        {order && <OrderStatusChip status="Draft" />}
      </Stack>
      <ProblemAlert
        problem={
          problem ?? (preview.isError ? describeError(preview.error) : null)
        }
      />
      <Paper elevation={0} className="permission-panel">
        <OrderHeaderFields
          header={header}
          setHeader={setHeader}
          problem={problem}
          quote={preview.data?.deliveryQuote}
        />
      </Paper>
      <Paper elevation={0} className="permission-panel">
        <LineEditor
          lines={lines}
          setLines={setLines}
          preview={p}
          problem={problem}
          canDiscount={canDiscount}
          autoFocus={!order}
        />
        {canDiscount && (
          <Stack
            direction={{ xs: 'column', md: 'row' }}
            spacing={2}
            sx={{ mt: 2, justifyContent: 'flex-end' }}
          >
            <TextField
              size="small"
              label="Order discount $"
              value={header.orderDiscount}
              onChange={(e) =>
                setHeader({ ...header, orderDiscount: e.target.value })
              }
              error={!!fieldError(problem, 'orderDiscount')}
              helperText={fieldError(problem, 'orderDiscount')}
              slotProps={{ htmlInput: { inputMode: 'decimal' } }}
              sx={{ maxWidth: 160 }}
            />
            <TextField
              size="small"
              label="Discount reason"
              value={header.discountReason ?? ''}
              onChange={(e) =>
                setHeader({ ...header, discountReason: e.target.value })
              }
              error={!!fieldError(problem, 'discountReason')}
              helperText={fieldError(problem, 'discountReason')}
              sx={{ minWidth: 260 }}
            />
          </Stack>
        )}
        {p && (
          <Totals
            subtotal={p.subtotal}
            discounts={p.lineDiscountTotal + p.orderDiscount}
            delivery={p.deliveryCharge}
            total={p.total}
            showDelivery={header.deliveryMethod === 'Delivery'}
          />
        )}
        {p?.needsDiscountPermission && !canDiscount && (
          <Alert severity="warning" sx={{ mt: 2 }}>
            A price is below the selling price. Only staff with the discount
            permission can save this.
          </Alert>
        )}
        <TextField
          label="Notes"
          value={header.notes ?? ''}
          onChange={(e) => setHeader({ ...header, notes: e.target.value })}
          fullWidth
          sx={{ mt: 2 }}
        />
      </Paper>
      <Stack direction="row" spacing={2}>
        <Button variant="outlined" disabled={busy} onClick={() => void save()}>
          Save draft
        </Button>
        <Button
          variant="contained"
          disabled={busy || lines.length === 0}
          onClick={() => setConfirming(true)}
        >
          Confirm invoice
        </Button>
        <Button color="error" disabled={busy} onClick={() => void discard()}>
          {order ? 'Discard draft' : 'Cancel'}
        </Button>
      </Stack>
      <Dialog open={confirming} onClose={() => setConfirming(false)}>
        <DialogTitle>Confirm this invoice?</DialogTitle>
        <DialogContent>
          <p>
            It gets an invoice number, its prices are fixed, and the stock is
            reserved for this customer. You can then issue the goods and record
            payments.
          </p>
          {p && <strong>Total {formatMoney(p.total)}</strong>}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirming(false)}>Not yet</Button>
          <Button
            variant="contained"
            onClick={() => {
              setConfirming(false);
              void save((saved) => salesApi.confirm(saved.id));
            }}
          >
            Confirm
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

function InvoiceView({ order }: { order: Order }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { can } = useAuth();
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);
  const [dialog, setDialog] = useState<'issue' | 'pay' | 'cancel' | null>(null);
  const [voiding, setVoiding] = useState<string | null>(null);
  const live = order.status === 'Confirmed' || order.status === 'Completed';

  const run = async (action: () => Promise<Order>) => {
    setBusy(true);
    setProblem(null);
    try {
      const result = await action();
      queryClient.setQueryData(['order', result.id], result);
      void queryClient.invalidateQueries({ queryKey: ['orders'] });
      return result;
    } catch (error) {
      setProblem(describeError(error));
      return null;
    } finally {
      setBusy(false);
    }
  };

  const toIssue = order.lines.reduce((s, l) => s + l.qtyToIssue, 0);
  return (
    <>
      <Button component={Link} to="/sales/invoices" size="small">
        ← Orders and invoices
      </Button>
      <Stack
        direction="row"
        spacing={1}
        useFlexGap
        sx={{ alignItems: 'center', mb: 1, flexWrap: 'wrap' }}
      >
        <Typography variant="h4" component="h1">
          Invoice {order.invoiceNumber}
        </Typography>
        <OrderStatusChip status={order.status} />
        {live && <PaymentChip status={order.paymentStatus} />}
        {live && <FulfillmentChip status={order.fulfillmentStatus} />}
      </Stack>
      <p className="muted">
        {sourceLabel(order.source)} · confirmed{' '}
        {order.confirmedAt && formatShopDate(order.confirmedAt)}
        {order.confirmedBy && ` by ${order.confirmedBy}`}
        {order.paymentTerms === 'OnDelivery' && ' · pay on delivery'}
        {order.cancelledAt &&
          ` · cancelled ${formatShopDate(order.cancelledAt)}: ${order.cancelReason}`}
      </p>
      <ProblemAlert problem={problem} />
      <Stack
        direction="row"
        spacing={1}
        useFlexGap
        sx={{ mb: 2, flexWrap: 'wrap' }}
      >
        {order.status === 'Confirmed' && toIssue > 0 && can('sales.issue') && (
          <Button
            variant="contained"
            disabled={busy}
            onClick={() => setDialog('issue')}
          >
            Issue stock
          </Button>
        )}
        {live && order.balanceDue > 0 && can('payments.record') && (
          <Button
            variant="contained"
            disabled={busy}
            onClick={() => setDialog('pay')}
          >
            Record payment
          </Button>
        )}
        <Button
          variant="outlined"
          disabled={busy}
          onClick={() =>
            void salesApi
              .openPdf(order.id)
              .catch((e) => setProblem(describeError(e)))
          }
        >
          Print / PDF
        </Button>
        <Button
          variant="outlined"
          disabled={busy}
          onClick={async () => {
            const copy = await run(() => salesApi.duplicate(order.id));
            if (copy) navigate(`/sales/invoices/${copy.id}`);
          }}
        >
          Copy to new draft
        </Button>
        {order.status === 'Confirmed' &&
          can('sales.cancel') &&
          order.issues.length === 0 && (
            <Button
              color="error"
              disabled={busy}
              onClick={() => setDialog('cancel')}
            >
              Cancel invoice
            </Button>
          )}
      </Stack>

      <Paper elevation={0} className="permission-panel">
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={4}>
          <div>
            <Typography variant="subtitle2">Bill to</Typography>
            <div>{order.customerName ?? 'Cash sale'}</div>
            {order.customerCompany && <div>{order.customerCompany}</div>}
            {order.billingAddress && (
              <div style={{ whiteSpace: 'pre-line' }}>
                {order.billingAddress}
              </div>
            )}
            {order.customerPhone && <div>{order.customerPhone}</div>}
            {order.customerEmail && <div>{order.customerEmail}</div>}
          </div>
          <div>
            <Typography variant="subtitle2">
              {order.deliveryMethod === 'Delivery' ? 'Deliver to' : 'Delivery'}
            </Typography>
            <div style={{ whiteSpace: 'pre-line' }}>
              {order.deliveryMethod === 'Delivery'
                ? order.deliveryAddress
                : 'Customer pickup'}
            </div>
          </div>
        </Stack>
      </Paper>

      <Paper elevation={0} className="permission-panel">
        <TableContainer>
          <Table size="small" aria-label="Invoice lines">
            <TableHead>
              <TableRow>
                <TableCell>Item</TableCell>
                <TableCell align="right">Qty</TableCell>
                <TableCell align="right">Issued</TableCell>
                <TableCell align="right">Unit price</TableCell>
                <TableCell align="right">Discount</TableCell>
                <TableCell align="right">Amount</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {order.lines.map((l) => (
                <TableRow key={l.id}>
                  <TableCell>
                    <span className="mono">{l.sku}</span> {l.description}
                    {l.discountReason && (
                      <Typography variant="body2" className="muted">
                        Discount: {l.discountReason}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell align="right">{formatQty(l.quantity)}</TableCell>
                  <TableCell align="right">{formatQty(l.qtyIssued)}</TableCell>
                  <TableCell align="right">
                    {formatMoney(l.unitPrice)}
                    {l.unitPrice !== l.listPrice && (
                      <Typography variant="body2" className="muted">
                        list {formatMoney(l.listPrice)}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell align="right">
                    {l.lineDiscount + l.orderDiscountShare > 0
                      ? '−' + formatMoney(l.lineDiscount + l.orderDiscountShare)
                      : ''}
                  </TableCell>
                  <TableCell align="right">
                    {formatMoney(l.lineTotal)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
        <Totals
          subtotal={order.subtotal}
          discounts={order.lineDiscountTotal + order.orderDiscount}
          delivery={order.deliveryCharge}
          total={order.total}
          paid={order.amountPaid}
          showDelivery={order.deliveryMethod === 'Delivery'}
        />
        {order.discountReason && (
          <p className="muted">Order discount: {order.discountReason}</p>
        )}
        {order.notes && <p>Notes: {order.notes}</p>}
      </Paper>

      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
        <Paper elevation={0} className="permission-panel" sx={{ flex: 1 }}>
          <Typography variant="h6" component="h2">
            Payments
          </Typography>
          <Table size="small" aria-label="Payments">
            <TableBody>
              {order.payments.map((p) => (
                <TableRow key={p.id}>
                  <TableCell>
                    <span className="mono">{p.receiptNumber}</span>
                    <Typography variant="body2" className="muted">
                      {formatShopDate(p.receivedAt)} · {p.receivedBy}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    {methodLabel(p.method)}
                    {p.reference && (
                      <span className="muted"> · {p.reference}</span>
                    )}
                    {p.voidReason && (
                      <div className="muted">Void: {p.voidReason}</div>
                    )}
                  </TableCell>
                  <TableCell
                    align="right"
                    sx={
                      p.isVoided
                        ? { textDecoration: 'line-through' }
                        : undefined
                    }
                  >
                    {formatMoney(p.amount)}
                  </TableCell>
                  <TableCell>
                    {p.voidsPaymentId === null &&
                      !p.isVoided &&
                      can('payments.void') &&
                      order.status !== 'Cancelled' && (
                        <Button
                          size="small"
                          color="error"
                          onClick={() => setVoiding(p.id)}
                        >
                          Void
                        </Button>
                      )}
                  </TableCell>
                </TableRow>
              ))}
              {order.payments.length === 0 && (
                <TableRow>
                  <TableCell>No payments yet.</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </Paper>
        <Paper elevation={0} className="permission-panel" sx={{ flex: 1 }}>
          <Typography variant="h6" component="h2">
            Stock issued
          </Typography>
          <Table size="small" aria-label="Stock issues">
            <TableBody>
              {order.issues.map((i) => (
                <TableRow key={i.id}>
                  <TableCell>
                    <span className="mono">{i.issueNumber}</span>
                    <Typography variant="body2" className="muted">
                      {formatShopDate(i.postedAt)} · {i.postedBy}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    {i.lines
                      .map((l) => `${formatQty(l.quantity)} × ${l.sku}`)
                      .join(', ')}
                  </TableCell>
                </TableRow>
              ))}
              {order.issues.length === 0 && (
                <TableRow>
                  <TableCell>
                    Nothing issued yet. The stock is reserved.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </Paper>
      </Stack>

      {order.status !== 'Draft' && <AfterSalePanel order={order} />}

      {dialog === 'issue' && (
        <IssueDialog
          order={order}
          onClose={() => setDialog(null)}
          onIssue={async (lines, key) => {
            if (await run(() => salesApi.issue(order.id, lines, key)))
              setDialog(null);
          }}
        />
      )}
      {dialog === 'pay' && (
        <PaymentDialog
          balance={order.balanceDue}
          onClose={() => setDialog(null)}
          onPay={async (input) => {
            if (await run(() => salesApi.pay(order.id, input))) setDialog(null);
          }}
        />
      )}
      {dialog === 'cancel' && (
        <ReasonDialog
          title={`Cancel ${order.invoiceNumber}?`}
          warning="The invoice keeps its number and is marked cancelled. Its reserved stock is released."
          action="Cancel invoice"
          onClose={() => setDialog(null)}
          onSubmit={async (reason) => {
            if (await run(() => salesApi.cancel(order.id, reason)))
              setDialog(null);
          }}
        />
      )}
      {voiding && (
        <ReasonDialog
          title="Void this payment?"
          warning="A void entry is added that cancels the payment. Nothing is deleted. Use this for mistakes, not refunds."
          action="Void payment"
          onClose={() => setVoiding(null)}
          onSubmit={async (reason) => {
            const key = crypto.randomUUID();
            if (await run(() => salesApi.voidPayment(voiding, reason, key)))
              setVoiding(null);
          }}
        />
      )}
    </>
  );
}

function IssueDialog({
  order,
  onClose,
  onIssue,
}: {
  order: Order;
  onClose: () => void;
  onIssue: (
    lines: { orderLineId: string; quantity: number }[],
    key: string,
  ) => Promise<void>;
}) {
  const open = order.lines.filter((l) => l.qtyToIssue > 0);
  const [qty, setQty] = useState<Record<string, string>>(
    Object.fromEntries(open.map((l) => [l.id, String(l.qtyToIssue)])),
  );
  // One key per dialog: a double click or retry issues once.
  const [key] = useState(() => crypto.randomUUID());
  const lines = open
    .map((l) => ({
      orderLineId: l.id,
      quantity: parseNumber(qty[l.id] ?? '') ?? 0,
    }))
    .filter((l) => l.quantity > 0);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Issue stock</DialogTitle>
      <DialogContent>
        <p>
          Stock goes down now. Issue only what is handed over or dispatched.
        </p>
        <Table size="small" aria-label="Quantities to issue">
          <TableBody>
            {open.map((l) => (
              <TableRow key={l.id}>
                <TableCell>
                  <span className="mono">{l.sku}</span> {l.description}
                </TableCell>
                <TableCell align="right" className="muted">
                  {formatQty(l.qtyToIssue)} to go
                </TableCell>
                <TableCell align="right">
                  <TextField
                    size="small"
                    value={qty[l.id] ?? ''}
                    onChange={(e) => setQty({ ...qty, [l.id]: e.target.value })}
                    slotProps={{
                      htmlInput: {
                        inputMode: 'decimal',
                        'aria-label': `Issue quantity of ${l.sku}`,
                        style: { textAlign: 'right', width: 70 },
                      },
                    }}
                  />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={lines.length === 0}
          onClick={() => void onIssue(lines, key)}
        >
          Issue
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export function PaymentDialog({
  balance,
  onClose,
  onPay,
}: {
  balance: number;
  onClose: () => void;
  onPay: (input: {
    method: PaymentMethod;
    amount: number;
    reference: string | null;
    idempotencyKey: string;
  }) => Promise<void>;
}) {
  const [method, setMethod] = useState<PaymentMethod>('Cash');
  const [amount, setAmount] = useState(balance.toFixed(2));
  const [reference, setReference] = useState('');
  const [key] = useState(() => crypto.randomUUID());
  const value = parseNumber(amount);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Record payment</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            select
            label="Method"
            value={method}
            slotProps={select}
            onChange={(e) => setMethod(e.target.value as PaymentMethod)}
          >
            {paymentMethods.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </TextField>
          <TextField
            label="Amount $"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            helperText={`Balance due ${formatMoney(balance)}`}
            slotProps={{ htmlInput: { inputMode: 'decimal' } }}
          />
          <TextField
            label="Reference (optional)"
            value={reference}
            onChange={(e) => setReference(e.target.value)}
            helperText="EFTPOS receipt or bank reference"
          />
          {value !== null && value > balance && (
            <Alert severity="warning">
              This is more than the balance. The invoice will show money owed to
              the customer.
            </Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!value || value <= 0}
          onClick={() =>
            void onPay({
              method,
              amount: value ?? 0,
              reference: reference || null,
              idempotencyKey: key,
            })
          }
        >
          Record {value ? formatMoney(value) : ''}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function ReasonDialog({
  title,
  warning,
  action,
  onClose,
  onSubmit,
}: {
  title: string;
  warning: string;
  action: string;
  onClose: () => void;
  onSubmit: (reason: string) => Promise<void>;
}) {
  const [reason, setReason] = useState('');
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Alert severity="warning" sx={{ mb: 2 }}>
          {warning}
        </Alert>
        <TextField
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          fullWidth
          required
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Back</Button>
        <Button
          color="error"
          variant="contained"
          disabled={!reason.trim()}
          onClick={() => void onSubmit(reason)}
        >
          {action}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
