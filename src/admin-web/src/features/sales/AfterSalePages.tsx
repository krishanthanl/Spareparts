import { useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
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
import { describeError, fieldError, type ApiProblem } from '../../api/problems';
import { formatMoney, formatShopDate } from '../../lib/shop';
import { ProblemAlert } from '../catalog/ProblemAlert';
import { useAuth } from '../auth/useAuth';
import { DocumentPagination } from '../inventory/components';
import { formatQty, parseNumber } from '../inventory/api';
import { methodLabel } from './api';
import {
  afterSaleApi,
  refundStatusLabel,
  type DeliveryZone,
  type DeliveryZoneInput,
  type Refund,
  type RefundStatus,
  type ShipmentStatus,
} from './afterSaleApi';
import {
  CompleteRefundDialog,
  RefundActions,
  RefundChip,
  ShipmentActions,
  ShipmentChip,
  TextDialog,
} from './AfterSalePanel';

const select = { select: { native: true } } as const;

const InvoiceLink = ({ id, number }: { id: string; number: string | null }) => (
  <Link to={`/sales/invoices/${id}`} className="mono">
    {number ?? 'draft'}
  </Link>
);

function ListShell({
  title,
  intro,
  q,
  setQ,
  filter,
  children,
}: {
  title: string;
  intro: string;
  q: string;
  setQ: (q: string) => void;
  filter?: ReactNode;
  children: ReactNode;
}) {
  return (
    <>
      <Typography variant="h4" component="h1">
        {title}
      </Typography>
      <p className="muted">{intro}</p>
      <Paper elevation={0} className="permission-panel">
        <Stack
          direction={{ xs: 'column', md: 'row' }}
          spacing={2}
          sx={{ mb: 2 }}
        >
          <TextField
            size="small"
            label="Search number, invoice or customer"
            value={q}
            onChange={(e) => setQ(e.target.value)}
            sx={{ flex: 1 }}
          />
          {filter}
        </Stack>
        {children}
      </Paper>
    </>
  );
}

// ---------------- Returns ----------------

export function ReturnsPage() {
  const [page, setPage] = useState(1);
  const [q, setQ] = useState('');
  const list = useQuery({
    queryKey: ['returns', page, q],
    queryFn: () => afterSaleApi.returns({ page, q }),
  });
  return (
    <ListShell
      title="Returns"
      intro="To take goods back, open the invoice and choose Return goods. Each return issues its credit note automatically."
      q={q}
      setQ={(v) => {
        setQ(v);
        setPage(1);
      }}
    >
      {list.isError && <ProblemAlert problem={describeError(list.error)} />}
      <TableContainer>
        <Table size="small" aria-label="Returns">
          <TableHead>
            <TableRow>
              <TableCell>Return</TableCell>
              <TableCell>Invoice</TableCell>
              <TableCell>Goods</TableCell>
              <TableCell>Reason</TableCell>
              <TableCell align="right">Credit</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((r) => (
              <TableRow key={r.id}>
                <TableCell>
                  <span className="mono">{r.returnNumber}</span>
                  <Typography variant="body2" className="muted">
                    {formatShopDate(r.postedAt)} · {r.postedBy}
                  </Typography>
                </TableCell>
                <TableCell>
                  <InvoiceLink id={r.orderId} number={r.invoiceNumber} />
                  <div className="muted">{r.customerName ?? 'Cash sale'}</div>
                </TableCell>
                <TableCell>
                  {r.lines
                    .map(
                      (l) =>
                        `${formatQty(l.quantity)} × ${l.sku}${l.disposition === 'WriteOff' ? ' (written off)' : ''}`,
                    )
                    .join(', ')}
                </TableCell>
                <TableCell>{r.reason}</TableCell>
                <TableCell align="right">
                  {formatMoney(r.creditTotal)}
                  {r.creditNoteNumber && (
                    <div className="muted mono">{r.creditNoteNumber}</div>
                  )}
                </TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>No returns.</TableCell>
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
    </ListShell>
  );
}

// ---------------- Credit notes ----------------

export function CreditNotesPage() {
  const [page, setPage] = useState(1);
  const [q, setQ] = useState('');
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const list = useQuery({
    queryKey: ['credit-notes', page, q],
    queryFn: () => afterSaleApi.creditNotes({ page, q }),
  });
  return (
    <ListShell
      title="Credit notes"
      intro="Credit notes lower what a customer owes. They are not cash: paying money back is a refund."
      q={q}
      setQ={(v) => {
        setQ(v);
        setPage(1);
      }}
    >
      <ProblemAlert
        problem={problem ?? (list.isError ? describeError(list.error) : null)}
      />
      <TableContainer>
        <Table size="small" aria-label="Credit notes">
          <TableHead>
            <TableRow>
              <TableCell>Credit note</TableCell>
              <TableCell>Invoice</TableCell>
              <TableCell>Reason</TableCell>
              <TableCell align="right">Amount</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((c) => (
              <TableRow key={c.id}>
                <TableCell>
                  <span className="mono">{c.creditNoteNumber}</span>
                  <Typography variant="body2" className="muted">
                    {formatShopDate(c.issuedAt)} · {c.issuedBy}
                  </Typography>
                </TableCell>
                <TableCell>
                  <InvoiceLink id={c.orderId} number={c.invoiceNumber} />
                  <div className="muted">{c.customerName ?? 'Cash sale'}</div>
                </TableCell>
                <TableCell>{c.reason}</TableCell>
                <TableCell align="right">{formatMoney(c.total)}</TableCell>
                <TableCell>
                  <Button
                    size="small"
                    onClick={() =>
                      void afterSaleApi
                        .openCreditNotePdf(c.id)
                        .catch((e) => setProblem(describeError(e)))
                    }
                  >
                    PDF
                  </Button>
                </TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>No credit notes.</TableCell>
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
    </ListShell>
  );
}

// ---------------- Refunds ----------------

export function RefundsPage() {
  const queryClient = useQueryClient();
  const { can, state } = useAuth();
  const me = state.status === 'signedIn' ? state.user.id : null;
  const [page, setPage] = useState(1);
  const [q, setQ] = useState('');
  const [status, setStatus] = useState<RefundStatus | ''>('');
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);
  const [completing, setCompleting] = useState<Refund | null>(null);
  const [ending, setEnding] = useState<{
    refund: Refund;
    kind: 'fail' | 'cancel';
  } | null>(null);
  const list = useQuery({
    queryKey: ['refunds', page, q, status],
    queryFn: () => afterSaleApi.refunds({ page, q, status }),
  });
  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    setProblem(null);
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: ['refunds'] });
      return true;
    } catch (error) {
      setProblem(describeError(error));
      return false;
    } finally {
      setBusy(false);
    }
  };
  return (
    <ListShell
      title="Refunds"
      intro="Refunds are requested on the invoice, approved by another admin, then marked paid out with evidence."
      q={q}
      setQ={(v) => {
        setQ(v);
        setPage(1);
      }}
      filter={
        <TextField
          select
          size="small"
          label="Status"
          value={status}
          slotProps={select}
          sx={{ minWidth: 220 }}
          onChange={(e) => {
            setStatus(e.target.value as RefundStatus | '');
            setPage(1);
          }}
        >
          <option value="">All</option>
          {(Object.keys(refundStatusLabel) as RefundStatus[]).map((s) => (
            <option key={s} value={s}>
              {refundStatusLabel[s]}
            </option>
          ))}
        </TextField>
      }
    >
      <ProblemAlert
        problem={problem ?? (list.isError ? describeError(list.error) : null)}
      />
      <TableContainer>
        <Table size="small" aria-label="Refunds">
          <TableHead>
            <TableRow>
              <TableCell>Refund</TableCell>
              <TableCell>Invoice</TableCell>
              <TableCell>Details</TableCell>
              <TableCell align="right">Amount</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((r) => (
              <TableRow key={r.id}>
                <TableCell>
                  <span className="mono">{r.refundNumber}</span>{' '}
                  <RefundChip refund={r} />
                  <Typography variant="body2" className="muted">
                    {formatShopDate(r.requestedAt)} · {r.requestedBy}
                  </Typography>
                </TableCell>
                <TableCell>
                  <InvoiceLink id={r.orderId} number={r.invoiceNumber} />
                  <div className="muted">{r.customerName ?? 'Cash sale'}</div>
                </TableCell>
                <TableCell>
                  {methodLabel(r.method)} · {r.reason}
                  {r.evidenceReference && (
                    <div className="muted">{r.evidenceReference}</div>
                  )}
                </TableCell>
                <TableCell align="right">{formatMoney(r.amount)}</TableCell>
                <TableCell>
                  <RefundActions
                    refund={r}
                    me={me}
                    can={can}
                    busy={busy}
                    onApprove={() =>
                      void run(() => afterSaleApi.approveRefund(r.id))
                    }
                    onComplete={() => setCompleting(r)}
                    onEnd={(kind) => setEnding({ refund: r, kind })}
                  />
                </TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>No refunds.</TableCell>
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
      {completing && (
        <CompleteRefundDialog
          refund={completing}
          onClose={() => setCompleting(null)}
          onSave={async (input) => {
            if (
              await run(() => afterSaleApi.completeRefund(completing.id, input))
            )
              setCompleting(null);
          }}
        />
      )}
      {ending && (
        <TextDialog
          title={
            ending.kind === 'fail'
              ? `Mark ${ending.refund.refundNumber} as failed?`
              : `Cancel ${ending.refund.refundNumber}?`
          }
          label="Reason"
          action={ending.kind === 'fail' ? 'Mark failed' : 'Cancel refund'}
          onClose={() => setEnding(null)}
          onSave={async (reason) => {
            const id = ending.refund.id;
            if (
              await run(() =>
                ending.kind === 'fail'
                  ? afterSaleApi.failRefund(id, reason)
                  : afterSaleApi.cancelRefund(id, reason),
              )
            )
              setEnding(null);
          }}
        />
      )}
    </ListShell>
  );
}

// ---------------- Shipments ----------------

export function ShipmentsPage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [q, setQ] = useState('');
  const [status, setStatus] = useState<ShipmentStatus | ''>('Pending');
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);
  const list = useQuery({
    queryKey: ['shipments', page, q, status],
    queryFn: () => afterSaleApi.shipments({ page, q, status }),
  });
  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    setProblem(null);
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: ['shipments'] });
      return true;
    } catch (error) {
      setProblem(describeError(error));
      return false;
    } finally {
      setBusy(false);
    }
  };
  return (
    <ListShell
      title="Shipments"
      intro="Create shipments from a delivery invoice. Dispatch takes the goods out of stock once; delivered only records the date."
      q={q}
      setQ={(v) => {
        setQ(v);
        setPage(1);
      }}
      filter={
        <TextField
          select
          size="small"
          label="Status"
          value={status}
          slotProps={select}
          sx={{ minWidth: 160 }}
          onChange={(e) => {
            setStatus(e.target.value as ShipmentStatus | '');
            setPage(1);
          }}
        >
          <option value="">All</option>
          <option value="Pending">To dispatch</option>
          <option value="Dispatched">On the way</option>
          <option value="Delivered">Delivered</option>
          <option value="Cancelled">Cancelled</option>
        </TextField>
      }
    >
      <ProblemAlert
        problem={problem ?? (list.isError ? describeError(list.error) : null)}
      />
      <TableContainer>
        <Table size="small" aria-label="Shipments">
          <TableHead>
            <TableRow>
              <TableCell>Shipment</TableCell>
              <TableCell>Invoice</TableCell>
              <TableCell>Deliver to</TableCell>
              <TableCell>Goods</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((s) => (
              <TableRow key={s.id}>
                <TableCell>
                  <span className="mono">{s.shipmentNumber}</span>{' '}
                  <ShipmentChip status={s.status} />
                  <Typography variant="body2" className="muted">
                    {[s.carrier, s.trackingReference]
                      .filter(Boolean)
                      .join(' · ')}
                  </Typography>
                </TableCell>
                <TableCell>
                  <InvoiceLink id={s.orderId} number={s.invoiceNumber} />
                  <div className="muted">{s.customerName ?? ''}</div>
                </TableCell>
                <TableCell sx={{ whiteSpace: 'pre-line' }}>
                  {s.deliveryAddress}
                </TableCell>
                <TableCell>
                  {s.lines
                    .map((l) => `${formatQty(l.quantity)} × ${l.sku}`)
                    .join(', ')}
                </TableCell>
                <TableCell align="right">
                  <ShipmentActions shipment={s} busy={busy} run={run} />
                </TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>No shipments.</TableCell>
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
    </ListShell>
  );
}

// ---------------- Delivery settings and zones ----------------

export function DeliverySettingsPage() {
  const queryClient = useQueryClient();
  const settings = useQuery({
    queryKey: ['delivery-settings'],
    queryFn: afterSaleApi.settings,
  });
  const zones = useQuery({
    queryKey: ['delivery-zones'],
    queryFn: afterSaleApi.zones,
  });
  const [editing, setEditing] = useState<DeliveryZone | 'new' | null>(null);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [saved, setSaved] = useState(false);
  const s = settings.data;
  return (
    <>
      <Typography variant="h4" component="h1">
        Delivery settings
      </Typography>
      <p className="muted">
        Charges are quoted from the zone that covers the postcode and copied
        onto each order. Changing them here never changes existing orders.
      </p>
      <ProblemAlert problem={problem} />
      {s && (
        <SettingsForm
          key={s.version}
          initial={s}
          onSave={async (input) => {
            setProblem(null);
            setSaved(false);
            try {
              await afterSaleApi.saveSettings(input);
              await queryClient.invalidateQueries({
                queryKey: ['delivery-settings'],
              });
              setSaved(true);
            } catch (e) {
              setProblem(describeError(e));
            }
          }}
        />
      )}
      {saved && (
        <Alert severity="success" sx={{ mb: 2 }}>
          Saved.
        </Alert>
      )}
      <Paper elevation={0} className="permission-panel">
        <Stack
          direction="row"
          sx={{ justifyContent: 'space-between', alignItems: 'center' }}
        >
          <Typography variant="h6" component="h2">
            Zones
          </Typography>
          <Button variant="contained" onClick={() => setEditing('new')}>
            Add zone
          </Button>
        </Stack>
        <TableContainer>
          <Table size="small" aria-label="Delivery zones">
            <TableHead>
              <TableRow>
                <TableCell>Zone</TableCell>
                <TableCell>Postcodes</TableCell>
                <TableCell align="right">Charge</TableCell>
                <TableCell align="right">Rural extra</TableCell>
                <TableCell align="right">Free over</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {zones.data?.map((z) => (
                <TableRow
                  key={z.id}
                  sx={z.isActive ? undefined : { opacity: 0.55 }}
                >
                  <TableCell>
                    {z.name}
                    {!z.isActive && ' (off)'}
                    {z.servicePromise && (
                      <div className="muted">{z.servicePromise}</div>
                    )}
                  </TableCell>
                  <TableCell className="mono">
                    {z.postcodes
                      .map((p) =>
                        p.from === p.to ? p.from : `${p.from}-${p.to}`,
                      )
                      .join(', ')}
                  </TableCell>
                  <TableCell align="right">{formatMoney(z.charge)}</TableCell>
                  <TableCell align="right">
                    {formatMoney(z.ruralSurcharge)}
                  </TableCell>
                  <TableCell align="right">
                    {z.freeDeliveryThreshold === null
                      ? ''
                      : formatMoney(z.freeDeliveryThreshold)}
                  </TableCell>
                  <TableCell>
                    <Button size="small" onClick={() => setEditing(z)}>
                      Edit
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
              {zones.data?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>
                    No zones yet. Until you add one, staff type the delivery
                    charge by hand.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>
      {editing && (
        <ZoneDialog
          zone={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={async () => {
            setEditing(null);
            await queryClient.invalidateQueries({
              queryKey: ['delivery-zones'],
            });
          }}
        />
      )}
    </>
  );
}

function SettingsForm({
  initial,
  onSave,
}: {
  initial: {
    deliveryEnabled: boolean;
    pickupEnabled: boolean;
    freeDeliveryThreshold: number | null;
    version: number;
  };
  onSave: (input: {
    deliveryEnabled: boolean;
    pickupEnabled: boolean;
    freeDeliveryThreshold: number | null;
    version?: number;
  }) => Promise<void>;
}) {
  const [delivery, setDelivery] = useState(initial.deliveryEnabled);
  const [pickup, setPickup] = useState(initial.pickupEnabled);
  const [free, setFree] = useState(
    initial.freeDeliveryThreshold === null
      ? ''
      : String(initial.freeDeliveryThreshold),
  );
  return (
    <Paper elevation={0} className="permission-panel">
      <Stack
        direction={{ xs: 'column', md: 'row' }}
        spacing={2}
        sx={{ alignItems: { md: 'center' } }}
      >
        <FormControlLabel
          control={
            <Checkbox
              checked={delivery}
              onChange={(e) => setDelivery(e.target.checked)}
            />
          }
          label="Offer delivery"
        />
        <FormControlLabel
          control={
            <Checkbox
              checked={pickup}
              onChange={(e) => setPickup(e.target.checked)}
            />
          }
          label="Offer pickup"
        />
        <TextField
          label="Free delivery over $ (optional)"
          value={free}
          onChange={(e) => setFree(e.target.value)}
          slotProps={{ htmlInput: { inputMode: 'decimal' } }}
          helperText="Zones can set their own"
        />
        <Button
          variant="contained"
          onClick={() =>
            void onSave({
              deliveryEnabled: delivery,
              pickupEnabled: pickup,
              freeDeliveryThreshold: free.trim() ? parseNumber(free) : null,
              version: initial.version || undefined,
            })
          }
        >
          Save
        </Button>
      </Stack>
      <Typography variant="body2" className="muted" sx={{ mt: 1 }}>
        GST: not registered. No GST is charged on goods or delivery.
      </Typography>
    </Paper>
  );
}

const parseRanges = (text: string) =>
  text
    .split(/[\s,]+/)
    .filter(Boolean)
    .map((part) => {
      const [from, to] = part.split('-');
      return { from: from.trim(), to: to?.trim() || null };
    });

function ZoneDialog({
  zone,
  onClose,
  onSaved,
}: {
  zone: DeliveryZone | null;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [name, setName] = useState(zone?.name ?? '');
  const [charge, setCharge] = useState(zone ? String(zone.charge) : '');
  const [rural, setRural] = useState(zone ? String(zone.ruralSurcharge) : '0');
  const [free, setFree] = useState(
    zone?.freeDeliveryThreshold == null
      ? ''
      : String(zone.freeDeliveryThreshold),
  );
  const [promise, setPromise] = useState(zone?.servicePromise ?? '');
  const [active, setActive] = useState(zone?.isActive ?? true);
  const [postcodes, setPostcodes] = useState(
    zone?.postcodes
      .map((p) => (p.from === p.to ? p.from : `${p.from}-${p.to}`))
      .join(', ') ?? '',
  );
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const save = async () => {
    const input: DeliveryZoneInput = {
      name,
      charge: parseNumber(charge) ?? 0,
      ruralSurcharge: parseNumber(rural) ?? 0,
      freeDeliveryThreshold: free.trim() ? parseNumber(free) : null,
      servicePromise: promise || null,
      isActive: active,
      postcodes: parseRanges(postcodes),
      version: zone?.version,
    };
    setProblem(null);
    try {
      if (zone) await afterSaleApi.updateZone(zone.id, input);
      else await afterSaleApi.createZone(input);
      await onSaved();
    } catch (e) {
      setProblem(describeError(e));
    }
  };
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>
        {zone ? `Edit ${zone.name}` : 'New delivery zone'}
      </DialogTitle>
      <DialogContent>
        <ProblemAlert problem={problem} />
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label="Name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={!!fieldError(problem, 'name')}
            helperText={fieldError(problem, 'name')}
          />
          <TextField
            label="Postcodes"
            value={postcodes}
            onChange={(e) => setPostcodes(e.target.value)}
            helperText={
              fieldError(problem, 'postcodes') ??
              'Single postcodes or ranges, e.g. 3200-3299, 3400'
            }
            error={!!fieldError(problem, 'postcodes')}
          />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              label="Charge $"
              value={charge}
              onChange={(e) => setCharge(e.target.value)}
              slotProps={{ htmlInput: { inputMode: 'decimal' } }}
            />
            <TextField
              label="Rural extra $"
              value={rural}
              onChange={(e) => setRural(e.target.value)}
              slotProps={{ htmlInput: { inputMode: 'decimal' } }}
            />
            <TextField
              label="Free over $"
              value={free}
              onChange={(e) => setFree(e.target.value)}
              slotProps={{ htmlInput: { inputMode: 'decimal' } }}
            />
          </Stack>
          <TextField
            label="Service promise (optional)"
            value={promise}
            onChange={(e) => setPromise(e.target.value)}
            helperText="e.g. 1-2 working days"
          />
          <FormControlLabel
            control={
              <Checkbox
                checked={active}
                onChange={(e) => setActive(e.target.checked)}
              />
            }
            label="Active"
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!name.trim() || !postcodes.trim()}
          onClick={() => void save()}
        >
          Save zone
        </Button>
      </DialogActions>
    </Dialog>
  );
}
