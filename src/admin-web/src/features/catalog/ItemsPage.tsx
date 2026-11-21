import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import FormControlLabel from '@mui/material/FormControlLabel';
import MenuItem from '@mui/material/MenuItem';
import Pagination from '@mui/material/Pagination';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { catalogApi, describeError, mediaUrl, type ApiProblem } from './api';
import { ItemEditor } from './ItemEditor';
import { Link } from 'react-router-dom';
import { useCan } from '../auth/useAuth';
import { formatMoney } from '../../lib/shop';
import { formatQty } from '../inventory/api';
import { ProblemAlert } from './ProblemAlert';
import { useConfirm } from './useConfirm';

const PAGE_SIZE = 25;

export function ItemsPage() {
  const can = useCan();
  const queryClient = useQueryClient();
  const [q, setQ] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [sort, setSort] = useState('name');
  const [page, setPage] = useState(1);
  const [showDeleted, setShowDeleted] = useState(false);
  const [editing, setEditing] = useState<string | 'new' | null>(null);
  const [actionProblem, setActionProblem] = useState<ApiProblem | null>(null);
  const { confirm, dialog } = useConfirm();

  const categories = useQuery({
    queryKey: ['categories', 'all'],
    queryFn: () => catalogApi.categories({ pageSize: 100 }),
  });
  const list = useQuery({
    queryKey: ['items', q, categoryId, sort, page, showDeleted],
    queryFn: () =>
      catalogApi.items({
        q,
        categoryId,
        sort,
        page,
        pageSize: PAGE_SIZE,
        includeDeleted: showDeleted,
      }),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['items'] });
  const action = useMutation({
    mutationFn: (run: () => Promise<unknown>) => run(),
    onSuccess: () => {
      setActionProblem(null);
      void refresh();
    },
    onError: (error) => setActionProblem(describeError(error)),
  });
  const listProblem = list.isError ? describeError(list.error) : null;
  const pages = list.data
    ? Math.max(1, Math.ceil(list.data.totalCount / PAGE_SIZE))
    : 1;

  return (
    <>
      <Stack
        direction="row"

        sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 2 }}
      >
        <Typography variant="h4" component="h1">
          Items
        </Typography>
        <Button variant="contained" onClick={() => setEditing('new')}>
          New item
        </Button>
      </Stack>
      <Stack direction={{ xs: 'column', md: 'row' }} sx={{ gap: 2, mb: 2 }}>
        <TextField
          label="Search name, SKU or brand"
          size="small"
          value={q}
          onChange={(e) => {
            setQ(e.target.value);
            setPage(1);
          }}
        />
        <TextField
          select
          label="Category"
          size="small"
          sx={{ minWidth: 200 }}
          value={categoryId}
          onChange={(e) => {
            setCategoryId(e.target.value);
            setPage(1);
          }}
        >
          <MenuItem value="">All categories</MenuItem>
          {categories.data?.items.map((c) => (
            <MenuItem key={c.id} value={c.id}>
              {c.name}
            </MenuItem>
          ))}
        </TextField>
        <TextField
          select
          label="Sort"
          size="small"
          sx={{ minWidth: 160 }}
          value={sort}
          onChange={(e) => setSort(e.target.value)}
        >
          <MenuItem value="name">Name A–Z</MenuItem>
          <MenuItem value="-name">Name Z–A</MenuItem>
          <MenuItem value="sku">SKU</MenuItem>
          <MenuItem value="-updatedAt">Recently changed</MenuItem>
        </TextField>
        <FormControlLabel
          control={
            <Switch
              checked={showDeleted}
              onChange={(e) => setShowDeleted(e.target.checked)}
            />
          }
          label="Show deleted"
        />
      </Stack>
      <ProblemAlert problem={listProblem ?? actionProblem} />
      {list.isPending && <p role="status">Loading items…</p>}
      {list.data && list.data.items.length === 0 && <p>No items match.</p>}
      {list.data && list.data.items.length > 0 && (
        <>
          <Box sx={{ overflowX: 'auto' }}>
            <Table size="small" aria-label="Items">
              <TableHead>
                <TableRow>
                  <TableCell>Image</TableCell>
                  <TableCell>SKU</TableCell>
                  <TableCell>Name</TableCell>
                  <TableCell>Category</TableCell>
                  <TableCell align="right">On hand</TableCell>
                  <TableCell align="right">Price</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell align="right">Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {list.data.items.map((item) => (
                  <TableRow
                    key={item.id}
                    sx={{ opacity: item.isDeleted ? 0.55 : 1 }}
                  >
                    <TableCell>
                      {item.thumbnailUrl ? (
                        <img
                          className="thumb"
                          src={mediaUrl(item.thumbnailUrl)}
                          alt=""
                        />
                      ) : (
                        <span
                          className="thumb thumb-empty"
                          aria-hidden="true"
                        />
                      )}
                    </TableCell>
                    <TableCell className="mono">{item.sku}</TableCell>
                    <TableCell>
                      {item.name}
                      {item.partBrand && (
                        <Typography variant="body2" color="text.secondary">
                          {item.partBrand}
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell>{item.categoryName}</TableCell>
                    <TableCell align="right">
                      {can('inventory.reports') ? (
                        <Link to={`/inventory/items/${item.id}`}>
                          {formatQty(item.onHand)}
                        </Link>
                      ) : (
                        formatQty(item.onHand)
                      )}
                    </TableCell>
                    <TableCell align="right">
                      {item.sellingPrice === null ? (
                        <span className="muted">No price</span>
                      ) : (
                        formatMoney(item.sellingPrice)
                      )}
                    </TableCell>
                    <TableCell>
                      {item.isDeleted ? (
                        <Chip size="small" label="Deleted" />
                      ) : item.isActive ? (
                        <Chip size="small" color="success" label="Active" />
                      ) : (
                        <Chip size="small" label="Inactive" />
                      )}
                    </TableCell>
                    <TableCell align="right">
                      {item.isDeleted ? (
                        <Button
                          onClick={() =>
                            action.mutate(() => catalogApi.restoreItem(item.id))
                          }
                        >
                          Restore
                        </Button>
                      ) : (
                        <>
                          <Button onClick={() => setEditing(item.id)}>
                            Edit
                          </Button>
                          <Button
                            color="error"
                            onClick={() =>
                              confirm({
                                title: `Delete ${item.sku}?`,
                                text: 'The item is hidden and can be restored later. Its SKU becomes free to reuse.',
                                action: () =>
                                  action.mutate(() =>
                                    catalogApi.deleteItem(item.id),
                                  ),
                              })
                            }
                          >
                            Delete
                          </Button>
                        </>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Box>
          <Stack
            direction="row"

            sx={{
              justifyContent: 'space-between',
              alignItems: 'center',
              mt: 2,
            }}
          >
            <Typography variant="body2" color="text.secondary">
              {list.data.totalCount} item(s)
            </Typography>
            <Pagination
              count={pages}
              page={page}
              onChange={(_, p) => setPage(p)}
            />
          </Stack>
        </>
      )}
      {editing && (
        <ItemEditor
          itemId={editing === 'new' ? null : editing}
          categories={categories.data?.items ?? []}
          onClose={() => {
            setEditing(null);
            void refresh();
          }}
        />
      )}
      {dialog}
    </>
  );
}
