import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import Autocomplete from '@mui/material/Autocomplete';
import Chip from '@mui/material/Chip';
import TextField from '@mui/material/TextField';
import { formatMoney } from '../../lib/shop';
import {
  formatQty,
  inventoryApi,
  type DocumentStatus,
  type ItemOption,
} from './api';

export function StatusChip({ status }: { status: DocumentStatus }) {
  const color =
    status === 'Posted' ? 'success' : status === 'Draft' ? 'info' : 'default';
  return <Chip size="small" color={color} label={status} />;
}

/** Type a SKU or name, pick an item. Shows stock and price to help entry. */
export function ItemPicker({
  label = 'Item',
  onPick,
  autoFocus,
}: {
  label?: string;
  onPick: (item: ItemOption) => void;
  autoFocus?: boolean;
}) {
  const [input, setInput] = useState('');
  const [value, setValue] = useState<ItemOption | null>(null);
  const search = useQuery({
    queryKey: ['item-search', input],
    queryFn: () => inventoryApi.searchItems(input),
    enabled: input.trim().length >= 1,
  });
  return (
    <Autocomplete
      size="small"
      sx={{ minWidth: 280, flex: 1 }}
      options={search.data ?? []}
      value={value}
      inputValue={input}
      filterOptions={(x) => x}
      loading={search.isFetching}
      getOptionLabel={(o) => `${o.sku} — ${o.name}`}
      isOptionEqualToValue={(a, b) => a.id === b.id}
      noOptionsText={input ? 'No matching items' : 'Type a SKU or name'}
      onInputChange={(_, text) => setInput(text)}
      onChange={(_, item) => {
        if (item) {
          onPick(item);
          setValue(null);
          setInput('');
        }
      }}
      renderOption={(props, o) => (
        <li {...props} key={o.id}>
          <span className="mono">{o.sku}</span>&nbsp;{o.name}
          <span
            className="muted"
            style={{ marginLeft: 'auto', paddingLeft: 12 }}
          >
            {formatQty(o.onHand)} in stock ·{' '}
            {o.sellingPrice === null ? 'no price' : formatMoney(o.sellingPrice)}
          </span>
        </li>
      )}
      renderInput={(params) => (
        <TextField {...params} label={label} autoFocus={autoFocus} />
      )}
    />
  );
}
