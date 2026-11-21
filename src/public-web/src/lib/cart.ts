import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';
import type { PublicItem } from '../api/catalog';

export type SavedLine = {
  itemId: string;
  quantity: number;
  name: string;
  price: number;
};
const validId = /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i;

export function readCart(saved: unknown): SavedLine[] {
  if (
    !saved ||
    typeof saved !== 'object' ||
    !('lines' in saved) ||
    !Array.isArray(saved.lines)
  )
    return [];
  const lines = new Map<string, SavedLine>();
  for (const line of saved.lines.slice(0, 25)) {
    if (
      !line ||
      typeof line.itemId !== 'string' ||
      !validId.test(line.itemId) ||
      typeof line.name !== 'string' ||
      !Number.isFinite(line.price) ||
      line.price < 0 ||
      !Number.isInteger(line.quantity) ||
      line.quantity < 1 ||
      line.quantity > 99
    )
      continue;
    const itemId = line.itemId.toLowerCase();
    lines.set(itemId, {
      itemId,
      quantity: line.quantity,
      name: line.name.slice(0, 200),
      price: line.price,
    });
  }
  return [...lines.values()];
}

type Cart = {
  lines: SavedLine[];
  save: (item: PublicItem, quantity: number) => void;
  remove: (itemId: string) => void;
};

export const useCart = create<Cart>()(
  persist(
    (set) => ({
      lines: [],
      save: (item, quantity) =>
        set(({ lines }) => {
          if (
            item.price == null ||
            !Number.isInteger(quantity) ||
            quantity < 1 ||
            quantity > 99
          )
            return { lines };
          const next = {
            itemId: item.id,
            quantity,
            name: item.name,
            price: item.price,
          };
          if (lines.some((line) => line.itemId === item.id))
            return {
              lines: lines.map((line) =>
                line.itemId === item.id ? next : line,
              ),
            };
          return { lines: lines.length < 25 ? [...lines, next] : lines };
        }),
      remove: (itemId) =>
        set(({ lines }) => ({
          lines: lines.filter((line) => line.itemId !== itemId),
        })),
    }),
    {
      name: 'spareparts-cart',
      version: 1,
      storage: createJSONStorage(() => ({
        getItem: (name) => {
          try {
            return localStorage.getItem(name);
          } catch {
            return null;
          }
        },
        setItem: (name, value) => {
          try {
            localStorage.setItem(name, value);
          } catch {
            // The cart still works for this session when storage is unavailable.
          }
        },
        removeItem: (name) => {
          try {
            localStorage.removeItem(name);
          } catch {
            // Storage might be disabled.
          }
        },
      })),
      partialize: ({ lines }) => ({ lines }),
      merge: (saved, current) => ({ ...current, lines: readCart(saved) }),
    },
  ),
);
