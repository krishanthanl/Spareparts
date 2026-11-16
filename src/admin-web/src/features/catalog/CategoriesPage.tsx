import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import {
  catalogApi,
  describeError,
  fieldError,
  mediaUrl,
  type ApiProblem,
  type Category,
} from './api';
import { ProblemAlert } from './ProblemAlert';
import { useConfirm } from './useConfirm';
import { ACCEPTED_IMAGES, checkImageFile } from './images';

type Draft = {
  id?: string;
  name: string;
  description: string;
  sortOrder: number;
  isActive: boolean;
  version?: number;
  imageUrl?: string | null;
};
const empty: Draft = {
  name: '',
  description: '',
  sortOrder: 0,
  isActive: true,
};

export function CategoriesPage() {
  const queryClient = useQueryClient();
  const [q, setQ] = useState('');
  const [showDeleted, setShowDeleted] = useState(false);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [formProblem, setFormProblem] = useState<ApiProblem | null>(null);
  const [actionProblem, setActionProblem] = useState<ApiProblem | null>(null);
  const { confirm, dialog } = useConfirm();

  const list = useQuery({
    queryKey: ['categories', q, showDeleted],
    queryFn: () =>
      catalogApi.categories({ q, includeDeleted: showDeleted, pageSize: 100 }),
  });
  const refresh = () =>
    queryClient.invalidateQueries({ queryKey: ['categories'] });

  const save = useMutation({
    mutationFn: async (value: Draft) => {
      const input = {
        name: value.name,
        description: value.description || null,
        sortOrder: value.sortOrder,
        isActive: value.isActive,
        version: value.version,
      };
      const saved = value.id
        ? await catalogApi.updateCategory(value.id, input)
        : await catalogApi.createCategory(input);
      return file ? catalogApi.setCategoryImage(saved.id, file) : saved;
    },
    onSuccess: () => {
      setDraft(null);
      setFile(null);
      void refresh();
    },
    onError: (error) => setFormProblem(describeError(error)),
  });
  const action = useMutation({
    mutationFn: (run: () => Promise<unknown>) => run(),
    onSuccess: () => {
      setActionProblem(null);
      void refresh();
    },
    onError: (error) => setActionProblem(describeError(error)),
  });

  const open = (category?: Category) => {
    setFormProblem(null);
    setFile(null);
    setDraft(
      category
        ? {
            id: category.id,
            name: category.name,
            description: category.description ?? '',
            sortOrder: category.sortOrder,
            isActive: category.isActive,
            version: category.version,
            imageUrl: category.thumbnailUrl,
          }
        : empty,
    );
  };

  const listProblem = list.isError ? describeError(list.error) : null;

  return (
    <>
      <Stack
        direction="row"

        sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 2 }}
      >
        <Typography variant="h4" component="h1">
          Categories
        </Typography>
        <Button variant="contained" onClick={() => open()}>
          New category
        </Button>
      </Stack>
      <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 2, mb: 2 }}>
        <TextField
          label="Search categories"
          size="small"
          value={q}
          onChange={(e) => setQ(e.target.value)}
        />
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
      {list.isPending && <p role="status">Loading categories…</p>}
      {list.data && list.data.items.length === 0 && <p>No categories yet.</p>}
      {list.data && list.data.items.length > 0 && (
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label="Categories">
            <TableHead>
              <TableRow>
                <TableCell>Image</TableCell>
                <TableCell>Name</TableCell>
                <TableCell align="right">Items</TableCell>
                <TableCell>Status</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {list.data.items.map((c) => (
                <TableRow key={c.id} sx={{ opacity: c.isDeleted ? 0.55 : 1 }}>
                  <TableCell>
                    {c.thumbnailUrl ? (
                      <img
                        className="thumb"
                        src={mediaUrl(c.thumbnailUrl)}
                        alt=""
                      />
                    ) : (
                      <span className="thumb thumb-empty" aria-hidden="true" />
                    )}
                  </TableCell>
                  <TableCell>
                    <strong>{c.name}</strong>
                    <Typography variant="body2" color="text.secondary">
                      {c.description}
                    </Typography>
                  </TableCell>
                  <TableCell align="right">{c.itemCount}</TableCell>
                  <TableCell>
                    {c.isDeleted ? (
                      <Chip size="small" label="Deleted" />
                    ) : c.isActive ? (
                      <Chip size="small" color="success" label="Active" />
                    ) : (
                      <Chip size="small" label="Hidden" />
                    )}
                  </TableCell>
                  <TableCell align="right">
                    {c.isDeleted ? (
                      <Button
                        onClick={() =>
                          action.mutate(() => catalogApi.restoreCategory(c.id))
                        }
                      >
                        Restore
                      </Button>
                    ) : (
                      <>
                        <Button onClick={() => open(c)}>Edit</Button>
                        <Button
                          color="error"
                          onClick={() =>
                            confirm({
                              title: `Delete ${c.name}?`,
                              text: 'The category is hidden and can be restored later.',
                              action: () =>
                                action.mutate(() =>
                                  catalogApi.deleteCategory(c.id),
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
      )}

      <Dialog
        open={draft !== null}
        onClose={() => setDraft(null)}
        fullWidth
        maxWidth="sm"
      >
        {draft && (
          <form
            noValidate
            onSubmit={(e) => {
              e.preventDefault();
              if (!draft.name.trim()) {
                setFormProblem({
                  message: 'Check the highlighted fields.',
                  fieldErrors: { Name: ['Name is required.'] },
                  signInRequired: false,
                });
                return;
              }
              save.mutate(draft);
            }}
          >
            <DialogTitle>
              {draft.id ? 'Edit category' : 'New category'}
            </DialogTitle>
            <DialogContent>
              <Stack sx={{ gap: 2, pt: 1 }}>
                <ProblemAlert problem={formProblem} />
                <TextField
                  label="Name"
                  required
                  value={draft.name}
                  onChange={(e) => setDraft({ ...draft, name: e.target.value })}
                  error={!!fieldError(formProblem, 'Name')}
                  helperText={fieldError(formProblem, 'Name')}
                  slotProps={{ htmlInput: { maxLength: 100 } }}
                />
                <TextField
                  label="Description"
                  multiline
                  minRows={2}
                  value={draft.description}
                  onChange={(e) =>
                    setDraft({ ...draft, description: e.target.value })
                  }
                  error={!!fieldError(formProblem, 'Description')}
                  helperText={fieldError(formProblem, 'Description')}
                  slotProps={{ htmlInput: { maxLength: 1000 } }}
                />
                <TextField
                  label="Sort order"
                  type="number"
                  value={draft.sortOrder}
                  onChange={(e) =>
                    setDraft({ ...draft, sortOrder: Number(e.target.value) })
                  }
                />
                <FormControlLabel
                  control={
                    <Switch
                      checked={draft.isActive}
                      onChange={(e) =>
                        setDraft({ ...draft, isActive: e.target.checked })
                      }
                    />
                  }
                  label="Show on the shop website"
                />
                <ImagePicker
                  current={draft.imageUrl}
                  file={file}
                  onChange={(f, problem) => {
                    setFile(f);
                    setFormProblem(problem);
                  }}
                />
              </Stack>
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setDraft(null)}>Cancel</Button>
              <Button
                type="submit"
                variant="contained"
                disabled={save.isPending}
              >
                Save
              </Button>
            </DialogActions>
          </form>
        )}
      </Dialog>
      {dialog}
    </>
  );
}

export function ImagePicker({
  current,
  file,
  onChange,
  label = 'Image',
}: {
  current?: string | null;
  file: File | null;
  onChange: (file: File | null, problem: ApiProblem | null) => void;
  label?: string;
}) {
  const [preview, setPreview] = useState<string | null>(null);
  const shown = file && preview ? preview : mediaUrl(current);
  return (
    <Stack direction="row" sx={{ gap: 2, alignItems: 'center' }}>
      {shown ? (
        <img className="preview" src={shown} alt="Selected" />
      ) : (
        <span className="preview thumb-empty" aria-hidden="true" />
      )}
      <Button component="label" variant="outlined">
        {label}
        <input
          hidden
          type="file"
          accept={ACCEPTED_IMAGES}
          aria-label={label}
          onChange={(e) => {
            const chosen = e.target.files?.[0] ?? null;
            e.target.value = '';
            if (!chosen) return;
            const problem = checkImageFile(chosen);
            if (problem) {
              onChange(null, {
                message: problem,
                fieldErrors: {},
                signInRequired: false,
              });
              return;
            }
            if (preview) URL.revokeObjectURL(preview);
            setPreview(URL.createObjectURL(chosen));
            onChange(chosen, null);
          }}
        />
      </Button>
    </Stack>
  );
}
