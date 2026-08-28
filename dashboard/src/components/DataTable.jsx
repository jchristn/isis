import { useState, useMemo, useCallback, useRef, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import Pagination from './Pagination';
import { LoadingState } from './States';
import { IconRefresh } from './Icons';
import { DEFAULT_PAGE_SIZE } from '../utils/constants';

/**
 * Reusable data table: sorting, client-side pagination, a "Select Columns"
 * control (with pinned always-visible columns), loading/empty states, and
 * row-click guards. Column selection persists per table via `tableId`.
 *
 * Column shape:
 *   { key, label, render?(item), sortValue?(item), sortable?, pinned?, cellClass?, numeric? }
 */
function DataTable({
  columns = [],
  data = [],
  loading = false,
  tableId = 'table',
  onRowClick = null,
  onRefresh = null,
  toolbarLeft = null,
  toolbarRight = null,
  emptyMessage = null,
  pageSize: initialPageSize = DEFAULT_PAGE_SIZE
}) {
  const { t } = useTranslation();
  const [sortKey, setSortKey] = useState(null);
  const [sortDir, setSortDir] = useState('asc');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(() => {
    try {
      const stored = parseInt(localStorage.getItem(`isis_pagesize_${tableId}`), 10);
      return Number.isFinite(stored) && stored > 0 ? stored : initialPageSize;
    } catch {
      return initialPageSize;
    }
  });
  const [showColumnMenu, setShowColumnMenu] = useState(false);
  const columnMenuRef = useRef(null);

  const storageKey = `isis_cols_${tableId}`;
  const [hiddenColumns, setHiddenColumns] = useState(() => {
    try {
      return new Set(JSON.parse(localStorage.getItem(storageKey) || '[]'));
    } catch {
      return new Set();
    }
  });

  useEffect(() => {
    if (!showColumnMenu) return undefined;
    const onClick = (e) => {
      if (columnMenuRef.current && !columnMenuRef.current.contains(e.target)) setShowColumnMenu(false);
    };
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, [showColumnMenu]);

  // Reset to page 1 whenever the dataset size changes materially.
  useEffect(() => {
    setPage(1);
  }, [data.length, pageSize, sortKey, sortDir]);

  // Persist the chosen page size per table so it is restored on the next visit.
  const changePageSize = useCallback(
    (size) => {
      setPageSize(size);
      try {
        localStorage.setItem(`isis_pagesize_${tableId}`, String(size));
      } catch {
        /* ignore storage failures */
      }
    },
    [tableId]
  );

  const toggleColumn = useCallback(
    (key) => {
      setHiddenColumns((prev) => {
        const next = new Set(prev);
        if (next.has(key)) next.delete(key);
        else next.add(key);
        localStorage.setItem(storageKey, JSON.stringify([...next]));
        return next;
      });
    },
    [storageKey]
  );

  const visibleColumns = columns.filter((c) => c.pinned || !hiddenColumns.has(c.key));

  const handleSort = useCallback(
    (col) => {
      if (col.sortable === false || col.isAction) return;
      if (sortKey === col.key) {
        setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
      } else {
        setSortKey(col.key);
        setSortDir('asc');
      }
    },
    [sortKey]
  );

  const sorted = useMemo(() => {
    if (!sortKey) return data;
    const col = columns.find((c) => c.key === sortKey);
    if (!col) return data;
    const getVal = col.sortValue || ((item) => item[sortKey]);
    const copy = [...data];
    copy.sort((a, b) => {
      const av = getVal(a);
      const bv = getVal(b);
      if (av === bv) return 0;
      if (av === null || av === undefined) return 1;
      if (bv === null || bv === undefined) return -1;
      const cmp = typeof av === 'number' && typeof bv === 'number' ? av - bv : String(av).localeCompare(String(bv));
      return sortDir === 'asc' ? cmp : -cmp;
    });
    return copy;
  }, [data, columns, sortKey, sortDir]);

  const pageData = useMemo(() => {
    const start = (page - 1) * pageSize;
    return sorted.slice(start, start + pageSize);
  }, [sorted, page, pageSize]);

  const handleRowClick = (item, e) => {
    if (!onRowClick) return;
    if (e.target.closest('[data-row-click-ignore="true"], a, button, input, select, textarea, label')) return;
    onRowClick(item);
  };

  const hideableColumns = columns.filter((c) => !c.pinned);

  const columnSelector = (
    <div className="action-menu-wrap" ref={columnMenuRef}>
      <button className="btn-secondary btn-sm" onClick={() => setShowColumnMenu((v) => !v)}>
        {t('table.selectColumns')}
      </button>
      {showColumnMenu && (
        <div className="action-menu-portal" style={{ position: 'absolute', right: 0, top: '110%', left: 'auto' }}>
          {hideableColumns.map((col) => (
            <label key={col.key} className="action-menu-item" style={{ display: 'flex', gap: '0.5rem' }}>
              <input
                type="checkbox"
                style={{ width: 'auto' }}
                checked={!hiddenColumns.has(col.key)}
                onChange={() => toggleColumn(col.key)}
              />
              {col.label}
            </label>
          ))}
        </div>
      )}
    </div>
  );

  const refreshButton = onRefresh ? (
    <button className="btn-icon" onClick={onRefresh} title={t('common.refresh')} aria-label={t('common.refresh')}>
      <IconRefresh />
    </button>
  ) : null;

  return (
    <div className="table-frame">
      <Pagination
        page={page}
        pageSize={pageSize}
        totalRecords={sorted.length}
        onPageChange={setPage}
        onPageSizeChange={changePageSize}
        toolbarLeft={toolbarLeft}
        toolbarRight={toolbarRight}
        refresh={refreshButton}
        columnSelector={columnSelector}
      />

      {loading ? (
        <LoadingState />
      ) : (
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                {visibleColumns.map((col) => (
                  <th
                    key={col.key}
                    scope="col"
                    className={col.sortable === false || col.isAction ? '' : 'sortable'}
                    onClick={() => handleSort(col)}
                    aria-sort={sortKey === col.key ? (sortDir === 'asc' ? 'ascending' : 'descending') : undefined}
                    style={col.width ? { width: col.width } : undefined}
                  >
                    {col.label}
                    {sortKey === col.key && <span> {sortDir === 'asc' ? '▲' : '▼'}</span>}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {pageData.length === 0 ? (
                <tr>
                  <td colSpan={visibleColumns.length} className="table-empty">
                    {emptyMessage || t('table.noData')}
                  </td>
                </tr>
              ) : (
                pageData.map((item, idx) => (
                  <tr
                    key={item.id || item.Id || item.slug || idx}
                    className={onRowClick ? 'clickable' : ''}
                    onClick={(e) => handleRowClick(item, e)}
                  >
                    {visibleColumns.map((col) => (
                      <td
                        key={col.key}
                        className={`${col.cellClass || ''}${col.numeric ? ' cell-num' : ''}`}
                      >
                        {col.render ? col.render(item) : item[col.key] ?? '—'}
                      </td>
                    ))}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export default DataTable;
