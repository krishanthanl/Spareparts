import { describe, expect, it } from 'vitest';
import { formatMoney, formatShopDate } from './shop';
describe('New Zealand display', () => {
  it('formats NZD to cents', () => {
    expect(formatMoney(1234.5)).toContain('1,234.50');
  });
  it('uses shop time rather than machine time', () => {
    expect(formatShopDate('2026-12-31T12:00:00Z')).toContain('2027');
  });
});
