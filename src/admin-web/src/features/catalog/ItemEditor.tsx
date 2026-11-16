import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import IconButton from '@mui/material/IconButton';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import {
  catalogApi,
  describeError,
  fieldError,
  mediaUrl,
  type ApiProblem,
  type Category,
  type Item,
  type ItemInput,
} from './api';
import { ImagePicker } from './CategoriesPage';
import { ProblemAlert } from './ProblemAlert';

type FitmentRow = {
  makeId: string;
  vehicleModelId: string;
  yearFrom: string;
  yearTo: string;
};

const toInput = (item: Item | null): ItemInput & { rows: FitmentRow[] } => ({
  sku: item?.sku ?? '',
  name: item?.name ?? '',
  description: item?.description ?? '',
  categoryId: item?.categoryId ?? '',
  partBrand: item?.partBrand ?? '',
  reorderLevel: item?.reorderLevel ?? null,
  reorderQuantity: item?.reorderQuantity ?? null,
  isActive: item?.isActive ?? true,
  fitments: [],
  version: item?.version,
  rows: [],
});

const optionalNumber = (value: string) =>
  value.trim() === '' ? null : Number(value);

export function ItemEditor({
  itemId,
  categories,
  onClose,
}: {
  itemId: string | null;
  categories: Category[];
  onClose: () => void;
}) {
  const existing = useQuery({
    queryKey: ['item', itemId],
    queryFn: () => catalogApi.item(itemId!),
    enabled: itemId !== null,
  });
  if (itemId && existing.isPending) return null;
  return (
    <ItemForm
      key={existing.data?.version ?? 'new'}
      item={existing.data ?? null}
      categories={categories}
      onClose={onClose}
    />
  );
}

function ItemForm({
  item: initial,
  categories,
  onClose,
}: {
  item: Item | null;
  categories: Category[];
  onClose: () => void;
}) {
  const [item, setItem] = useState(initial);
  const [form, setForm] = useState(() => toInput(initial));
  const [rows, setRows] = useState<FitmentRow[]>(
    () =>
      initial?.fitments.map((f) => ({
        makeId: '',
        vehicleModelId: f.vehicleModelId,
        yearFrom: f.yearFrom?.toString() ?? '',
        yearTo: f.yearTo?.toString() ?? '',
      })) ?? [],
  );
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const makes = useQuery({
    queryKey: ['vehicle-makes'],
    queryFn: catalogApi.makes,
  });
  const allModels =
    makes.data?.flatMap((m) =>
      m.models.map((model) => ({ ...model, make: m.name })),
    ) ?? [];

  const save = useMutation({
    mutationFn: () => {
      const input: ItemInput = {
        ...form,
        description: form.description || null,
        partBrand: form.partBrand || null,
        fitments: rows
          .filter((r) => r.vehicleModelId)
          .map((r) => ({
            vehicleModelId: r.vehicleModelId,
            yearFrom: optionalNumber(r.yearFrom),
            yearTo: optionalNumber(r.yearTo),
          })),
        version: item?.version,
      };
      return item
        ? catalogApi.updateItem(item.id, input)
        : catalogApi.createItem(input);
    },
    onSuccess: (saved) => {
      setProblem(null);
      if (item) onClose();
      else {
        // Stay open after creating so images can be added straight away.
        setItem(saved);
        setForm(toInput(saved));
      }
    },
    onError: (error) => setProblem(describeError(error)),
  });
  const imageAction = useMutation({
    mutationFn: (run: () => Promise<Item>) => run(),
    onSuccess: (updated) => {
      setProblem(null);
      setItem(updated);
      setForm((f) => ({ ...f, version: updated.version }));
    },
    onError: (error) => setProblem(describeError(error)),
  });

  const err = (field: string) => fieldError(problem, field);
  const set = <K extends keyof ItemInput>(key: K, value: ItemInput[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <form
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        <DialogTitle>{item ? `Edit ${item.sku}` : 'New item'}</DialogTitle>
        <DialogContent>
          <Stack sx={{ gap: 2, pt: 1 }}>
            <ProblemAlert problem={problem} />
            <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 2 }}>
              <TextField
                label="SKU"
                required
                value={form.sku}
                onChange={(e) => set('sku', e.target.value)}
                error={!!err('Sku')}
                helperText={err('Sku') ?? 'Saved in capitals, e.g. TP-001'}
                slotProps={{ htmlInput: { maxLength: 50 } }}
              />
              <TextField
                label="Name"
                required
                fullWidth
                value={form.name}
                onChange={(e) => set('name', e.target.value)}
                error={!!err('Name')}
                helperText={err('Name')}
                slotProps={{ htmlInput: { maxLength: 200 } }}
              />
            </Stack>
            <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 2 }}>
              <TextField
                select
                label="Category"
                required
                fullWidth
                value={form.categoryId}
                onChange={(e) => set('categoryId', e.target.value)}
                error={!!err('CategoryId')}
                helperText={err('CategoryId')}
              >
                {categories.map((c) => (
                  <MenuItem key={c.id} value={c.id}>
                    {c.name}
                  </MenuItem>
                ))}
              </TextField>
              <TextField
                label="Part brand"
                fullWidth
                value={form.partBrand ?? ''}
                onChange={(e) => set('partBrand', e.target.value)}
                helperText={err('PartBrand') ?? 'Who made the part, e.g. Bosch'}
                error={!!err('PartBrand')}
              />
            </Stack>
            <TextField
              label="Description"
              multiline
              minRows={3}
              value={form.description ?? ''}
              onChange={(e) => set('description', e.target.value)}
              error={!!err('Description')}
              helperText={err('Description')}
              slotProps={{ htmlInput: { maxLength: 4000 } }}
            />
            <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 2 }}>
              <TextField
                label="Reorder level (optional)"
                type="number"
                value={form.reorderLevel ?? ''}
                onChange={(e) =>
                  set('reorderLevel', optionalNumber(e.target.value))
                }
                error={!!err('ReorderLevel')}
                helperText={err('ReorderLevel')}
              />
              <TextField
                label="Reorder quantity (optional)"
                type="number"
                value={form.reorderQuantity ?? ''}
                onChange={(e) =>
                  set('reorderQuantity', optionalNumber(e.target.value))
                }
                error={!!err('ReorderQuantity')}
                helperText={err('ReorderQuantity')}
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={form.isActive}
                    onChange={(e) => set('isActive', e.target.checked)}
                  />
                }
                label="Active"
              />
            </Stack>

            <Typography variant="h6" component="h2">
              Fits vehicles
            </Typography>
            {err('Fitments') && (
              <Alert severity="error">{err('Fitments')}</Alert>
            )}
            {rows.map((row, index) => (
              <Stack
                key={index}
                direction={{ xs: 'column', sm: 'row' }}

                sx={{ gap: 1, alignItems: 'flex-start' }}
              >
                <TextField
                  select
                  label="Vehicle model"
                  size="small"
                  sx={{ minWidth: 240 }}
                  value={row.vehicleModelId}
                  onChange={(e) =>
                    setRows(
                      rows.map((r, i) =>
                        i === index
                          ? { ...r, vehicleModelId: e.target.value }
                          : r,
                      ),
                    )
                  }
                >
                  {allModels.map((m) => (
                    <MenuItem key={m.id} value={m.id}>
                      {m.make} {m.name}
                    </MenuItem>
                  ))}
                </TextField>
                <TextField
                  label="Year from"
                  size="small"
                  type="number"
                  value={row.yearFrom}
                  onChange={(e) =>
                    setRows(
                      rows.map((r, i) =>
                        i === index ? { ...r, yearFrom: e.target.value } : r,
                      ),
                    )
                  }
                  error={!!err(`Fitments[${index}].YearFrom`)}
                  helperText={err(`Fitments[${index}].YearFrom`)}
                />
                <TextField
                  label="Year to"
                  size="small"
                  type="number"
                  value={row.yearTo}
                  onChange={(e) =>
                    setRows(
                      rows.map((r, i) =>
                        i === index ? { ...r, yearTo: e.target.value } : r,
                      ),
                    )
                  }
                  error={!!err(`Fitments[${index}].YearTo`)}
                  helperText={err(`Fitments[${index}].YearTo`)}
                />
                <IconButton
                  aria-label={`Remove vehicle ${index + 1}`}
                  onClick={() => setRows(rows.filter((_, i) => i !== index))}
                >
                  ×
                </IconButton>
              </Stack>
            ))}
            <div>
              <Button
                onClick={() =>
                  setRows([
                    ...rows,
                    {
                      makeId: '',
                      vehicleModelId: '',
                      yearFrom: '',
                      yearTo: '',
                    },
                  ])
                }
                disabled={allModels.length === 0}
              >
                Add vehicle
              </Button>
              {makes.isSuccess && allModels.length === 0 && (
                <Typography
                  variant="body2"
                  color="text.secondary"
                  component="span"
                >
                  Add vehicle makes and models first.
                </Typography>
              )}
            </div>

            <Typography variant="h6" component="h2">
              Images
            </Typography>
            {!item ? (
              <Typography variant="body2" color="text.secondary">
                Save the item first, then add images.
              </Typography>
            ) : (
              <>
                <div className="image-grid">
                  {item.images.map((image) => (
                    <figure
                      key={image.id}
                      className={image.isPrimary ? 'primary' : undefined}
                    >
                      <img
                        src={mediaUrl(image.thumbnailUrl)}
                        alt={image.altText ?? ''}
                      />
                      <figcaption>
                        {image.isPrimary ? (
                          <strong>Main image</strong>
                        ) : (
                          <Button
                            size="small"
                            onClick={() =>
                              imageAction.mutate(() =>
                                catalogApi.setPrimaryImage(item.id, image.id),
                              )
                            }
                          >
                            Make main
                          </Button>
                        )}
                        <Button
                          size="small"
                          color="error"
                          onClick={() =>
                            imageAction.mutate(() =>
                              catalogApi.removeImage(item.id, image.id),
                            )
                          }
                        >
                          Remove
                        </Button>
                      </figcaption>
                    </figure>
                  ))}
                </div>
                <ImagePicker
                  label="Add image"
                  file={null}
                  onChange={(file, p) => {
                    if (p) setProblem(p);
                    if (file)
                      imageAction.mutate(() =>
                        catalogApi.addImage(item.id, file),
                      );
                  }}
                />
              </>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{item ? 'Close' : 'Cancel'}</Button>
          <Button type="submit" variant="contained" disabled={save.isPending}>
            {item ? 'Save' : 'Create item'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
