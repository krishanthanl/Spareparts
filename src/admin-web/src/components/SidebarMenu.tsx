import { useState } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import type { MenuNode } from '../features/auth/session';

const collapsedKey = 'admin.nav.collapsed';

const readCollapsed = (): string[] => {
  try {
    const value: unknown = JSON.parse(
      localStorage.getItem(collapsedKey) ?? '[]',
    );
    return Array.isArray(value)
      ? value.filter((x) => typeof x === 'string')
      : [];
  } catch {
    return [];
  }
};

const containsPath = (node: MenuNode, pathname: string): boolean =>
  node.children.some(
    (c) =>
      (c.path !== null &&
        c.path !== '/' &&
        (pathname === c.path || pathname.startsWith(c.path + '/'))) ||
      containsPath(c, pathname),
  );

/** Menu groups open and close; the choice is remembered in this browser. */
function MenuLinks({
  nodes,
  collapsed,
  toggle,
}: {
  nodes: MenuNode[];
  collapsed: string[];
  toggle: (key: string) => void;
}) {
  const { pathname } = useLocation();
  return nodes.map((node) => {
    if (node.path)
      return (
        <NavLink key={node.key} end={node.path === '/'} to={node.path}>
          {node.title}
        </NavLink>
      );
    // The group holding the current page always shows it.
    const open = !collapsed.includes(node.key) || containsPath(node, pathname);
    const id = `nav-group-${node.key}`;
    return (
      <div key={node.key} className="nav-group">
        <button
          type="button"
          className="nav-group-title"
          aria-expanded={open}
          aria-controls={id}
          onClick={() => toggle(node.key)}
        >
          <span>{node.title}</span>
          <span className="nav-chevron" aria-hidden="true">
            {open ? '▾' : '▸'}
          </span>
        </button>
        {open && (
          <div
            id={id}
            role="group"
            aria-label={node.title}
            className="nav-group-items"
          >
            <MenuLinks
              nodes={node.children}
              collapsed={collapsed}
              toggle={toggle}
            />
          </div>
        )}
      </div>
    );
  });
}

/** The sidebar menu. Groups open and close; closed groups are remembered in this browser. */
export function SidebarMenu({ menus }: { menus: MenuNode[] }) {
  const [collapsed, setCollapsed] = useState(readCollapsed);
  const toggle = (key: string) =>
    setCollapsed((current) => {
      const next = current.includes(key)
        ? current.filter((k) => k !== key)
        : [...current, key];
      try {
        localStorage.setItem(collapsedKey, JSON.stringify(next));
      } catch {
        // Storage can be unavailable (private mode); the menu still works.
      }
      return next;
    });
  return (
    <nav aria-label="Main navigation">
      <MenuLinks nodes={menus} collapsed={collapsed} toggle={toggle} />
    </nav>
  );
}
