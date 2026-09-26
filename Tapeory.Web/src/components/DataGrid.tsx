import { useCallback, useId, useMemo, type ReactNode } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import "./dataGrid.css";

export type SortDirection = "asc" | "desc";

export interface GridSort<K extends string> {
  key: K;
  direction: SortDirection;
}

export interface GridColumn<K extends string> {
  key: K;
  /** Already translated. */
  label: string;
}

/**
 * Clicking the sorted column flips it; a new column starts ascending, except the columns in
 * `descendingFirst` (dates, counts) where the biggest/newest first is what people expect.
 */
export function nextGridSort<K extends string>(
  current: GridSort<K> | null,
  key: K,
  descendingFirst: readonly K[] = [],
): GridSort<K> {
  if (current?.key === key) {
    return { key, direction: current.direction === "asc" ? "desc" : "asc" };
  }

  return { key, direction: descendingFirst.includes(key) ? "desc" : "asc" };
}

/** Keeps the grid sort in `?sort=<key>&dir=desc`, so it survives reloads and can be shared. */
export function useUrlSort<K extends string>(
  isKey: (value: string | null) => value is K,
): [GridSort<K> | null, (next: GridSort<K> | null) => void] {
  const [searchParams, setSearchParams] = useSearchParams();
  const key = searchParams.get("sort");
  const dir = searchParams.get("dir");

  const sort = useMemo<GridSort<K> | null>(
    () => (isKey(key) ? { key, direction: dir === "desc" ? "desc" : "asc" } : null),
    [isKey, key, dir],
  );

  const setSort = useCallback(
    (next: GridSort<K> | null) => {
      setSearchParams(
        (prev) => {
          const params = new URLSearchParams(prev);
          if (next) params.set("sort", next.key);
          else params.delete("sort");
          if (next?.direction === "desc") params.set("dir", "desc");
          else params.delete("dir");
          return params;
        },
        { replace: true },
      );
    },
    [setSearchParams],
  );

  return [sort, setSort];
}

export function SortIcon({ direction }: { direction: SortDirection | null }) {
  return (
    <svg className="th-sort__icon" viewBox="0 0 10 14" aria-hidden="true">
      <path d="M5 1 9 5.5H1Z" className={direction === "asc" ? "is-active" : undefined} />
      <path d="M5 13 1 8.5h8Z" className={direction === "desc" ? "is-active" : undefined} />
    </svg>
  );
}

interface DataGridProps<K extends string> {
  columns: GridColumn<K>[];
  sort: GridSort<K> | null;
  onSortChange: (sort: GridSort<K> | null) => void;
  /** Columns that start descending when first clicked. */
  descendingFirst?: readonly K[];
  /** Adds an unlabeled-looking (but screen-reader named) trailing column for row actions. */
  hasActions?: boolean;
  footer?: ReactNode;
  className?: string;
  /** The `<DataGridRow>`s. */
  children: ReactNode;
}

/**
 * A sortable table that turns into stacked rows on phones. Cells mark their role with classes:
 * `data-grid__primary` (title cell, first line on phones), `data-grid__actions-cell` (icon
 * actions, top right on phones), `data-grid__end` (pushed right on the phone meta line) and
 * `data-grid__hide-mobile`.
 */
export function DataGrid<K extends string>({
  columns,
  sort,
  onSortChange,
  descendingFirst,
  hasActions = false,
  footer,
  className,
  children,
}: DataGridProps<K>) {
  const { t } = useTranslation();
  const sortSelectId = useId();

  return (
    <div className={`data-grid card page-enter${className ? ` ${className}` : ""}`}>
      {/* Phones hide the header row, so sorting moves into this bar. */}
      <div className="data-grid__mobile-sort">
        <div className="data-grid__sort-field">
          <label htmlFor={sortSelectId}>{t("common.sortBy")}</label>
          <select
            id={sortSelectId}
            value={sort?.key ?? ""}
            onChange={(e) => {
              const column = columns.find((item) => item.key === e.target.value);
              onSortChange(column ? nextGridSort(null, column.key, descendingFirst) : null);
            }}
          >
            <option value="">{t("common.sortDefault")}</option>
            {columns.map((column) => (
              <option key={column.key} value={column.key}>
                {column.label}
              </option>
            ))}
          </select>
        </div>
        {sort && (
          <button
            type="button"
            className="data-grid__direction"
            onClick={() => onSortChange(nextGridSort(sort, sort.key))}
          >
            <SortIcon direction={sort.direction} />
            {t(sort.direction === "asc" ? "common.sortAscending" : "common.sortDescending")}
          </button>
        )}
      </div>

      <div className="data-grid__scroll">
        <table>
          <thead>
            <tr>
              {columns.map((column) => {
                const direction = sort?.key === column.key ? sort.direction : null;
                return (
                  <th
                    key={column.key}
                    className={`data-grid__col--${column.key}`}
                    aria-sort={direction ? (direction === "asc" ? "ascending" : "descending") : undefined}
                  >
                    <button
                      type="button"
                      className={`th-sort${direction ? " is-sorted" : ""}`}
                      onClick={() => onSortChange(nextGridSort(sort, column.key, descendingFirst))}
                    >
                      {column.label}
                      <SortIcon direction={direction} />
                    </button>
                  </th>
                );
              })}
              {hasActions && (
                <th className="data-grid__actions-cell">
                  <span className="visually-hidden">{t("common.actions")}</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>{children}</tbody>
        </table>
      </div>

      {footer && <div className="data-grid__footer">{footer}</div>}
    </div>
  );
}

/** A row that opens `href` when clicked anywhere except on its own links and buttons. */
export function DataGridRow({ href, children }: { href: string; children: ReactNode }) {
  const navigate = useNavigate();

  return (
    <tr
      onClick={(e) => {
        if (!(e.target as HTMLElement).closest("a, button, input, select, textarea")) navigate(href);
      }}
    >
      {children}
    </tr>
  );
}
