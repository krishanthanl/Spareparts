import { useQuery } from '@tanstack/react-query';
import { publicCatalog, type PartsFilters } from '../api/catalog';

export function VehiclePicker({
  values,
  change,
}: {
  values: PartsFilters;
  change: (patch: PartsFilters) => void;
}) {
  const makeId = values.makeId ?? '';
  const makes = useQuery({
    queryKey: ['vehicle-makes'],
    queryFn: publicCatalog.makes,
  });
  const models = useQuery({
    queryKey: ['vehicle-models', makeId],
    queryFn: ({ signal }) => publicCatalog.models(makeId, signal),
    enabled: !!makeId,
  });
  const year = new Date().getFullYear() + 1;
  return (
    <fieldset className="vehicle-picker">
      <legend>Find parts by vehicle</legend>
      <label>
        Make
        <select
          value={makeId}
          onChange={(e) =>
            change({ makeId: e.target.value, modelId: '', year: '' })
          }
        >
          <option value="">Any make</option>
          {makes.data?.map((make) => (
            <option key={make.id} value={make.id}>
              {make.name}
            </option>
          ))}
        </select>
      </label>
      <label>
        Model
        <select
          value={values.modelId ?? ''}
          disabled={!makeId || models.isPending}
          onChange={(e) => change({ modelId: e.target.value, year: '' })}
        >
          <option value="">Any model</option>
          {models.data?.map((model) => (
            <option key={model.id} value={model.id}>
              {model.name}
            </option>
          ))}
        </select>
      </label>
      <label>
        Year
        <select
          value={values.year ?? ''}
          disabled={!values.modelId}
          onChange={(e) => change({ year: e.target.value })}
        >
          <option value="">Any year</option>
          {Array.from({ length: year - 1885 }, (_, i) => year - i).map((y) => (
            <option key={y}>{y}</option>
          ))}
        </select>
      </label>
      {(makes.isError || (makeId && models.isError)) && (
        <p role="status">Vehicle options are unavailable.</p>
      )}
    </fieldset>
  );
}
