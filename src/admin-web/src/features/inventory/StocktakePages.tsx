import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
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
import { describeError, type ApiProblem } from '../../api/problems';
import { formatShopDate } from '../../lib/shop';
import { ProblemAlert } from '../catalog/ProblemAlert';
import { useConfirm } from '../catalog/useConfirm';
import { formatQty, inventoryApi, parseNumber, type Stocktake } from './api';
import { DocumentPagination, ItemPicker, StatusChip } from './components';

export function StocktakeListPage() {
  const [page, setPage] = useState(1);
  const list = useQuery({
    queryKey: ['stocktakes', page],
    queryFn: () => inventoryApi.stocktakes({ page, pageSize: 25 }),
  });
  return (
    <>
      <Stack
        direction="row"
        sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}
      >
        <Typography variant="h4" component="h1">
          Stocktake
        </Typography>
        <Button
          variant="contained"
          component={Link}
          to="/inventory/stocktakes/new"
        >
          Start a count
        </Button>
      </Stack>
      <p>
        Count what is on the shelf. When you post, the system works out the
        differences and records one adjustment.
      </p>
      <ProblemAlert problem={list.isError ? describeError(list.error) : null} />
      <TableContainer component={Paper} elevation={0}>
        <Table size="small" aria-label="Stocktakes">
          <TableHead>
            <TableRow>
              <TableCell>Number</TableCell>
              <TableCell>Status</TableCell>
              <TableCell align="right">Items</TableCell>
              <TableCell>Started</TableCell>
              <TableCell>Posted</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {list.data?.items.map((s) => (
              <TableRow key={s.id} hover>
                <TableCell>
                  <Link to={`/inventory/stocktakes/${s.id}`}>
                    {s.number ?? 'Draft'}
                  </Link>
                </TableCell>
                <TableCell>
                  <StatusChip status={s.status} />
                </TableCell>
                <TableCell align="right">{s.lineCount}</TableCell>
                <TableCell>{formatShopDate(s.startedAt)}</TableCell>
                <TableCell>
                  {s.postedAt ? formatShopDate(s.postedAt) : '—'}
                </TableCell>
              </TableRow>
            ))}
            {list.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>No counts yet.</TableCell>
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
  counted: string;
  onHand: number;
  moved: boolean;
};

export function StocktakeEditPage() {
  const { id = 'new' } = useParams();
  const isNew = id === 'new';
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { confirm, dialog } = useConfirm();
  const existing = useQuery({
    queryKey: ['stocktake', id],
    queryFn: () => inventoryApi.stocktake(id),
    enabled: !isNew,
  });
  const [loaded, setLoaded] = useState<string | null>(isNew ? 'new' : null);
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<Line[]>([]);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [busy, setBusy] = useState(false);

  const take = existing.data;
  if (take && loaded !== take.id + take.version) {
    setLoaded(take.id + take.version);
    setNotes(take.notes ?? '');
    setLines(
      take.lines.map((l) => ({
        key: l.id,
        itemId: l.itemId,
        sku: l.sku,
        name: l.itemName,
        counted: String(l.countedQty),
        onHand: l.systemQtyAtPost ?? l.onHandNow,
        moved: l.movedSinceStart,
      })),
    );
  }
  if (!isNew && !take)
    return existing.isError ? (
      <ProblemAlert problem={describeError(existing.error)} />
    ) : (
      <p role="status">Loading…</p>
    );
  const editable = isNew || take?.status === 'Draft';
  const input = () => ({
    notes: notes || null,
    lines: lines.map((l) => ({
      itemId: l.itemId,
      countedQty: parseNumber(l.counted) ?? -1,
    })),
  });

  const run = async (action: () => Promise<Stocktake>) => {
    setBusy(true);
    setProblem(null);
    try {
      const result = await action();
      queryClient.setQueryData(['stocktake', result.id], result);
      void queryClient.invalidateQueries({ queryKey: ['stocktakes'] });
      if (isNew)
        navigate(`/inventory/stocktakes/${result.id}`, { replace: true });
    } catch (error) {
      setProblem(describeError(error));
    } finally {
      setBusy(false);
    }
  };
  const save = (post: boolean) =>
    run(async () => {
      const saved = isNew
        ? await inventoryApi.createStocktake(input())
        : await inventoryApi.updateStocktake(id, {
            ...input(),
            version: take!.version,
          });
      return post ? inventoryApi.postStocktake(saved.id) : saved;
    });
  const changes = lines.filter(
    (l) =>
      parseNumber(l.counted) !== null && parseNumber(l.counted) !== l.onHand,
  );

  return (
    <>
      <Button component={Link} to="/inventory/stocktakes" size="small">
        ← Stocktake
      </Button>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', mb: 1 }}>
        <Typography variant="h4" component="h1">
          {take?.number ?? (isNew ? 'New count' : 'Count in progress')}
        </Typography>
        {take && <StatusChip status={take.status} />}
      </Stack>
      {take?.adjustmentId && (
        <p>
          Differences were posted as{' '}
          <Link to={`/inventory/adjustments/${take.adjustmentId}`}>
            an adjustment
          </Link>
          .
        </p>
      )}
      {editable && lines.some((l) => l.moved) && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          Stock of some items changed since this count started (marked below).
          Recount them before posting.
        </Alert>
      )}
      <ProblemAlert problem={problem} />
      <Paper elevation={0} className="permission-panel">
        <TextField
          label="Notes"
          value={notes}
          disabled={!editable}
          onChange={(e) => setNotes(e.target.value)}
          fullWidth
        />
      </Paper>
      <Paper elevation={0} className="permission-panel">
        {editable && (
          <Stack direction="row" sx={{ mb: 2 }}>
            <ItemPicker
              label="Add item to count"
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
                        counted: '',
                        onHand: item.onHand,
                        moved: false,
                      },
                    ])
              }
            />
          </Stack>
        )}
        <TableContainer>
          <Table size="small" aria-label="Counts">
            <TableHead>
              <TableRow>
                <TableCell>Item</TableCell>
                <TableCell align="right">
                  {take?.status === 'Posted' ? 'System had' : 'System has'}
                </TableCell>
                <TableCell align="right">Counted</TableCell>
                <TableCell align="right">Difference</TableCell>
                {editable && <TableCell />}
              </TableRow>
            </TableHead>
            <TableBody>
              {lines.map((l) => {
                const counted = parseNumber(l.counted);
                const diff = counted === null ? null : counted - l.onHand;
                return (
                  <TableRow key={l.key}>
                    <TableCell>
                      <span className="mono">{l.sku}</span> {l.name}
                      {editable && l.moved && (
                        <span className="muted"> · changed since start</span>
                      )}
                    </TableCell>
                    <TableCell align="right">{formatQty(l.onHand)}</TableCell>
                    <TableCell align="right">
                      {editable ? (
                        <TextField
                          size="small"
                          value={l.counted}
                          onChange={(e) =>
                            setLines(
                              lines.map((x) =>
                                x.key === l.key
                                  ? { ...x, counted: e.target.value }
                                  : x,
                              ),
                            )
                          }
                          slotProps={{
                            htmlInput: {
                              inputMode: 'decimal',
                              'aria-label': `Counted ${l.sku}`,
                              style: { textAlign: 'right', width: 80 },
                            },
                          }}
                        />
                      ) : (
                        formatQty(Number(l.counted))
                      )}
                    </TableCell>
                    <TableCell align="right">
                      {diff === null
                        ? '—'
                        : diff === 0
                          ? '0'
                          : (diff > 0 ? '+' : '') + formatQty(diff)}
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
                );
              })}
              {lines.length === 0 && (
                <TableRow>
                  <TableCell colSpan={5}>
                    Add the items you are counting.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>
      {editable && (
        <Stack direction="row" spacing={2}>
          <Button
            variant="outlined"
            disabled={busy}
            onClick={() => void save(false)}
          >
            Save count
          </Button>
          <Button
            variant="contained"
            disabled={
              busy ||
              lines.length === 0 ||
              lines.some((l) => parseNumber(l.counted) === null)
            }
            onClick={() =>
              confirm({
                title: 'Post this stocktake?',
                text: `${changes.length} item${changes.length === 1 ? '' : 's'} will be adjusted to the counted quantity. Differences are worked out again at the moment you post.`,
                action: () => void save(true),
                confirmLabel: 'Post',
                destructive: false,
              })
            }
          >
            Post
          </Button>
          {take?.status === 'Draft' && (
            <Button
              color="error"
              disabled={busy}
              onClick={() => void run(() => inventoryApi.cancelStocktake(id))}
            >
              Cancel count
            </Button>
          )}
        </Stack>
      )}
      {dialog}
    </>
  );
}
