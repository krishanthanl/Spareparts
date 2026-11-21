import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { describeError, type ApiProblem } from '../../api/problems';
import { formatMoney, formatShopDate } from '../../lib/shop';
import { ProblemAlert } from '../catalog/ProblemAlert';
import { useAuth } from '../auth/useAuth';
import { formatQty, parseNumber } from '../inventory/api';
import {
  methodLabel,
  paymentMethods,
  type Order,
  type PaymentMethod,
} from './api';
import {
  afterSaleApi,
  carriers,
  refundStatusLabel,
  type OrderAfterSale,
  type Refund,
  type ReturnDisposition,
  type Shipment,
} from './afterSaleApi';

const select = { select: { native: true } } as const;
const money = { htmlInput: { inputMode: 'decimal' as const } };

type DialogKind = 'ship' | 'return' | 'credit' | 'refund' | null;

/**
 * Everything after the sale on one invoice (ADR 0006): balance, shipments, returns, credit notes and refunds.
 * Every action re-reads the invoice and this panel from the server.
 */
export function AfterSalePanel({ order }: { order: Order }) {
  const queryClient = useQueryClient();
  const { can, state } = useAuth();
  const me = state.status === 'signedIn' ? state.user.id : null;
  const data = useQuery({
    queryKey: ['after-sale', order.id],
    queryFn: () => afterSaleApi.forOrder(order.id),
  });
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);
  const [dialog, setDialog] = useState<DialogKind>(null);
  const [completing, setCompleting] = useState<Refund | null>(null);
  const [ending, setEnding] = useState<{
    refund: Refund;
    kind: 'fail' | 'cancel';
  } | null>(null);

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    setProblem(null);
    try {
      await action();
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['after-sale', order.id] }),
        queryClient.invalidateQueries({ queryKey: ['order', order.id] }),
        queryClient.invalidateQueries({ queryKey: ['orders'] }),
      ]);
      return true;
    } catch (error) {
      setProblem(describeError(error));
      return false;
    } finally {
      setBusy(false);
    }
  };

  if (!data.data)
    return data.isError ? (
      <ProblemAlert problem={describeError(data.error)} />
    ) : null;
  const a = data.data;
  const b = a.balance;
  const live = order.status === 'Confirmed' || order.status === 'Completed';
  const canReturn =
    live && a.returnable.some((l) => l.returnable > 0) && can('sales.return');
  const toShip =
    order.lines.reduce((s, l) => s + l.qtyToIssue, 0) - pendingQty(a.shipments);

  return (
    <>
      <ProblemAlert problem={problem} />
      <Paper elevation={0} className="permission-panel">
        <Typography variant="h6" component="h2">
          Balance
        </Typography>
        <Table size="small" aria-label="Invoice balance">
          <TableBody>
            <Row label="Invoice total" value={b.total} />
            {b.credits > 0 && <Row label="Credit notes" value={-b.credits} />}
            <Row label="Received (after voids)" value={b.received} />
            {b.refunded > 0 && <Row label="Refunded" value={-b.refunded} />}
            {b.balanceDue > 0 && (
              <Row label="Balance due" value={b.balanceDue} bold />
            )}
            {b.owedToCustomer > 0 && (
              <Row label="Owed to customer" value={b.owedToCustomer} bold />
            )}
            {b.pendingRefunds > 0 && (
              <Row label="Refunds in progress" value={b.pendingRefunds} />
            )}
          </TableBody>
        </Table>
        <Typography variant="body2" className="muted" sx={{ mt: 1 }}>
          Not GST registered: no GST charged or credited.
        </Typography>
        <Stack
          direction="row"
          spacing={1}
          useFlexGap
          sx={{ mt: 2, flexWrap: 'wrap' }}
        >
          {live &&
            order.deliveryMethod === 'Delivery' &&
            toShip > 0 &&
            can('fulfillment.update') && (
              <Button
                variant="outlined"
                disabled={busy}
                onClick={() => setDialog('ship')}
              >
                New shipment
              </Button>
            )}
          {canReturn && (
            <Button
              variant="outlined"
              disabled={busy}
              onClick={() => setDialog('return')}
            >
              Return goods
            </Button>
          )}
          {live && can('creditnotes.issue') && (
            <Button
              variant="outlined"
              disabled={busy}
              onClick={() => setDialog('credit')}
            >
              Credit note
            </Button>
          )}
          {(b.refundable > 0 || b.refundableOnCancel > 0) &&
            can('refunds.create') && (
              <Button
                variant="outlined"
                disabled={busy}
                onClick={() => setDialog('refund')}
              >
                Refund
              </Button>
            )}
        </Stack>
      </Paper>

      {a.shipments.length > 0 && (
        <Paper elevation={0} className="permission-panel">
          <Typography variant="h6" component="h2">
            Shipments
          </Typography>
          <Table size="small" aria-label="Shipments">
            <TableBody>
              {a.shipments.map((s) => (
                <TableRow key={s.id}>
                  <TableCell>
                    <span className="mono">{s.shipmentNumber}</span>{' '}
                    <ShipmentChip status={s.status} />
                    <Typography variant="body2" className="muted">
                      {[s.carrier, s.trackingReference, s.issueNumber]
                        .filter(Boolean)
                        .join(' · ')}
                      {s.dispatchedAt &&
                        ` · sent ${formatShopDate(s.dispatchedAt)}`}
                      {s.deliveredAt &&
                        ` · delivered ${formatShopDate(s.deliveredAt)}`}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    {s.lines
                      .map((l) => `${formatQty(l.quantity)} × ${l.sku}`)
                      .join(', ')}
                  </TableCell>
                  <TableCell align="right">
                    {can('fulfillment.update') && (
                      <ShipmentActions shipment={s} busy={busy} run={run} />
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}

      {(a.returns.length > 0 || a.creditNotes.length > 0) && (
        <Paper elevation={0} className="permission-panel">
          <Typography variant="h6" component="h2">
            Returns and credit notes
          </Typography>
          <Table size="small" aria-label="Credit notes">
            <TableBody>
              {a.creditNotes.map((c) => (
                <TableRow key={c.id}>
                  <TableCell>
                    <span className="mono">{c.creditNoteNumber}</span>
                    {c.returnNumber && (
                      <span className="muted"> for {c.returnNumber}</span>
                    )}
                    <Typography variant="body2" className="muted">
                      {formatShopDate(c.issuedAt)} · {c.issuedBy} · {c.reason}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    {c.lines
                      .map(
                        (l) =>
                          (l.quantity ? `${formatQty(l.quantity)} × ` : '') +
                          l.description,
                      )
                      .join(', ')}
                  </TableCell>
                  <TableCell align="right">−{formatMoney(c.total)}</TableCell>
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
              {a.returns
                .flatMap((r) => r.lines.map((l) => ({ r, l })))
                .filter(({ l }) => l.disposition === 'WriteOff')
                .map(({ r, l }) => (
                  <TableRow key={r.id + l.orderLineId}>
                    <TableCell colSpan={4} className="muted">
                      {r.returnNumber}: {formatQty(l.quantity)} × {l.sku}{' '}
                      written off (not restocked)
                    </TableCell>
                  </TableRow>
                ))}
            </TableBody>
          </Table>
        </Paper>
      )}

      {a.refunds.length > 0 && (
        <Paper elevation={0} className="permission-panel">
          <Typography variant="h6" component="h2">
            Refunds
          </Typography>
          <Table size="small" aria-label="Refunds">
            <TableBody>
              {a.refunds.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>
                    <span className="mono">{r.refundNumber}</span>{' '}
                    <RefundChip refund={r} />
                    <Typography variant="body2" className="muted">
                      {methodLabel(r.method)} · asked by {r.requestedBy} ·{' '}
                      {r.reason}
                      {r.approvedBy && ` · approved by ${r.approvedBy}`}
                      {r.evidenceReference && ` · ${r.evidenceReference}`}
                      {r.failureReason && ` · ${r.failureReason}`}
                    </Typography>
                  </TableCell>
                  <TableCell align="right">{formatMoney(r.amount)}</TableCell>
                  <TableCell align="right">
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
            </TableBody>
          </Table>
        </Paper>
      )}

      {dialog === 'ship' && (
        <ShipmentDialog
          order={order}
          after={a}
          onClose={() => setDialog(null)}
          onSave={async (input) => {
            if (await run(() => afterSaleApi.createShipment(order.id, input)))
              setDialog(null);
          }}
        />
      )}
      {dialog === 'return' && (
        <ReturnDialog
          after={a}
          onClose={() => setDialog(null)}
          onSave={async (input) => {
            if (await run(() => afterSaleApi.createReturn(order.id, input)))
              setDialog(null);
          }}
        />
      )}
      {dialog === 'credit' && (
        <CreditDialog
          order={order}
          after={a}
          onClose={() => setDialog(null)}
          onSave={async (input) => {
            if (await run(() => afterSaleApi.createCreditNote(order.id, input)))
              setDialog(null);
          }}
        />
      )}
      {dialog === 'refund' && (
        <RefundDialog
          after={a}
          onClose={() => setDialog(null)}
          onSave={async (input) => {
            if (await run(() => afterSaleApi.requestRefund(order.id, input)))
              setDialog(null);
          }}
        />
      )}
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
            const ok = await run(() =>
              ending.kind === 'fail'
                ? afterSaleApi.failRefund(id, reason)
                : afterSaleApi.cancelRefund(id, reason),
            );
            if (ok) setEnding(null);
          }}
        />
      )}
    </>
  );
}

const pendingQty = (shipments: Shipment[]) =>
  shipments
    .filter((s) => s.status === 'Pending')
    .reduce((sum, s) => sum + s.lines.reduce((x, l) => x + l.quantity, 0), 0);

function Row({
  label,
  value,
  bold,
}: {
  label: string;
  value: number;
  bold?: boolean;
}) {
  return (
    <TableRow>
      <TableCell sx={bold ? { fontWeight: 700 } : undefined}>{label}</TableCell>
      <TableCell align="right" sx={bold ? { fontWeight: 700 } : undefined}>
        {value < 0 ? '−' + formatMoney(-value) : formatMoney(value)}
      </TableCell>
    </TableRow>
  );
}

export function ShipmentChip({ status }: { status: Shipment['status'] }) {
  const color =
    status === 'Delivered'
      ? 'success'
      : status === 'Dispatched'
        ? 'info'
        : status === 'Cancelled'
          ? 'default'
          : 'warning';
  return <Chip size="small" color={color} label={status} />;
}

export function RefundChip({ refund }: { refund: Refund }) {
  const s = refund.status;
  const color =
    s === 'Succeeded'
      ? 'success'
      : s === 'Failed'
        ? 'error'
        : s === 'Cancelled'
          ? 'default'
          : 'warning';
  return <Chip size="small" color={color} label={refundStatusLabel[s]} />;
}

export function ShipmentActions({
  shipment,
  busy,
  run,
}: {
  shipment: Shipment;
  busy: boolean;
  run: (action: () => Promise<unknown>) => Promise<boolean>;
}) {
  const [dispatching, setDispatching] = useState(false);
  if (shipment.status === 'Pending')
    return (
      <>
        <Button
          size="small"
          variant="contained"
          disabled={busy}
          onClick={() => setDispatching(true)}
        >
          Dispatch
        </Button>
        <Button
          size="small"
          color="error"
          disabled={busy}
          onClick={() =>
            void run(() => afterSaleApi.cancelShipment(shipment.id))
          }
        >
          Cancel
        </Button>
        {dispatching && (
          <DispatchDialog
            shipment={shipment}
            onClose={() => setDispatching(false)}
            onSave={async (input) => {
              if (await run(() => afterSaleApi.dispatch(shipment.id, input)))
                setDispatching(false);
            }}
          />
        )}
      </>
    );
  if (shipment.status === 'Dispatched')
    return (
      <Button
        size="small"
        disabled={busy}
        onClick={() => void run(() => afterSaleApi.deliver(shipment.id))}
      >
        Mark delivered
      </Button>
    );
  return null;
}

export function RefundActions({
  refund,
  me,
  can,
  busy,
  onApprove,
  onComplete,
  onEnd,
}: {
  refund: Refund;
  me: string | null;
  can: (p: string) => boolean;
  busy: boolean;
  onApprove: () => void;
  onComplete: () => void;
  onEnd: (kind: 'fail' | 'cancel') => void;
}) {
  const own = refund.requestedById === me;
  return (
    <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
      {refund.status === 'Requested' && can('refunds.approve') && (
        <Button
          size="small"
          variant="contained"
          disabled={busy || own}
          title={
            own
              ? 'Someone else must approve a refund you asked for.'
              : undefined
          }
          onClick={onApprove}
        >
          Approve
        </Button>
      )}
      {refund.status === 'Approved' && can('refunds.execute') && (
        <>
          <Button
            size="small"
            variant="contained"
            disabled={busy}
            onClick={onComplete}
          >
            Paid out
          </Button>
          <Button size="small" disabled={busy} onClick={() => onEnd('fail')}>
            Failed
          </Button>
        </>
      )}
      {(refund.status === 'Requested' || refund.status === 'Approved') &&
        can('refunds.approve') && (
          <Button
            size="small"
            color="error"
            disabled={busy}
            onClick={() => onEnd('cancel')}
          >
            Cancel
          </Button>
        )}
    </Stack>
  );
}

function ShipmentDialog({
  order,
  after,
  onClose,
  onSave,
}: {
  order: Order;
  after: OrderAfterSale;
  onClose: () => void;
  onSave: (input: {
    carrier: string | null;
    trackingReference: string | null;
    notes: string | null;
    lines: { orderLineId: string; quantity: number }[];
  }) => Promise<void>;
}) {
  const pending = new Map<string, number>();
  after.shipments
    .filter((s) => s.status === 'Pending')
    .forEach((s) =>
      s.lines.forEach((l) =>
        pending.set(
          l.orderLineId,
          (pending.get(l.orderLineId) ?? 0) + l.quantity,
        ),
      ),
    );
  const open = order.lines
    .map((l) => ({ l, left: l.qtyToIssue - (pending.get(l.id) ?? 0) }))
    .filter((x) => x.left > 0);
  const [qty, setQty] = useState<Record<string, string>>(
    Object.fromEntries(open.map(({ l, left }) => [l.id, String(left)])),
  );
  const [carrier, setCarrier] = useState(carriers[0]);
  const [tracking, setTracking] = useState('');
  const [notes, setNotes] = useState('');
  const lines = open
    .map(({ l }) => ({
      orderLineId: l.id,
      quantity: parseNumber(qty[l.id] ?? '') ?? 0,
    }))
    .filter((l) => l.quantity > 0);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>New shipment</DialogTitle>
      <DialogContent>
        <p>Stock goes down when you dispatch the shipment, not now.</p>
        <Stack spacing={2}>
          <Table size="small" aria-label="Quantities to ship">
            <TableBody>
              {open.map(({ l, left }) => (
                <TableRow key={l.id}>
                  <TableCell>
                    <span className="mono">{l.sku}</span> {l.description}
                  </TableCell>
                  <TableCell align="right" className="muted">
                    {formatQty(left)} left
                  </TableCell>
                  <TableCell align="right">
                    <TextField
                      size="small"
                      value={qty[l.id] ?? ''}
                      onChange={(e) =>
                        setQty({ ...qty, [l.id]: e.target.value })
                      }
                      slotProps={{
                        htmlInput: {
                          inputMode: 'decimal',
                          'aria-label': `Ship quantity of ${l.sku}`,
                          style: { textAlign: 'right', width: 70 },
                        },
                      }}
                    />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <TextField
            select
            label="Carrier"
            value={carrier}
            slotProps={select}
            onChange={(e) => setCarrier(e.target.value)}
          >
            {carriers.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </TextField>
          <TextField
            label="Tracking reference (optional)"
            value={tracking}
            onChange={(e) => setTracking(e.target.value)}
          />
          <TextField
            label="Notes (optional)"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={lines.length === 0}
          onClick={() =>
            void onSave({
              carrier,
              trackingReference: tracking || null,
              notes: notes || null,
              lines,
            })
          }
        >
          Create shipment
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function DispatchDialog({
  shipment,
  onClose,
  onSave,
}: {
  shipment: Shipment;
  onClose: () => void;
  onSave: (input: {
    carrier: string | null;
    trackingReference: string | null;
    idempotencyKey: string;
  }) => Promise<void>;
}) {
  const [carrier, setCarrier] = useState(shipment.carrier ?? carriers[0]);
  const [tracking, setTracking] = useState(shipment.trackingReference ?? '');
  const [key] = useState(() => crypto.randomUUID());
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Dispatch {shipment.shipmentNumber}</DialogTitle>
      <DialogContent>
        <Alert severity="info" sx={{ mb: 2 }}>
          The goods leave the shop now and stock goes down. Marking it delivered
          later does not change stock.
        </Alert>
        <Stack spacing={2}>
          <TextField
            label="Carrier"
            value={carrier}
            onChange={(e) => setCarrier(e.target.value)}
          />
          <TextField
            label="Tracking reference"
            value={tracking}
            onChange={(e) => setTracking(e.target.value)}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Back</Button>
        <Button
          variant="contained"
          onClick={() =>
            void onSave({
              carrier: carrier || null,
              trackingReference: tracking || null,
              idempotencyKey: key,
            })
          }
        >
          Dispatch
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function ReturnDialog({
  after,
  onClose,
  onSave,
}: {
  after: OrderAfterSale;
  onClose: () => void;
  onSave: (input: {
    reason: string;
    lines: {
      orderLineId: string;
      quantity: number;
      disposition: ReturnDisposition;
    }[];
    idempotencyKey: string;
  }) => Promise<void>;
}) {
  const open = after.returnable.filter((l) => l.returnable > 0);
  const [qty, setQty] = useState<Record<string, string>>({});
  const [disposition, setDisposition] = useState<
    Record<string, ReturnDisposition>
  >({});
  const [reason, setReason] = useState('');
  const [key] = useState(() => crypto.randomUUID());
  const lines = open
    .map((l) => ({
      orderLineId: l.orderLineId,
      quantity: parseNumber(qty[l.orderLineId] ?? '') ?? 0,
      disposition: disposition[l.orderLineId] ?? 'Restock',
    }))
    .filter((l) => l.quantity > 0);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>Return goods</DialogTitle>
      <DialogContent>
        <p>
          A credit note for the returned goods is issued automatically at the
          prices charged. It lowers what the customer owes; paying money back is
          a separate refund.
        </p>
        <Table size="small" aria-label="Lines to return">
          <TableHead>
            <TableRow>
              <TableCell>Item</TableCell>
              <TableCell align="right">Issued</TableCell>
              <TableCell align="right">Can return</TableCell>
              <TableCell align="right">Qty</TableCell>
              <TableCell>Condition</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {open.map((l) => (
              <TableRow key={l.orderLineId}>
                <TableCell>
                  <span className="mono">{l.sku}</span> {l.description}
                </TableCell>
                <TableCell align="right">{formatQty(l.issued)}</TableCell>
                <TableCell align="right">{formatQty(l.returnable)}</TableCell>
                <TableCell align="right">
                  <TextField
                    size="small"
                    value={qty[l.orderLineId] ?? ''}
                    onChange={(e) =>
                      setQty({ ...qty, [l.orderLineId]: e.target.value })
                    }
                    slotProps={{
                      htmlInput: {
                        inputMode: 'decimal',
                        'aria-label': `Return quantity of ${l.sku}`,
                        style: { textAlign: 'right', width: 70 },
                      },
                    }}
                  />
                </TableCell>
                <TableCell>
                  <TextField
                    select
                    size="small"
                    value={disposition[l.orderLineId] ?? 'Restock'}
                    slotProps={{
                      ...select,
                      htmlInput: { 'aria-label': `Condition of ${l.sku}` },
                    }}
                    onChange={(e) =>
                      setDisposition({
                        ...disposition,
                        [l.orderLineId]: e.target.value as ReturnDisposition,
                      })
                    }
                  >
                    <option value="Restock">Back on the shelf</option>
                    <option value="WriteOff">Damaged / write off</option>
                  </TextField>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <TextField
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          fullWidth
          required
          sx={{ mt: 2 }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={lines.length === 0 || !reason.trim()}
          onClick={() => void onSave({ reason, lines, idempotencyKey: key })}
        >
          Record return
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function CreditDialog({
  order,
  after,
  onClose,
  onSave,
}: {
  order: Order;
  after: OrderAfterSale;
  onClose: () => void;
  onSave: (input: {
    reason: string;
    lines: { orderLineId: string; amount: number }[];
    deliveryAmount: number;
    idempotencyKey: string;
  }) => Promise<void>;
}) {
  const [amounts, setAmounts] = useState<Record<string, string>>({});
  const [delivery, setDelivery] = useState('');
  const [reason, setReason] = useState('');
  const [key] = useState(() => crypto.randomUUID());
  const deliveryCredited = after.creditNotes
    .flatMap((c) => c.lines)
    .filter((l) => l.orderLineId === null)
    .reduce((s, l) => s + l.amount, 0);
  const lines = after.returnable
    .map((l) => ({
      orderLineId: l.orderLineId,
      amount: parseNumber(amounts[l.orderLineId] ?? '') ?? 0,
    }))
    .filter((l) => l.amount > 0);
  const deliveryAmount = parseNumber(delivery) ?? 0;
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Credit note</DialogTitle>
      <DialogContent>
        <p>
          For a price adjustment or goodwill when no goods come back. For
          returned goods use Return goods instead.
        </p>
        <Table size="small" aria-label="Amounts to credit">
          <TableBody>
            {after.returnable.map((l) => (
              <TableRow key={l.orderLineId}>
                <TableCell>
                  <span className="mono">{l.sku}</span> {l.description}
                </TableCell>
                <TableCell align="right" className="muted">
                  up to {formatMoney(l.lineTotal - l.credited)}
                </TableCell>
                <TableCell align="right">
                  <TextField
                    size="small"
                    value={amounts[l.orderLineId] ?? ''}
                    onChange={(e) =>
                      setAmounts({
                        ...amounts,
                        [l.orderLineId]: e.target.value,
                      })
                    }
                    slotProps={{
                      htmlInput: {
                        inputMode: 'decimal',
                        'aria-label': `Credit for ${l.sku}`,
                        style: { textAlign: 'right', width: 90 },
                      },
                    }}
                  />
                </TableCell>
              </TableRow>
            ))}
            {order.deliveryCharge > 0 && (
              <TableRow>
                <TableCell>Delivery charge</TableCell>
                <TableCell align="right" className="muted">
                  up to {formatMoney(order.deliveryCharge - deliveryCredited)}
                </TableCell>
                <TableCell align="right">
                  <TextField
                    size="small"
                    value={delivery}
                    onChange={(e) => setDelivery(e.target.value)}
                    slotProps={{
                      htmlInput: {
                        inputMode: 'decimal',
                        'aria-label': 'Delivery credit',
                        style: { textAlign: 'right', width: 90 },
                      },
                    }}
                  />
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
        <TextField
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          fullWidth
          required
          sx={{ mt: 2 }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={
            (lines.length === 0 && deliveryAmount <= 0) || !reason.trim()
          }
          onClick={() =>
            void onSave({ reason, lines, deliveryAmount, idempotencyKey: key })
          }
        >
          Issue credit note
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function RefundDialog({
  after,
  onClose,
  onSave,
}: {
  after: OrderAfterSale;
  onClose: () => void;
  onSave: (input: {
    amount: number;
    method: PaymentMethod;
    reason: string;
    creditNoteId: string | null;
    forCancellation: boolean;
    idempotencyKey: string;
  }) => Promise<void>;
}) {
  const b = after.balance;
  const cancelOnly = b.refundable <= 0 && b.refundableOnCancel > 0;
  const [forCancellation, setForCancellation] = useState(cancelOnly);
  const limit = forCancellation ? b.refundableOnCancel : b.refundable;
  const [amount, setAmount] = useState(limit.toFixed(2));
  const [method, setMethod] = useState<PaymentMethod>('Cash');
  const [reason, setReason] = useState('');
  const [creditNoteId, setCreditNoteId] = useState('');
  const [key] = useState(() => crypto.randomUUID());
  const value = parseNumber(amount);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Request a refund</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Alert severity="info">
            An admin other than you must approve it, then record when the money
            is paid back.
          </Alert>
          {b.refundableOnCancel > 0 && b.refundable > 0 && (
            <TextField
              select
              label="Why"
              value={forCancellation ? 'cancel' : 'owed'}
              slotProps={select}
              onChange={(e) => {
                const c = e.target.value === 'cancel';
                setForCancellation(c);
                setAmount((c ? b.refundableOnCancel : b.refundable).toFixed(2));
              }}
            >
              <option value="owed">Customer is owed money</option>
              <option value="cancel">Cancelling the order</option>
            </TextField>
          )}
          {forCancellation && (
            <Alert severity="warning">
              After the refund is paid out, cancel the invoice to release the
              reserved stock.
            </Alert>
          )}
          <TextField
            label="Amount $"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            helperText={`At most ${formatMoney(limit)}`}
            slotProps={money}
          />
          <TextField
            select
            label="Pay back by"
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
          {after.creditNotes.length > 0 && (
            <TextField
              select
              label="For credit note"
              value={creditNoteId}
              slotProps={select}
              onChange={(e) => setCreditNoteId(e.target.value)}
            >
              <option value="">None</option>
              {after.creditNotes.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.creditNoteNumber} ({formatMoney(c.total)})
                </option>
              ))}
            </TextField>
          )}
          <TextField
            label="Reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            required
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!value || value <= 0 || value > limit || !reason.trim()}
          onClick={() =>
            void onSave({
              amount: value ?? 0,
              method,
              reason,
              creditNoteId: creditNoteId || null,
              forCancellation,
              idempotencyKey: key,
            })
          }
        >
          Request {value ? formatMoney(value) : ''}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export function CompleteRefundDialog({
  refund,
  onClose,
  onSave,
}: {
  refund: Refund;
  onClose: () => void;
  onSave: (input: {
    method: PaymentMethod;
    evidenceReference: string;
    idempotencyKey: string;
  }) => Promise<void>;
}) {
  const [method, setMethod] = useState<PaymentMethod>(refund.method);
  const [evidence, setEvidence] = useState('');
  const [key] = useState(() => crypto.randomUUID());
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>
        Paid out {refund.refundNumber} ({formatMoney(refund.amount)})
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            select
            label="Paid back by"
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
            label="Evidence"
            value={evidence}
            onChange={(e) => setEvidence(e.target.value)}
            helperText="Bank reference, EFTPOS refund receipt or till note"
            required
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Back</Button>
        <Button
          variant="contained"
          disabled={!evidence.trim()}
          onClick={() =>
            void onSave({
              method,
              evidenceReference: evidence,
              idempotencyKey: key,
            })
          }
        >
          Confirm paid out
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export function TextDialog({
  title,
  label,
  action,
  onClose,
  onSave,
}: {
  title: string;
  label: string;
  action: string;
  onClose: () => void;
  onSave: (value: string) => Promise<void>;
}) {
  const [value, setValue] = useState('');
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <TextField
          label={label}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          fullWidth
          required
          sx={{ mt: 1 }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Back</Button>
        <Button
          color="error"
          variant="contained"
          disabled={!value.trim()}
          onClick={() => void onSave(value)}
        >
          {action}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
