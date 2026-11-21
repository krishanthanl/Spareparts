import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
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
import { parseNumber, shopToday } from '../inventory/api';
import {
  blankOrder,
  methodLabel,
  paymentMethods,
  salesApi,
  type Customer,
  type CustomerInput,
  type Order,
  type PaymentMethod,
} from './api';
import { LineEditor, Totals } from './components';
import { buildInput, usePreview, type EditLine } from './draft';

const select = { select: { native: true } } as const;

/** Counter sale: items, payment method, done. Creates, confirms, issues and pays in one step. */
export function QuickSalePage() {
  const { can } = useAuth();
  const canDiscount = can('sales.discount');
  const [lines, setLines] = useState<EditLine[]>([]);
  const [customerName, setCustomerName] = useState('');
  const [method, setMethod] = useState<PaymentMethod>('Cash');
  const [tendered, setTendered] = useState('');
  const [reference, setReference] = useState('');
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<Order | null>(null);
  // A new key per sale; a double click submits the same sale once.
  const [key, setKey] = useState(() => crypto.randomUUID());
  const base = blankOrder('Counter');
  const header = {
    ...base,
    customerName: customerName || null,
    deliveryCharge: '',
    orderDiscount: '',
  };
  const input = buildInput(header, lines);
  const preview = usePreview(input, true);
  const total = preview.data?.total ?? 0;
  const cash = parseNumber(tendered);

  const submit = async () => {
    setBusy(true);
    setProblem(null);
    try {
      const sale = await salesApi.quickSale({
        order: input,
        method,
        amount: null,
        reference: reference || null,
        idempotencyKey: key,
      });
      setDone(sale);
    } catch (error) {
      setProblem(describeError(error));
    } finally {
      setBusy(false);
    }
  };

  const next = () => {
    setLines([]);
    setCustomerName('');
    setTendered('');
    setReference('');
    setDone(null);
    setKey(crypto.randomUUID());
  };

  return (
    <>
      <Typography variant="h4" component="h1">
        Counter sale
      </Typography>
      <p>
        For customers at the counter: the goods leave now and payment is taken
        in full.
      </p>
      <ProblemAlert
        problem={
          problem ?? (preview.isError ? describeError(preview.error) : null)
        }
      />
      <Paper elevation={0} className="permission-panel">
        <LineEditor
          lines={lines}
          setLines={setLines}
          preview={preview.data}
          problem={problem}
          canDiscount={canDiscount}
          autoFocus
        />
        {preview.data && (
          <Totals
            subtotal={preview.data.subtotal}
            discounts={preview.data.lineDiscountTotal}
            delivery={0}
            total={total}
            showDelivery={false}
          />
        )}
      </Paper>
      <Paper elevation={0} className="permission-panel">
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
          <TextField
            label="Customer name (optional)"
            value={customerName}
            onChange={(e) => setCustomerName(e.target.value)}
            sx={{ flex: 2 }}
          />
          <TextField
            select
            label="Payment"
            value={method}
            slotProps={select}
            onChange={(e) => setMethod(e.target.value as PaymentMethod)}
            sx={{ minWidth: 170 }}
          >
            {paymentMethods.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </TextField>
          {method === 'Cash' ? (
            <TextField
              label="Cash given $"
              value={tendered}
              onChange={(e) => setTendered(e.target.value)}
              helperText={
                cash !== null && cash >= total && total > 0
                  ? `Change ${formatMoney(cash - total)}`
                  : 'Optional, to work out change'
              }
              slotProps={{ htmlInput: { inputMode: 'decimal' } }}
              sx={{ maxWidth: 200 }}
            />
          ) : (
            <TextField
              label="Reference (optional)"
              value={reference}
              onChange={(e) => setReference(e.target.value)}
              sx={{ flex: 1 }}
            />
          )}
        </Stack>
      </Paper>
      <Button
        size="large"
        variant="contained"
        disabled={busy || lines.length === 0 || !preview.data}
        onClick={() => void submit()}
      >
        Take {formatMoney(total)}{' '}
        {method === 'Cash' ? 'cash' : methodLabel(method).toLowerCase()}
      </Button>

      <Dialog open={!!done} onClose={next}>
        <DialogTitle>Sale complete</DialogTitle>
        <DialogContent>
          <p>
            Invoice <strong>{done?.invoiceNumber}</strong> ·{' '}
            {done && formatMoney(done.total)} paid by{' '}
            {done && methodLabel(method)}.
          </p>
          {method === 'Cash' && cash !== null && done && cash > done.total && (
            <Alert severity="info">
              Change to give: {formatMoney(cash - done.total)}
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          {done && (
            <Button onClick={() => void salesApi.openPdf(done.id)}>
              Print invoice
            </Button>
          )}
          {done && (
            <Button component={Link} to={`/sales/invoices/${done.id}`}>
              Open
            </Button>
          )}
          <Button variant="contained" onClick={next} autoFocus>
            Next sale
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

const emptyCustomer: CustomerInput = {
  name: '',
  phone: null,
  email: null,
  company: null,
  billingAddress: null,
  deliveryAddress: null,
  notes: null,
  isActive: true,
};

export function CustomersPage() {
  const { can } = useAuth();
  const queryClient = useQueryClient();
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const [showInactive, setShowInactive] = useState(false);
  const [editing, setEditing] = useState<{
    id: string | null;
    input: CustomerInput;
  } | null>(null);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const list = useQuery({
    queryKey: ['customers', q, page, showInactive],
    queryFn: () =>
      salesApi.customers({ q, page, includeInactive: showInactive }),
  });
  const save = async () => {
    if (!editing) return;
    setProblem(null);
    try {
      if (editing.id) await salesApi.updateCustomer(editing.id, editing.input);
      else await salesApi.createCustomer(editing.input);
      void queryClient.invalidateQueries({ queryKey: ['customers'] });
      void queryClient.invalidateQueries({ queryKey: ['customer-search'] });
      setEditing(null);
    } catch (error) {
      setProblem(describeError(error));
    }
  };
  const edit = (c: Customer) => setEditing({ id: c.id, input: { ...c } });
  const field = (name: keyof CustomerInput, label: string, multiline = false) =>
    editing && (
      <TextField
        label={label}
        value={(editing.input[name] as string | null) ?? ''}
        onChange={(e) =>
          setEditing({
            ...editing,
            input: { ...editing.input, [name]: e.target.value },
          })
        }
        error={!!fieldError(problem, name)}
        helperText={fieldError(problem, name)}
        multiline={multiline}
        minRows={multiline ? 2 : undefined}
        fullWidth
        required={name === 'name'}
      />
    );
  return (
    <>
      <Stack
        direction="row"
        sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}
      >
        <Typography variant="h4" component="h1">
          Customers
        </Typography>
        {can('customers.manage') && (
          <Button
            variant="contained"
            onClick={() => {
              setProblem(null);
              setEditing({ id: null, input: emptyCustomer });
            }}
          >
            Add customer
          </Button>
        )}
      </Stack>
      <p>
        Phone and regular customers. Invoices keep a copy of the details at the
        time, so editing here never changes old invoices.
      </p>
      <Stack direction="row" spacing={2} sx={{ mb: 2, alignItems: 'center' }}>
        <TextField
          size="small"
          label="Search name, phone, email or company"
          value={q}
          sx={{ minWidth: 300 }}
          onChange={(e) => {
            setQ(e.target.value);
            setPage(1);
          }}
        />
        <FormControlLabel
          control={
            <Switch
              checked={showInactive}
              onChange={(e) => setShowInactive(e.target.checked)}
            />
          }
          label="Show inactive"
        />
      </Stack>
      <ProblemAlert problem={list.isError ? describeError(list.error) : null} />
      <TableContainer component={Paper} elevation={0}>
        <Table size="small" aria-label="Customers">
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Phone</TableCell>
              <TableCell>Email</TableCell>
              <TableCell>Company</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((c) => (
              <TableRow key={c.id} hover>
                <TableCell>
                  {c.name}
                  {!c.isActive && <span className="muted"> (inactive)</span>}
                </TableCell>
                <TableCell>{c.phone}</TableCell>
                <TableCell>{c.email}</TableCell>
                <TableCell>{c.company}</TableCell>
                <TableCell>
                  {can('customers.manage') && (
                    <Button
                      size="small"
                      onClick={() => {
                        setProblem(null);
                        edit(c);
                      }}
                    >
                      Edit
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>No customers found.</TableCell>
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
      <Dialog
        open={!!editing}
        onClose={() => setEditing(null)}
        fullWidth
        maxWidth="sm"
      >
        <DialogTitle>
          {editing?.id ? 'Edit customer' : 'Add customer'}
        </DialogTitle>
        <DialogContent>
          <ProblemAlert problem={problem} />
          <Stack spacing={2} sx={{ mt: 1 }}>
            {field('name', 'Name')}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              {field('phone', 'Phone')}
              {field('email', 'Email')}
            </Stack>
            {field('company', 'Company')}
            {field('billingAddress', 'Billing address', true)}
            {field('deliveryAddress', 'Delivery address', true)}
            {field('notes', 'Notes', true)}
            {editing?.id && (
              <FormControlLabel
                control={
                  <Switch
                    checked={editing.input.isActive}
                    onChange={(e) =>
                      setEditing({
                        ...editing,
                        input: { ...editing.input, isActive: e.target.checked },
                      })
                    }
                  />
                }
                label="Active"
              />
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setEditing(null)}>Cancel</Button>
          <Button
            variant="contained"
            disabled={!editing?.input.name.trim()}
            onClick={() => void save()}
          >
            Save
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

export function DailyTakingsPage() {
  const today = shopToday();
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const report = useQuery({
    queryKey: ['takings', from, to],
    queryFn: () => salesApi.takings(from, to),
    enabled: !!from && !!to,
  });
  const r = report.data;
  return (
    <>
      <Typography variant="h4" component="h1">
        Daily takings
      </Typography>
      <p>
        Money received per day in New Zealand time, by payment method, to
        reconcile the till and the bank.
      </p>
      <Stack
        direction="row"
        spacing={2}
        useFlexGap
        sx={{ mb: 2, flexWrap: 'wrap' }}
        className="no-print"
      >
        <TextField
          size="small"
          type="date"
          label="From"
          value={from}
          onChange={(e) => setFrom(e.target.value)}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          size="small"
          type="date"
          label="To"
          value={to}
          onChange={(e) => setTo(e.target.value)}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <Button variant="outlined" onClick={() => window.print()}>
          Print
        </Button>
        <Button
          variant="outlined"
          onClick={() =>
            void salesApi
              .takingsCsv(from, to)
              .catch((e) => setProblem(describeError(e)))
          }
        >
          Download CSV
        </Button>
      </Stack>
      <ProblemAlert
        problem={
          problem ?? (report.isError ? describeError(report.error) : null)
        }
      />
      {r && (
        <>
          <Paper elevation={0} className="permission-panel">
            <Table size="small" aria-label="Totals by method">
              <TableHead>
                <TableRow>
                  <TableCell>Method</TableCell>
                  <TableCell align="right">Received</TableCell>
                  <TableCell align="right">Voided</TableCell>
                  <TableCell align="right">Net</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {r.methods.map((m) => (
                  <TableRow key={m.method}>
                    <TableCell>{methodLabel(m.method)}</TableCell>
                    <TableCell align="right">
                      {formatMoney(m.received)}
                    </TableCell>
                    <TableCell align="right">
                      {m.voided ? '−' + formatMoney(m.voided) : '—'}
                    </TableCell>
                    <TableCell align="right">{formatMoney(m.net)}</TableCell>
                  </TableRow>
                ))}
                <TableRow>
                  <TableCell>
                    <strong>Total</strong>
                  </TableCell>
                  <TableCell align="right">
                    <strong>{formatMoney(r.received)}</strong>
                  </TableCell>
                  <TableCell align="right">
                    <strong>
                      {r.voided ? '−' + formatMoney(r.voided) : '—'}
                    </strong>
                  </TableCell>
                  <TableCell align="right">
                    <strong>{formatMoney(r.net)}</strong>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
            <Typography variant="body2" className="muted" sx={{ mt: 1 }}>
              Refunds paid out will appear here once refunds are added.
            </Typography>
          </Paper>
          {r.days
            .filter((d) => d.payments.length > 0 || r.days.length === 1)
            .map((d) => (
              <Paper key={d.date} elevation={0} className="permission-panel">
                <Typography variant="h6" component="h2">
                  {new Date(d.date + 'T12:00:00').toLocaleDateString('en-NZ', {
                    weekday: 'long',
                    day: 'numeric',
                    month: 'long',
                    year: 'numeric',
                  })}
                  {' — '}
                  {formatMoney(d.net)}
                </Typography>
                <Table size="small" aria-label={`Payments on ${d.date}`}>
                  <TableBody>
                    {d.payments.map((p) => (
                      <TableRow key={p.paymentId}>
                        <TableCell>{formatShopDate(p.receivedAt)}</TableCell>
                        <TableCell className="mono">
                          {p.receiptNumber}
                        </TableCell>
                        <TableCell>
                          <Link to={`/sales/invoices/${p.orderId}`}>
                            {p.invoiceNumber}
                          </Link>
                        </TableCell>
                        <TableCell>
                          {p.customerName ?? (
                            <span className="muted">Cash sale</span>
                          )}
                        </TableCell>
                        <TableCell>
                          {methodLabel(p.method)}
                          {p.isVoid && ' (void)'}
                        </TableCell>
                        <TableCell align="right">
                          {formatMoney(p.amount)}
                        </TableCell>
                        <TableCell className="muted">{p.receivedBy}</TableCell>
                      </TableRow>
                    ))}
                    {d.payments.length === 0 && (
                      <TableRow>
                        <TableCell>No payments.</TableCell>
                      </TableRow>
                    )}
                  </TableBody>
                </Table>
              </Paper>
            ))}
        </>
      )}
    </>
  );
}
