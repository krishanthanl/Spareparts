import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import Alert from '@mui/material/Alert';
import Autocomplete from '@mui/material/Autocomplete';
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
import { describeError, fieldError, type ApiProblem } from '../../api/problems';
import { formatMoney, formatShopDate } from '../../lib/shop';
import { ProblemAlert } from '../catalog/ProblemAlert';
import { useAuth } from '../auth/useAuth';
import {
  formatQty,
  inventoryApi,
  parseNumber,
  type Grn,
  type DocumentStatus,
} from './api';
import { DocumentPagination, ItemPicker, StatusChip } from './components';

export function GrnListPage() {
  const [page, setPage] = useState(1);
  const [q, setQ] = useState('');
  const [status, setStatus] = useState<DocumentStatus | ''>('');
  const list = useQuery({
    queryKey: ['grns', q, status, page],
    queryFn: () =>
      inventoryApi.grns({ q, status: status || undefined, page, pageSize: 25 }),
  });
  return (
    <>
      <Stack
        direction="row"
        sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}
      >
        <Typography variant="h4" component="h1">
          Goods received
        </Typography>
        <Button variant="contained" component={Link} to="/inventory/grn/new">
          Receive stock
        </Button>
      </Stack>
      <p>
        Each posted goods received note adds stock and records the cost and
        selling price on the day it is posted.
      </p>
      <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
        <TextField
          size="small"
          label="Search supplier or number"
          value={q}
          onChange={(e) => {
            setQ(e.target.value);
            setPage(1);
          }}
        />
        <TextField
          size="small"
          select
          label="Status"
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as DocumentStatus | '');
            setPage(1);
          }}
          slotProps={{ select: { native: true } }}
          sx={{ minWidth: 140 }}
        >
          <option value="">All</option>
          {['Draft', 'Posted', 'Reversed', 'Cancelled'].map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </TextField>
      </Stack>
      <ProblemAlert problem={list.isError ? describeError(list.error) : null} />
      <TableContainer component={Paper} elevation={0}>
        <Table size="small" aria-label="Goods received notes">
          <TableHead>
            <TableRow>
              <TableCell>Number</TableCell>
              <TableCell>Supplier</TableCell>
              <TableCell>Status</TableCell>
              <TableCell align="right">Lines</TableCell>
              <TableCell align="right">Total cost</TableCell>
              <TableCell>Posted</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((g) => (
              <TableRow key={g.id} hover>
                <TableCell>
                  <Link to={`/inventory/grn/${g.id}`}>
                    {g.grnNumber ?? 'Draft'}
                  </Link>
                </TableCell>
                <TableCell>
                  {g.supplierName}
                  {g.supplierReference && (
                    <span className="muted"> · {g.supplierReference}</span>
                  )}
                </TableCell>
                <TableCell>
                  <StatusChip status={g.status} />
                </TableCell>
                <TableCell align="right">{g.lineCount}</TableCell>
                <TableCell align="right">{formatMoney(g.total)}</TableCell>
                <TableCell>
                  {g.postedAt ? formatShopDate(g.postedAt) : '—'}
                </TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={6}>No goods received notes yet.</TableCell>
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

type Line = {
  key: string;
  itemId: string;
  sku: string;
  name: string;
  quantity: string;
  unitCost: string;
  sellingPrice: string;
  currentPrice: number | null;
};

const toLines = (grn: Grn): Line[] =>
  grn.lines.map((l) => ({
    key: l.id,
    itemId: l.itemId,
    sku: l.sku,
    name: l.itemName,
    quantity: String(l.quantity),
    unitCost: String(l.unitCost),
    sellingPrice: l.sellingPrice === null ? '' : String(l.sellingPrice),
    currentPrice: l.currentPrice,
  }));

export function GrnEditPage() {
  const { id = 'new' } = useParams();
  const isNew = id === 'new';
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { can } = useAuth();
  const existing = useQuery({
    queryKey: ['grn', id],
    queryFn: () => inventoryApi.grn(id),
    enabled: !isNew,
  });
  const [loaded, setLoaded] = useState<string | null>(isNew ? 'new' : null);
  const [supplier, setSupplier] = useState('');
  const [reference, setReference] = useState('');
  const [supplierDate, setSupplierDate] = useState('');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<Line[]>([]);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmPost, setConfirmPost] = useState(false);
  const [reversing, setReversing] = useState(false);
  const [reason, setReason] = useState('');
  const suppliers = useQuery({
    queryKey: ['suppliers', supplier],
    queryFn: () => inventoryApi.suppliers(supplier),
  });

  const grn = existing.data;
  if (grn && loaded !== grn.id + grn.version) {
    setLoaded(grn.id + grn.version);
    setSupplier(grn.supplierName);
    setReference(grn.supplierReference ?? '');
    setSupplierDate(grn.supplierDocumentDate ?? '');
    setNotes(grn.notes ?? '');
    setLines(toLines(grn));
  }
  if (!isNew && !grn)
    return existing.isError ? (
      <ProblemAlert problem={describeError(existing.error)} />
    ) : (
      <p role="status">Loading…</p>
    );
  const editable = isNew || grn?.status === 'Draft';
  const total = lines.reduce(
    (sum, l) =>
      sum + (parseNumber(l.quantity) ?? 0) * (parseNumber(l.unitCost) ?? 0),
    0,
  );

  const input = () => ({
    supplierName: supplier,
    supplierReference: reference || null,
    supplierDocumentDate: supplierDate || null,
    notes: notes || null,
    lines: lines.map((l) => ({
      itemId: l.itemId,
      quantity: parseNumber(l.quantity) ?? 0,
      unitCost: parseNumber(l.unitCost) ?? -1,
      sellingPrice: parseNumber(l.sellingPrice),
    })),
    version: grn?.version,
  });

  const run = async (action: () => Promise<Grn>, after?: (g: Grn) => void) => {
    setBusy(true);
    setProblem(null);
    try {
      const result = await action();
      queryClient.setQueryData(['grn', result.id], result);
      void queryClient.invalidateQueries({ queryKey: ['grns'] });
      after?.(result);
    } catch (error) {
      setProblem(describeError(error));
    } finally {
      setBusy(false);
    }
  };

  const save = (then?: (g: Grn) => Promise<Grn>) =>
    run(
      async () => {
        const saved = isNew
          ? await inventoryApi.createGrn(input())
          : await inventoryApi.updateGrn(id, input());
        return then ? then(saved) : saved;
      },
      (g) => isNew && navigate(`/inventory/grn/${g.id}`, { replace: true }),
    );

  const update = (key: string, patch: Partial<Line>) =>
    setLines(lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));

  return (
    <>
      <Button component={Link} to="/inventory/grn" size="small">
        ← Goods received
      </Button>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', mb: 1 }}>
        <Typography variant="h4" component="h1">
          {grn?.grnNumber ?? (isNew ? 'Receive stock' : 'Draft goods received')}
        </Typography>
        {grn && <StatusChip status={grn.status} />}
      </Stack>
      {grn?.postedAt && (
        <p className="muted">
          Posted {formatShopDate(grn.postedAt)}
          {grn.reversedAt &&
            ` · Reversed ${formatShopDate(grn.reversedAt)}: ${grn.reversalReason}`}
        </p>
      )}
      <ProblemAlert problem={problem} />
      <Paper elevation={0} className="permission-panel">
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
          <Autocomplete
            freeSolo
            options={suppliers.data ?? []}
            inputValue={supplier}
            onInputChange={(_, v) => setSupplier(v)}
            disabled={!editable}
            sx={{ flex: 2 }}
            renderInput={(params) => (
              <TextField
                {...params}
                label="Supplier"
                required
                error={!!fieldError(problem, 'supplierName')}
                helperText={fieldError(problem, 'supplierName')}
              />
            )}
          />
          <TextField
            label="Supplier invoice no."
            value={reference}
            onChange={(e) => setReference(e.target.value)}
            disabled={!editable}
            sx={{ flex: 1 }}
          />
          <TextField
            label="Supplier's date"
            type="date"
            value={supplierDate}
            onChange={(e) => setSupplierDate(e.target.value)}
            disabled={!editable}
            helperText="For reference only. Stock is added when you post."
            slotProps={{ inputLabel: { shrink: true } }}
            sx={{ flex: 1 }}
          />
        </Stack>
        <TextField
          label="Notes"
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          disabled={!editable}
          fullWidth
          sx={{ mt: 2 }}
        />
      </Paper>

      <Paper elevation={0} className="permission-panel">
        {editable && (
          <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
            <ItemPicker
              label="Add item (SKU or name)"
              autoFocus={isNew}
              onPick={(item) =>
                lines.some((l) => l.itemId === item.id)
                  ? undefined
                  : setLines([
                      ...lines,
                      {
                        key: crypto.randomUUID(),
                        itemId: item.id,
                        sku: item.sku,
                        name: item.name,
                        quantity: '1',
                        unitCost: '',
                        sellingPrice:
                          item.sellingPrice === null
                            ? ''
                            : String(item.sellingPrice),
                        currentPrice: item.sellingPrice,
                      },
                    ])
              }
            />
          </Stack>
        )}
        <TableContainer>
          <Table size="small" aria-label="Lines">
            <TableHead>
              <TableRow>
                <TableCell>Item</TableCell>
                <TableCell align="right">Quantity</TableCell>
                <TableCell align="right">Unit cost</TableCell>
                <TableCell align="right">Selling price</TableCell>
                <TableCell align="right">Line total</TableCell>
                {editable && <TableCell />}
              </TableRow>
            </TableHead>
            <TableBody>
              {lines.map((l, i) => (
                <TableRow key={l.key}>
                  <TableCell>
                    <span className="mono">{l.sku}</span> {l.name}
                  </TableCell>
                  <TableCell align="right">
                    {editable ? (
                      <TextField
                        size="small"
                        value={l.quantity}
                        onChange={(e) =>
                          update(l.key, { quantity: e.target.value })
                        }
                        error={!!fieldError(problem, `lines[${i}].quantity`)}
                        helperText={fieldError(problem, `lines[${i}].quantity`)}
                        slotProps={{
                          htmlInput: {
                            inputMode: 'decimal',
                            'aria-label': `Quantity of ${l.sku}`,
                            style: { textAlign: 'right', width: 80 },
                          },
                        }}
                      />
                    ) : (
                      formatQty(Number(l.quantity))
                    )}
                  </TableCell>
                  <TableCell align="right">
                    {editable ? (
                      <TextField
                        size="small"
                        value={l.unitCost}
                        onChange={(e) =>
                          update(l.key, { unitCost: e.target.value })
                        }
                        error={!!fieldError(problem, `lines[${i}].unitCost`)}
                        helperText={fieldError(problem, `lines[${i}].unitCost`)}
                        slotProps={{
                          htmlInput: {
                            inputMode: 'decimal',
                            'aria-label': `Unit cost of ${l.sku}`,
                            style: { textAlign: 'right', width: 90 },
                          },
                        }}
                      />
                    ) : (
                      formatMoney(Number(l.unitCost))
                    )}
                  </TableCell>
                  <TableCell align="right">
                    {editable ? (
                      <TextField
                        size="small"
                        value={l.sellingPrice}
                        onChange={(e) =>
                          update(l.key, { sellingPrice: e.target.value })
                        }
                        placeholder={
                          l.currentPrice === null ? 'required' : 'keep'
                        }
                        error={
                          !!fieldError(problem, `lines[${i}].sellingPrice`)
                        }
                        helperText={fieldError(
                          problem,
                          `lines[${i}].sellingPrice`,
                        )}
                        slotProps={{
                          htmlInput: {
                            inputMode: 'decimal',
                            'aria-label': `Selling price of ${l.sku}`,
                            style: { textAlign: 'right', width: 90 },
                          },
                        }}
                      />
                    ) : l.sellingPrice ? (
                      formatMoney(Number(l.sellingPrice))
                    ) : (
                      <span className="muted">unchanged</span>
                    )}
                  </TableCell>
                  <TableCell align="right">
                    {formatMoney(
                      (parseNumber(l.quantity) ?? 0) *
                        (parseNumber(l.unitCost) ?? 0),
                    )}
                  </TableCell>
                  {editable && (
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
                  )}
                </TableRow>
              ))}
              {lines.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>
                    No lines yet. Add an item above.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
        <Typography sx={{ mt: 2, textAlign: 'right' }}>
          <strong>Total cost {formatMoney(total)}</strong>
        </Typography>
      </Paper>

      <Stack direction="row" spacing={2}>
        {editable && (
          <>
            <Button
              variant="outlined"
              disabled={busy}
              onClick={() => void save()}
            >
              Save draft
            </Button>
            <Button
              variant="contained"
              disabled={busy || lines.length === 0 || !supplier.trim()}
              onClick={() => setConfirmPost(true)}
            >
              Post
            </Button>
          </>
        )}
        {grn?.status === 'Draft' && (
          <Button
            color="error"
            disabled={busy}
            onClick={() => void run(() => inventoryApi.cancelGrn(id))}
          >
            Cancel draft
          </Button>
        )}
        {grn?.status === 'Posted' && can('inventory.reverse') && (
          <Button
            color="error"
            variant="outlined"
            onClick={() => setReversing(true)}
          >
            Reverse
          </Button>
        )}
      </Stack>

      <Dialog open={confirmPost} onClose={() => setConfirmPost(false)}>
        <DialogTitle>Post this goods received note?</DialogTitle>
        <DialogContent>
          <p>
            Stock goes up now and cannot be edited afterwards. Mistakes are
            fixed by reversing (Admin) or with an adjustment.
          </p>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmPost(false)}>Not yet</Button>
          <Button
            variant="contained"
            onClick={() => {
              setConfirmPost(false);
              void save((saved) => inventoryApi.postGrn(saved.id));
            }}
          >
            Post
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog
        open={reversing}
        onClose={() => setReversing(false)}
        fullWidth
        maxWidth="xs"
      >
        <DialogTitle>Reverse {grn?.grnNumber}?</DialogTitle>
        <DialogContent>
          <Alert severity="warning" sx={{ mb: 2 }}>
            Takes the received stock back out now. It is refused if some of it
            has already been used.
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
          <Button onClick={() => setReversing(false)}>Cancel</Button>
          <Button
            color="error"
            variant="contained"
            disabled={!reason.trim()}
            onClick={() => {
              setReversing(false);
              void run(() => inventoryApi.reverseGrn(id, reason));
            }}
          >
            Reverse
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
