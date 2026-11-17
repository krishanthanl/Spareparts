import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
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
import {
  formatQty,
  inventoryApi,
  parseNumber,
  shopToday,
  type MovementType,
} from './api';

const movementLabel: Record<MovementType, string> = {
  Opening: 'Opening stock',
  Grn: 'Received',
  GrnReversal: 'Receipt reversed',
  Adjustment: 'Adjustment',
  Issue: 'Sold / issued',
  Return: 'Returned',
  Reversal: 'Reversal',
};

const documentLink = (refType: string, refId: string, type: MovementType) =>
  refType === 'Grn'
    ? `/inventory/grn/${refId}`
    : refType === 'StockDocument'
      ? `/inventory/${type === 'Opening' ? 'opening' : 'adjustments'}/${refId}`
      : null;

export function StockReportPage() {
  const today = shopToday();
  const [date, setDate] = useState(today);
  const [q, setQ] = useState('');
  const [sort, setSort] = useState('name');
  const [onlyInStock, setOnlyInStock] = useState(false);
  const [page, setPage] = useState(1);
  const isToday = date === today;
  const report = useQuery({
    queryKey: ['stock', date, q, sort, onlyInStock, page],
    queryFn: () =>
      inventoryApi.stock({
        date: isToday ? undefined : date,
        q,
        sort,
        onlyInStock,
        page,
      }),
  });
  const data = report.data;
  const pages = data
    ? Math.max(1, Math.ceil(data.items.totalCount / data.items.pageSize))
    : 1;
  return (
    <>
      <Typography variant="h4" component="h1" sx={{ mb: 1 }}>
        Stock report
      </Typography>
      <p>
        Stock is worked out from every posted movement. Pick a date to see stock
        at the end of that day (New Zealand time).
      </p>
      <Stack
        direction={{ xs: 'column', md: 'row' }}
        spacing={2}
        sx={{ mb: 2, alignItems: { md: 'center' } }}
      >
        <TextField
          size="small"
          type="date"
          label="Stock at end of"
          value={date}
          onChange={(e) => {
            setDate(e.target.value || today);
            setPage(1);
          }}
          slotProps={{
            inputLabel: { shrink: true },
            htmlInput: { max: today },
          }}
        />
        <TextField
          size="small"
          label="Search SKU or name"
          value={q}
          onChange={(e) => {
            setQ(e.target.value);
            setPage(1);
          }}
        />
        <TextField
          size="small"
          select
          label="Sort"
          value={sort}
          onChange={(e) => setSort(e.target.value)}
          slotProps={{ select: { native: true } }}
        >
          <option value="name">Name</option>
          <option value="sku">SKU</option>
          <option value="-onHand">Most stock</option>
          <option value="onHand">Least stock</option>
          <option value="-value">Highest value</option>
        </TextField>
        <FormControlLabel
          control={
            <Switch
              checked={onlyInStock}
              onChange={(e) => setOnlyInStock(e.target.checked)}
            />
          }
          label="Only items with stock"
        />
      </Stack>
      <ProblemAlert
        problem={report.isError ? describeError(report.error) : null}
      />
      {data && (
        <p>
          <strong>{isToday ? 'Now' : `End of ${date}`}:</strong>{' '}
          {formatQty(data.totalUnits)} units, valued at{' '}
          {formatMoney(data.totalValue)} (average cost).
        </p>
      )}
      <TableContainer component={Paper} elevation={0}>
        <Table size="small" aria-label="Stock">
          <TableHead>
            <TableRow>
              <TableCell>SKU</TableCell>
              <TableCell>Name</TableCell>
              <TableCell>Category</TableCell>
              <TableCell align="right">On hand</TableCell>
              <TableCell align="right">Average cost</TableCell>
              <TableCell align="right">Value</TableCell>
              <TableCell align="right">Selling price</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data?.items.items.map((r) => (
              <TableRow key={r.itemId} hover>
                <TableCell className="mono">
                  <Link to={`/inventory/items/${r.itemId}`}>{r.sku}</Link>
                </TableCell>
                <TableCell>{r.name}</TableCell>
                <TableCell>{r.categoryName}</TableCell>
                <TableCell align="right">{formatQty(r.onHand)}</TableCell>
                <TableCell align="right">
                  {formatMoney(r.averageCost)}
                </TableCell>
                <TableCell align="right">{formatMoney(r.stockValue)}</TableCell>
                <TableCell align="right">
                  {r.sellingPrice === null ? (
                    <span className="muted">No price</span>
                  ) : (
                    formatMoney(r.sellingPrice)
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <Stack direction="row" spacing={2} sx={{ mt: 2, alignItems: 'center' }}>
        <Button disabled={page <= 1} onClick={() => setPage(page - 1)}>
          Previous
        </Button>
        <span>
          Page {page} of {pages}
        </span>
        <Button disabled={page >= pages} onClick={() => setPage(page + 1)}>
          Next
        </Button>
      </Stack>
    </>
  );
}

export function LowStockPage() {
  const low = useQuery({
    queryKey: ['low-stock'],
    queryFn: inventoryApi.lowStock,
  });
  return (
    <>
      <Typography variant="h4" component="h1" sx={{ mb: 1 }}>
        Low stock
      </Typography>
      <p>
        Items at or below their reorder level. Items without a reorder level are
        never listed.
      </p>
      <ProblemAlert problem={low.isError ? describeError(low.error) : null} />
      <TableContainer component={Paper} elevation={0}>
        <Table size="small" aria-label="Low stock">
          <TableHead>
            <TableRow>
              <TableCell>SKU</TableCell>
              <TableCell>Name</TableCell>
              <TableCell align="right">On hand</TableCell>
              <TableCell align="right">Reorder level</TableCell>
              <TableCell align="right">Reorder quantity</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {low.data?.map((r) => (
              <TableRow key={r.itemId} hover>
                <TableCell className="mono">
                  <Link to={`/inventory/items/${r.itemId}`}>{r.sku}</Link>
                </TableCell>
                <TableCell>{r.name}</TableCell>
                <TableCell align="right">{formatQty(r.onHand)}</TableCell>
                <TableCell align="right">{r.reorderLevel}</TableCell>
                <TableCell align="right">{r.reorderQuantity ?? '—'}</TableCell>
              </TableRow>
            ))}
            {low.data?.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>Nothing is low right now.</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
    </>
  );
}

export function StockCardPage() {
  const { id = '' } = useParams();
  const { can } = useAuth();
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [pricing, setPricing] = useState(false);
  const card = useQuery({
    queryKey: ['stock-card', id, from, to],
    queryFn: () =>
      inventoryApi.stockCard(id, from || undefined, to || undefined),
  });
  const prices = useQuery({
    queryKey: ['price-history', id],
    queryFn: () => inventoryApi.priceHistory(id),
  });
  if (card.isError) return <ProblemAlert problem={describeError(card.error)} />;
  if (!card.data) return <p role="status">Loading…</p>;
  const c = card.data;
  return (
    <>
      <Button component={Link} to="/inventory/stock" size="small">
        ← Stock report
      </Button>
      <Typography variant="h4" component="h1">
        <span className="mono">{c.sku}</span> {c.name}
      </Typography>
      <p>
        <strong>{formatQty(c.onHand)}</strong> on hand · average cost{' '}
        {formatMoney(c.averageCost)} · selling price{' '}
        {c.sellingPrice === null ? 'not set' : formatMoney(c.sellingPrice)}
        {can('inventory.price') && (
          <Button size="small" sx={{ ml: 1 }} onClick={() => setPricing(true)}>
            Change price
          </Button>
        )}
      </p>
      <Stack direction="row" spacing={2} sx={{ mb: 2 }}>
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
      </Stack>
      <TableContainer component={Paper} elevation={0}>
        <Table size="small" aria-label="Stock movements">
          <TableHead>
            <TableRow>
              <TableCell>When</TableCell>
              <TableCell>Movement</TableCell>
              <TableCell>Document</TableCell>
              <TableCell align="right">Change</TableCell>
              <TableCell align="right">Balance</TableCell>
              <TableCell align="right">Unit cost</TableCell>
              <TableCell align="right">Average after</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {from && (
              <TableRow>
                <TableCell colSpan={4}>Balance before {from}</TableCell>
                <TableCell align="right">
                  {formatQty(c.openingBalance)}
                </TableCell>
                <TableCell colSpan={2} />
              </TableRow>
            )}
            {c.movements.map((m) => {
              const href = documentLink(m.refType, m.refId, m.movementType);
              return (
                <TableRow key={m.sequence}>
                  <TableCell>{formatShopDate(m.occurredAt)}</TableCell>
                  <TableCell>{movementLabel[m.movementType]}</TableCell>
                  <TableCell>
                    {href ? (
                      <Link to={href}>{m.documentNumber ?? 'View'}</Link>
                    ) : (
                      (m.documentNumber ?? '—')
                    )}
                  </TableCell>
                  <TableCell align="right">
                    {(m.qtyChange > 0 ? '+' : '') + formatQty(m.qtyChange)}
                  </TableCell>
                  <TableCell align="right">{formatQty(m.balance)}</TableCell>
                  <TableCell align="right">{formatMoney(m.unitCost)}</TableCell>
                  <TableCell align="right">
                    {formatMoney(m.averageCostAfter)}
                  </TableCell>
                </TableRow>
              );
            })}
            {c.movements.length === 0 && (
              <TableRow>
                <TableCell colSpan={7}>
                  No movements{from || to ? ' in this period' : ' yet'}.
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
      <Typography variant="h6" component="h2" sx={{ mt: 3 }}>
        Price history
      </Typography>
      <TableContainer component={Paper} elevation={0}>
        <Table size="small" aria-label="Price history">
          <TableHead>
            <TableRow>
              <TableCell>From</TableCell>
              <TableCell align="right">Selling price</TableCell>
              <TableCell align="right">Average cost then</TableCell>
              <TableCell>Source</TableCell>
              <TableCell>Reason</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {prices.data?.map((p) => (
              <TableRow key={p.sequence}>
                <TableCell>{formatShopDate(p.effectiveFrom)}</TableCell>
                <TableCell align="right">
                  {formatMoney(p.sellingPrice)}
                </TableCell>
                <TableCell align="right">
                  {p.costPrice === null ? '—' : formatMoney(p.costPrice)}
                </TableCell>
                <TableCell>
                  {p.sourceType === 'PriceChange'
                    ? 'Price change'
                    : p.sourceType === 'Grn'
                      ? 'Goods received'
                      : 'Opening stock'}
                </TableCell>
                <TableCell>{p.reason ?? ''}</TableCell>
              </TableRow>
            ))}
            {prices.data?.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>No price yet.</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
      {pricing && (
        <PriceDialog
          itemId={c.itemId}
          sku={c.sku}
          current={c.sellingPrice}
          onClose={() => setPricing(false)}
        />
      )}
    </>
  );
}

export function PriceDialog({
  itemId,
  sku,
  current,
  onClose,
}: {
  itemId: string;
  sku: string;
  current: number | null;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [price, setPrice] = useState(current === null ? '' : String(current));
  const [reason, setReason] = useState('');
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const save = async () => {
    try {
      await inventoryApi.changePrice(itemId, parseNumber(price) ?? 0, reason);
      for (const key of ['stock-card', 'price-history', 'items', 'stock'])
        void queryClient.invalidateQueries({ queryKey: [key] });
      onClose();
    } catch (error) {
      setProblem(describeError(error));
    }
  };
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Change price of {sku}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <ProblemAlert problem={problem} />
          <p className="muted">
            Applies from now. Earlier invoices and receipts keep their prices.
          </p>
          <TextField
            label="New selling price (NZD)"
            value={price}
            onChange={(e) => setPrice(e.target.value)}
            error={!!fieldError(problem, 'sellingPrice')}
            helperText={fieldError(problem, 'sellingPrice')}
            slotProps={{ htmlInput: { inputMode: 'decimal' } }}
          />
          <TextField
            label="Reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="e.g. supplier price rise"
            error={!!fieldError(problem, 'reason')}
            helperText={fieldError(problem, 'reason')}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={parseNumber(price) === null || !reason.trim()}
          onClick={() => void save()}
        >
          Save price
        </Button>
      </DialogActions>
    </Dialog>
  );
}
