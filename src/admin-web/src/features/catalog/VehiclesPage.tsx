import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { catalogApi, describeError, type ApiProblem } from './api';
import { ProblemAlert } from './ProblemAlert';

export function VehiclesPage() {
  const queryClient = useQueryClient();
  const [make, setMake] = useState('');
  const [models, setModels] = useState<Record<string, string>>({});
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const list = useQuery({
    queryKey: ['vehicle-makes'],
    queryFn: catalogApi.makes,
  });
  const run = useMutation({
    mutationFn: (action: () => Promise<unknown>) => action(),
    onSuccess: () => {
      setProblem(null);
      void queryClient.invalidateQueries({ queryKey: ['vehicle-makes'] });
    },
    onError: (error) => setProblem(describeError(error)),
  });

  return (
    <>
      <Typography variant="h4" component="h1" sx={{ mb: 1 }}>
        Vehicles
      </Typography>
      <p>
        Makes and models that items can fit. Add them once and pick them on each
        item.
      </p>
      <ProblemAlert
        problem={list.isError ? describeError(list.error) : problem}
      />
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (!make.trim()) return;
          run.mutate(() => catalogApi.createMake(make).then(() => setMake('')));
        }}
      >
        <Stack direction="row" sx={{ gap: 1, my: 2 }}>
          <TextField
            size="small"
            label="New make, e.g. Toyota"
            value={make}
            onChange={(e) => setMake(e.target.value)}
          />
          <Button type="submit" variant="contained">
            Add make
          </Button>
        </Stack>
      </form>
      {list.data?.length === 0 && <p>No vehicle makes yet.</p>}
      <Stack sx={{ gap: 2 }}>
        {list.data?.map((m) => (
          <Paper key={m.id} variant="outlined" sx={{ p: 2 }}>
            <Typography variant="h6" component="h2">
              {m.name}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              {m.models.length
                ? m.models.map((x) => x.name).join(', ')
                : 'No models yet'}
            </Typography>
            <form
              onSubmit={(e) => {
                e.preventDefault();
                const name = models[m.id]?.trim();
                if (!name) return;
                run.mutate(() =>
                  catalogApi
                    .createModel(m.id, name)
                    .then(() => setModels({ ...models, [m.id]: '' })),
                );
              }}
            >
              <Stack direction="row" sx={{ gap: 1 }}>
                <TextField
                  size="small"
                  label={`New ${m.name} model`}
                  value={models[m.id] ?? ''}
                  onChange={(e) =>
                    setModels({ ...models, [m.id]: e.target.value })
                  }
                />
                <Button type="submit">Add model</Button>
              </Stack>
            </form>
          </Paper>
        ))}
      </Stack>
    </>
  );
}
