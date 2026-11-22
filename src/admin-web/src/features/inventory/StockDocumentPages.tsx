import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import axios from 'axios';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
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
import { useConfirm } from '../catalog/useConfirm';
import {
  formatQty,
  inventoryApi,
  parseNumber,
  type AdjustmentReason,
  type DocumentKind,
  type ImportResult,
  type StockDocument,
} from './api';
import { DocumentPagination, ItemPicker, StatusChip } from './components';

const copy = {
  Adjustment: {
    title: 'Adjustments',
    one: 'Adjustment',
    path: '/inventory/adjustments',
    intro:
      'Correct stock for damage, loss, items found or other reasons. Use + to add and − to remove.',
  },
  Opening: {
    title: 'Opening stock',
    one: 'Opening stock',
    path: '/inventory/opening',
    intro:
      'Load the stock you already have. Each item can have opening stock once; after that use adjustments.',
  },
} as const;

const adjustmentReasons: AdjustmentReason[] = [
  'Damage',
  'Lost',
  'Found',
  'Correction',
  'Other',
];

export function StockDocumentListPage({ kind }: { kind: DocumentKind }) {
  const [pages, setPages] = useState({ Adjustment: 1, Opening: 1 });
  const page = pages[kind];
  const setPage = (next: number) =>
    setPages((current) => ({ ...current, [kind]: next }));
  const text = copy[kind];
  const list = useQuery({
    queryKey: ['stock-documents', kind, page],
    queryFn: () => inventoryApi.documents(kind, { page, pageSize: 25 }),
  });
  return (
    <>
      <Stack
        direction="row"
        sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}
      >
        <Typography variant="h4" component="h1">
          {text.title}
        </Typography>
        <Button variant="contained" component={Link} to={`${text.path}/new`}>
          New {text.one.toLowerCase()}
        </Button>
      </Stack>
      <p>{text.intro}</p>
      {kind === 'Opening' && <OpeningImport />}
      <ProblemAlert problem={list.isError ? describeError(list.error) : null} />
      <TableContainer component={Paper} elevation={0}>
        <Table size="small" aria-label={text.title}>
          <TableHead>
            <TableRow>
              <TableCell>Number</TableCell>
              <TableCell>Reason</TableCell>
              <TableCell>Status</TableCell>
              <TableCell align="right">Lines</TableCell>
              <TableCell align="right">Net quantity</TableCell>
              <TableCell>Posted</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((d) => (
              <TableRow key={d.id} hover>
                <TableCell>
                  <Link to={`${text.path}/${d.id}`}>{d.number ?? 'Draft'}</Link>
                </TableCell>
                <TableCell>{d.reason}</TableCell>
                <TableCell>
                  <StatusChip status={d.status} />
                </TableCell>
                <TableCell align="right">{d.lineCount}</TableCell>
                <TableCell align="right">{formatQty(d.netUnits)}</TableCell>
                <TableCell>
                  {d.postedAt ? formatShopDate(d.postedAt) : '—'}
                </TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={6}>Nothing here yet.</TableCell>
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

function OpeningImport() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);
  const download = async () => {
    setBusy(true);
    setProblem(null);
    try {
      const url = URL.createObjectURL(
        await inventoryApi.downloadOpeningTemplate(),
      );
      const link = document.createElement('a');
      link.href = url;
      link.download = 'opening-stock.xlsx';
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (error) {
      // Axios returns API errors as blobs for download requests.
      if (axios.isAxiosError(error) && error.response?.data instanceof Blob) {
        try {
          error.response.data = JSON.parse(await error.response.data.text());
        } catch {
          // Keep the original error if the response is not JSON.
        }
      }
      setProblem(describeError(error));
    } finally {
      setBusy(false);
    }
  };
  const run = async (dryRun: boolean) => {
    if (!file) return;
    setBusy(true);
    setProblem(null);
    try {
      const r = await inventoryApi.importOpening(file, dryRun);
      setResult(r);
      if (r.draftId) {
        void queryClient.invalidateQueries({
          queryKey: ['stock-documents', 'Opening'],
        });
        navigate(`/inventory/opening/${r.draftId}`);
      }
    } catch (error) {
      setResult(null);
      setProblem(describeError(error));
    } finally {
      setBusy(false);
    }
  };
  return (
    <Paper elevation={0} className="permission-panel">
      <Typography variant="h6" component="h2">
        Import from a spreadsheet
      </Typography>
      <p className="muted">
        Download the Excel workbook with all current items. Enter the opening
        quantity and unit cost for items without stock history. Blank and zero
        quantities are skipped in Excel. Import up to 2,000 filled rows at a
        time, then review and post the draft to update stock.
      </p>
      <Stack
        direction="row"
        sx={{ alignItems: 'center', flexWrap: 'wrap', gap: 2 }}
      >
        <Button
          variant="outlined"
          disabled={busy}
          onClick={() => void download()}
        >
          Download Excel template
        </Button>
        <Button variant="outlined" component="label" disabled={busy}>
          Choose Excel or CSV file
          <input
            hidden
            type="file"
            accept=".xlsx,.csv"
            disabled={busy}
            onChange={(e) => {
              setFile(e.target.files?.[0] ?? null);
              setResult(null);
              setProblem(null);
              e.target.value = '';
            }}
          />
        </Button>
        <span>{file?.name ?? 'No file chosen'}</span>
        <Button disabled={busy || !file} onClick={() => void run(true)}>
          Check file
        </Button>
        <Button
          variant="contained"
          disabled={
            busy || !file || !result || result.invalid > 0 || result.valid === 0
          }
          onClick={() => void run(false)}
        >
          Import as draft
        </Button>
      </Stack>
      <ProblemAlert problem={problem} />
      {result && (
        <>
          <Alert
            severity={
              result.invalid > 0 || result.valid === 0 ? 'warning' : 'success'
            }
            sx={{ mt: 2 }}
          >
            {result.valid} row{result.valid === 1 ? '' : 's'} ready,{' '}
            {result.invalid} with problems.
            {result.invalid > 0 && ' Fix the file and check it again.'}
            {result.invalid === 0 &&
              result.valid === 0 &&
              ' Enter an opening quantity and unit cost for at least one item.'}
          </Alert>
          {result.invalid > 0 && (
            <ul>
              {result.rows
                .filter((r) => r.error)
                .slice(0, 50)
                .map((r) => (
                  <li key={r.line}>
                    Line {r.line} <span className="mono">{r.sku}</span>:{' '}
                    {r.error}
                  </li>
                ))}
            </ul>
          )}
        </>
      )}
    </Paper>
  );
}

type Line = {
  key: string;
  itemId: string;
  sku: string;
  name: string;
  qty: string;
  unitCost: string;
  sellingPrice: string;
};

export function StockDocumentEditPage({ kind }: { kind: DocumentKind }) {
  const text = copy[kind];
  const { id = 'new' } = useParams();
  const isNew = id === 'new';
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { confirm, dialog } = useConfirm();
  const existing = useQuery({
    queryKey: ['stock-document', kind, id],
    queryFn: () => inventoryApi.document(kind, id),
    enabled: !isNew,
  });
  const [loaded, setLoaded] = useState<string | null>(isNew ? 'new' : null);
  const [reason, setReason] = useState<AdjustmentReason>(
    kind === 'Opening' ? 'Opening' : 'Damage',
  );
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<Line[]>([]);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);

  const doc = existing.data;
  if (doc && loaded !== doc.id + doc.version) {
    setLoaded(doc.id + doc.version);
    setReason(doc.reason);
    setNotes(doc.notes ?? '');
    setLines(
      doc.lines.map((l) => ({
        key: l.id,
        itemId: l.itemId,
        sku: l.sku,
        name: l.itemName,
        qty: String(l.qtyChange),
        unitCost: l.unitCost === null ? '' : String(l.unitCost),
        sellingPrice: l.sellingPrice === null ? '' : String(l.sellingPrice),
      })),
    );
  }
  if (!isNew && !doc)
    return existing.isError ? (
      <ProblemAlert problem={describeError(existing.error)} />
    ) : (
      <p role="status">Loading…</p>
    );
  const editable = (isNew || doc?.status === 'Draft') && !doc?.stocktakeId;

  const input = () => ({
    reason,
    notes: notes || null,
    lines: lines.map((l) => ({
      itemId: l.itemId,
      qtyChange: parseNumber(l.qty) ?? 0,
      unitCost: parseNumber(l.unitCost),
      sellingPrice: parseNumber(l.sellingPrice),
    })),
    version: doc?.version,
  });

  const run = async (action: () => Promise<StockDocument>) => {
    setBusy(true);
    setProblem(null);
    try {
      const result = await action();
      queryClient.setQueryData(['stock-document', kind, result.id], result);
      void queryClient.invalidateQueries({
        queryKey: ['stock-documents', kind],
      });
      if (isNew) navigate(`${text.path}/${result.id}`, { replace: true });
    } catch (error) {
      setProblem(describeError(error));
    } finally {
      setBusy(false);
    }
  };
  const save = (post: boolean) =>
    run(async () => {
      const saved = isNew
        ? await inventoryApi.createDocument(kind, input())
        : await inventoryApi.updateDocument(kind, id, input());
      return post ? inventoryApi.postDocument(kind, saved.id) : saved;
    });
  const update = (key: string, patch: Partial<Line>) =>
    setLines(lines.map((l) => (l.key === key ? { ...l, ...patch } : l)));

  return (
    <>
      <Button component={Link} to={text.path} size="small">
        ← {text.title}
      </Button>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', mb: 1 }}>
        <Typography variant="h4" component="h1">
          {doc?.number ??
            (isNew
              ? `New ${text.one.toLowerCase()}`
              : `Draft ${text.one.toLowerCase()}`)}
        </Typography>
        {doc && <StatusChip status={doc.status} />}
      </Stack>
      {doc?.postedAt && (
        <p className="muted">Posted {formatShopDate(doc.postedAt)}</p>
      )}
      {doc?.stocktakeId && (
        <Alert severity="info">
          Created by a stocktake. It cannot be edited.
        </Alert>
      )}
      <ProblemAlert problem={problem} />
      <Paper elevation={0} className="permission-panel">
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
          {kind === 'Adjustment' && (
            <TextField
              select
              label="Reason"
              value={reason}
              disabled={!editable}
              onChange={(e) => setReason(e.target.value as AdjustmentReason)}
              slotProps={{ select: { native: true } }}
              sx={{ minWidth: 180 }}
            >
              {(doc?.reason === 'Count'
                ? ['Count' as AdjustmentReason]
                : adjustmentReasons
              ).map((r) => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
            </TextField>
          )}
          <TextField
            label="Notes"
            value={notes}
            disabled={!editable}
            onChange={(e) => setNotes(e.target.value)}
            fullWidth
          />
        </Stack>
      </Paper>
      <Paper elevation={0} className="permission-panel">
        {editable && (
          <Stack direction="row" sx={{ mb: 2 }}>
            <ItemPicker
              label="Add item (SKU or name)"
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
                        qty: kind === 'Opening' ? '' : '-1',
                        unitCost: '',
                        sellingPrice:
                          item.sellingPrice === null
                            ? ''
                            : String(item.sellingPrice),
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
                <TableCell align="right">
                  {kind === 'Opening' ? 'Quantity' : 'Change (+/−)'}
                </TableCell>
                <TableCell align="right">
                  Unit cost{kind === 'Adjustment' && ' (added stock)'}
                </TableCell>
                {kind === 'Opening' && (
                  <TableCell align="right">Selling price</TableCell>
                )}
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
                        value={l.qty}
                        onChange={(e) => update(l.key, { qty: e.target.value })}
                        error={!!fieldError(problem, `lines[${i}].qtyChange`)}
                        helperText={fieldError(
                          problem,
                          `lines[${i}].qtyChange`,
                        )}
                        slotProps={{
                          htmlInput: {
                            inputMode: 'decimal',
                            'aria-label': `Quantity of ${l.sku}`,
                            style: { textAlign: 'right', width: 80 },
                          },
                        }}
                      />
                    ) : (
                      formatQty(Number(l.qty))
                    )}
                  </TableCell>
                  <TableCell align="right">
                    {editable &&
                    (kind === 'Opening' || (parseNumber(l.qty) ?? 0) > 0) ? (
                      <TextField
                        size="small"
                        value={l.unitCost}
                        onChange={(e) =>
                          update(l.key, { unitCost: e.target.value })
                        }
                        placeholder={
                          kind === 'Opening' ? 'required' : 'average'
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
                    ) : l.unitCost ? (
                      formatMoney(Number(l.unitCost))
                    ) : (
                      <span className="muted">average</span>
                    )}
                  </TableCell>
                  {kind === 'Opening' && (
                    <TableCell align="right">
                      {editable ? (
                        <TextField
                          size="small"
                          value={l.sellingPrice}
                          onChange={(e) =>
                            update(l.key, { sellingPrice: e.target.value })
                          }
                          placeholder="optional"
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
                        '—'
                      )}
                    </TableCell>
                  )}
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
                  <TableCell colSpan={5}>
                    No lines yet. Add an item above.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>
      <Stack direction="row" spacing={2}>
        {editable && (
          <>
            <Button
              variant="outlined"
              disabled={busy}
              onClick={() => void save(false)}
            >
              Save draft
            </Button>
            <Button
              variant="contained"
              disabled={busy || lines.length === 0}
              onClick={() =>
                confirm({
                  title: `Post this ${text.one.toLowerCase()}?`,
                  text: 'Stock changes now and the document can no longer be edited.',
                  action: () => void save(true),
                  confirmLabel: 'Post',
                  destructive: false,
                })
              }
            >
              Post
            </Button>
          </>
        )}
        {doc?.status === 'Draft' && !doc.stocktakeId && (
          <Button
            color="error"
            disabled={busy}
            onClick={() =>
              void run(() => inventoryApi.cancelDocument(kind, id))
            }
          >
            Cancel draft
          </Button>
        )}
      </Stack>
      {dialog}
    </>
  );
}
