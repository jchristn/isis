import { useTranslation } from 'react-i18next';
import { PAGE_SIZE_OPTIONS } from '../utils/constants';
import { formatNumber } from '../i18n/formatters';

/**
 * Unified table toolbar, rendered at the TOP of a table. One bar holds the
 * "showing N of M" count, the per-page selector, first/prev/jump/next/last
 * navigation, and — pushed to the right — any caller toolbar content, the
 * refresh control, and the column selector.
 */
function Pagination({
  page,
  pageSize,
  totalRecords,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions = PAGE_SIZE_OPTIONS,
  toolbarLeft = null,
  toolbarRight = null,
  refresh = null,
  columnSelector = null
}) {
  const { t, i18n } = useTranslation();
  const totalPages = Math.max(1, Math.ceil(totalRecords / pageSize));
  const from = totalRecords === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalRecords);

  return (
    <div className="pagination table-toolbar">
      {toolbarLeft}
      <span className="pagination-count">
        {t('table.showing', {
          from: formatNumber(from, i18n.language),
          to: formatNumber(to, i18n.language),
          total: formatNumber(totalRecords, i18n.language)
        })}
      </span>
      <label style={{ margin: 0 }} htmlFor="page-size-select">
        {t('table.pageSize')}
      </label>
      <select
        id="page-size-select"
        value={pageSize}
        onChange={(e) => onPageSizeChange(Number(e.target.value))}
      >
        {pageSizeOptions.map((size) => (
          <option key={size} value={size}>
            {size}
          </option>
        ))}
      </select>
      <button onClick={() => onPageChange(1)} disabled={page <= 1}>
        «
      </button>
      <button onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
        ‹
      </button>
      <span>{t('table.page', { page, pages: totalPages })}</span>
      <input
        className="page-jump"
        type="number"
        min={1}
        max={totalPages}
        aria-label={t('table.jumpTo')}
        value={page}
        onChange={(e) => {
          const next = Number(e.target.value);
          if (next >= 1 && next <= totalPages) onPageChange(next);
        }}
      />
      <button onClick={() => onPageChange(page + 1)} disabled={page >= totalPages}>
        ›
      </button>
      <button onClick={() => onPageChange(totalPages)} disabled={page >= totalPages}>
        »
      </button>
      <span className="spacer" />
      {toolbarRight}
      {refresh}
      {columnSelector}
    </div>
  );
}

export default Pagination;
