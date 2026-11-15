const money = new Intl.NumberFormat('en-NZ', {
  style: 'currency',
  currency: 'NZD',
  minimumFractionDigits: 2,
});
const date = new Intl.DateTimeFormat('en-NZ', {
  timeZone: 'Pacific/Auckland',
  dateStyle: 'medium',
  timeStyle: 'short',
});
export const formatMoney = (value: number) => money.format(value);
export const formatShopDate = (value: string | Date) =>
  date.format(new Date(value));
