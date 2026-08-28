import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import PageHeader from '../components/PageHeader';
import KpiCard from '../components/KpiCard';
import ActivityChart from '../components/ActivityChart';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import CopyableId from '../components/CopyableId';
import CodeViewer from '../components/CodeViewer';
import StatusBadge from '../components/StatusBadge';
import { LoadingState, ErrorBanner } from '../components/States';
import { formatNumber, formatDateTime, formatDate, formatTimeShort } from '../i18n/formatters';

const METHODS = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD', 'OPTIONS'];

// Time windows for the "API calls over time" chart. The /requests endpoint has no time/aggregate
// params, so bucketing is client-side over the loaded records: each window rebuilds a zero-filled,
// gap-free set of buckets at its step size — hour→1m, day→15m, week→1h, month→6h.
const RANGES = [
  { id: 'hour', hours: 1, stepMs: 60 * 1000 },
  { id: 'day', hours: 24, stepMs: 15 * 60 * 1000 },
  { id: 'week', hours: 24 * 7, stepMs: 60 * 60 * 1000 },
  { id: 'month', hours: 24 * 30, stepMs: 6 * 60 * 60 * 1000 }
];

function statusTone(code) {
  if (code >= 500) return 'danger';
  if (code >= 400) return 'warning';
  if (code >= 300) return 'info';
  if (code >= 200) return 'success';
  return 'neutral';
}

/** A muted single-line placeholder used when a section has no captured content. */
function EmptyLine({ children }) {
  return <div className="detail-empty">{children}</div>;
}

/**
 * Render a JSON string map of HTTP headers as a readable key/value table. Falls back to a raw
 * CodeViewer dump when the value isn't parseable JSON, and to an empty-state line when it is null.
 */
function HeadersSection({ raw, emptyLabel }) {
  if (raw == null || raw === '') return <EmptyLine>{emptyLabel}</EmptyLine>;
  return <CodeViewer value={raw} maxHeight={280} />;
}

/** Render a captured request/response body via CodeViewer, or an empty-state line when absent. */
function BodySection({ raw, emptyLabel }) {
  if (raw == null || raw === '') return <EmptyLine>{emptyLabel}</EmptyLine>;
  return <CodeViewer value={raw} maxHeight={420} />;
}

function RequestHistoryView() {
  const { t, i18n } = useTranslation();
  const { apiClient, isAdmin, isTenantAdmin } = useAuth();
  const { addToast } = useApp();

  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [method, setMethod] = useState('');
  const [statusClass, setStatusClass] = useState('');
  const [search, setSearch] = useState('');
  const [rangeId, setRangeId] = useState(() => {
    try {
      const stored = localStorage.getItem('isis_reqhistory_range');
      return RANGES.some((r) => r.id === stored) ? stored : 'day';
    } catch {
      return 'day';
    }
  });

  // Remember the selected chart timeframe across visits.
  useEffect(() => {
    try {
      localStorage.setItem('isis_reqhistory_range', rangeId);
    } catch {
      /* ignore storage failures */
    }
  }, [rangeId]);
  const [inspect, setInspect] = useState(null);
  const [confirmClear, setConfirmClear] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.getRequestHistory({ maxResults: 5000 });
      setRows(res.items || []);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient]);

  useEffect(() => {
    load();
  }, [load]);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return rows.filter((r) => {
      if (method && r.method !== method) return false;
      if (statusClass && Math.floor((r.statusCode || 0) / 100) !== Number(statusClass)) return false;
      if (term && !(`${r.path || ''} ${r.principalName || ''}`.toLowerCase().includes(term))) return false;
      return true;
    });
  }, [rows, method, statusClass, search]);

  // Restrict to the selected time window and bucket the in-window records into a gap-free series for
  // the activity chart. Success = status < 400, failure ≥ 400 (matches the reference dashboards).
  const { windowed, timeBuckets } = useMemo(() => {
    const range = RANGES.find((r) => r.id === rangeId) || RANGES[1];
    const endMs = Date.now();
    const startMs = endMs - range.hours * 3600 * 1000;
    const floorToStep = (ts) => Math.floor(ts / range.stepMs) * range.stepMs;

    const scaffold = new Map();
    for (let b = floorToStep(startMs); b <= endMs; b += range.stepMs) {
      scaffold.set(b, { key: b, success: 0, failure: 0 });
    }

    const inWindow = [];
    for (const r of filtered) {
      const ts = new Date(r.createdUtc).getTime();
      if (Number.isNaN(ts) || ts < startMs || ts > endMs) continue;
      inWindow.push(r);
      const bucket = scaffold.get(floorToStep(ts));
      if (!bucket) continue;
      if ((r.statusCode || 0) < 400) bucket.success += 1;
      else bucket.failure += 1;
    }

    const timeOnly = range.id === 'hour' || range.id === 'day';
    const buckets = [...scaffold.values()].map((b) => {
      const when = new Date(b.key);
      return {
        label: timeOnly ? formatTimeShort(when, i18n.language) : `${formatDate(when, i18n.language)} ${formatTimeShort(when, i18n.language)}`,
        success: b.success,
        failure: b.failure,
        total: b.success + b.failure,
        tooltip: [
          { k: t('requestHistory.success'), v: b.success },
          { k: t('requestHistory.failed'), v: b.failure }
        ]
      };
    });

    return { windowed: inWindow, timeBuckets: buckets };
  }, [filtered, rangeId, i18n.language, t]);

  const stats = useMemo(() => {
    const total = windowed.length;
    const errors = windowed.filter((r) => (r.statusCode || 0) >= 400).length;
    const avg = total ? windowed.reduce((s, r) => s + (r.durationMs || 0), 0) / total : 0;
    const errorRate = total ? (errors / total) * 100 : 0;
    return { total, errors, avg, errorRate };
  }, [windowed]);

  const handleClear = async () => {
    await apiClient.clearRequestHistory();
    addToast(t('requestHistory.cleared'), 'success');
    setConfirmClear(false);
    load();
  };

  const columns = [
    { key: 'createdUtc', label: t('requestHistory.time'), render: (r) => formatDateTime(r.createdUtc, i18n.language), sortValue: (r) => r.createdUtc || '' },
    { key: 'method', label: t('requestHistory.method'), width: '90px', render: (r) => <StatusBadge tone="info">{r.method}</StatusBadge> },
    { key: 'path', label: t('requestHistory.path'), render: (r) => <span className="cell-truncate cell-mono">{r.path}</span> },
    { key: 'statusCode', label: t('requestHistory.status'), width: '90px', numeric: true, render: (r) => <StatusBadge tone={statusTone(r.statusCode)}>{r.statusCode}</StatusBadge> },
    { key: 'durationMs', label: t('requestHistory.duration'), numeric: true, render: (r) => `${Math.round(r.durationMs || 0)} ms` },
    { key: 'principalName', label: t('requestHistory.principal'), render: (r) => r.principalName || '—' },
    { key: 'tenantId', label: t('settings.tenant'), render: (r) => (r.tenantId ? <CopyableId value={r.tenantId} /> : '—') },
    { key: 'sourceIp', label: t('requestHistory.sourceIp'), cellClass: 'cell-mono', render: (r) => r.sourceIp || '—' }
  ];

  const canClear = isAdmin || isTenantAdmin;

  return (
    <>
      <PageHeader
        title={t('requestHistory.title')}
        subtitle={t('requestHistory.subtitle')}
        actions={
          <>
            <button className="btn-secondary" onClick={load}>{t('common.refresh')}</button>
            {canClear && <button className="btn-secondary" onClick={() => setConfirmClear(true)}>{t('requestHistory.clear')}</button>}
          </>
        }
      />

      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}

      {loading ? (
        <LoadingState />
      ) : (
        <>
          <div className="kpi-grid section">
            <KpiCard label={t('requestHistory.kpiTotal')} value={formatNumber(stats.total, i18n.language)} />
            <KpiCard label={t('requestHistory.kpiErrors')} value={formatNumber(stats.errors, i18n.language)} tone={stats.errors > 0 ? 'danger' : 'success'} />
            <KpiCard label={t('requestHistory.kpiErrorRate')} value={`${stats.errorRate.toFixed(1)}%`} tone={stats.errorRate > 0 ? 'warning' : 'success'} />
            <KpiCard label={t('requestHistory.kpiAvgLatency')} value={`${Math.round(stats.avg)} ms`} />
          </div>

          <div className="section">
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 'var(--spacing-sm)', marginBottom: 'var(--spacing-sm)' }}>
              <div className="section-title" style={{ marginBottom: 0 }}>{t('requestHistory.overTime')}</div>
              <div role="tablist" aria-label={t('requestHistory.overTime')} style={{ display: 'flex', gap: '0.25rem' }}>
                {RANGES.map((r) => (
                  <button
                    key={r.id}
                    type="button"
                    role="tab"
                    aria-selected={rangeId === r.id}
                    className={`btn-sm ${rangeId === r.id ? 'btn-primary' : 'btn-secondary'}`}
                    onClick={() => setRangeId(r.id)}
                  >
                    {t(`requestHistory.range${r.id.charAt(0).toUpperCase()}${r.id.slice(1)}`)}
                  </button>
                ))}
              </div>
            </div>
            <ActivityChart buckets={timeBuckets} emptyLabel={t('requestHistory.empty')} />
          </div>

          <div className="filter-bar section">
            <div className="field">
              <label>{t('requestHistory.method')}</label>
              <select value={method} onChange={(e) => setMethod(e.target.value)}>
                <option value="">{t('common.all', 'All')}</option>
                {METHODS.map((m) => <option key={m} value={m}>{m}</option>)}
              </select>
            </div>
            <div className="field">
              <label>{t('requestHistory.status')}</label>
              <select value={statusClass} onChange={(e) => setStatusClass(e.target.value)}>
                <option value="">{t('common.all', 'All')}</option>
                <option value="2">2xx</option>
                <option value="3">3xx</option>
                <option value="4">4xx</option>
                <option value="5">5xx</option>
              </select>
            </div>
            <div className="field" style={{ flex: 1, minWidth: 200 }}>
              <label>{t('common.search')}</label>
              <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('requestHistory.searchPlaceholder')} />
            </div>
          </div>

          <DataTable
            tableId="request-history"
            columns={columns}
            data={windowed}
            loading={false}
            onRefresh={load}
            onRowClick={(r) => setInspect(r)}
            emptyMessage={t('requestHistory.empty')}
          />
        </>
      )}

      {inspect && (
        <Modal isOpen onClose={() => setInspect(null)} title={`${inspect.method} ${(inspect.path || '').split('?')[0]}`} size="full">
          <div className="detail-section">
            <div className="section-title">{t('requestHistory.sectionMetadata')}</div>
            <dl className="kv-grid">
              <dt>{t('requestHistory.method')}</dt><dd>{inspect.method}</dd>
              <dt>{t('requestHistory.path')}</dt><dd className="cell-mono">{inspect.path}</dd>
              <dt>{t('requestHistory.status')}</dt><dd><StatusBadge tone={statusTone(inspect.statusCode)}>{inspect.statusCode}</StatusBadge></dd>
              <dt>{t('requestHistory.duration')}</dt><dd>{Math.round(inspect.durationMs || 0)} ms</dd>
              <dt>{t('requestHistory.principal')}</dt><dd>{inspect.principalName || '—'}</dd>
              <dt>{t('settings.tenant')}</dt><dd>{inspect.tenantId ? <CopyableId value={inspect.tenantId} /> : '—'}</dd>
              <dt>{t('requestHistory.sourceIp')}</dt><dd className="cell-mono">{inspect.sourceIp || '—'}</dd>
              <dt>{t('requestHistory.time')}</dt><dd>{formatDateTime(inspect.createdUtc, i18n.language)}</dd>
            </dl>
          </div>

          <details className="detail-section" open>
            <summary>{t('requestHistory.requestHeaders')}</summary>
            <HeadersSection raw={inspect.requestHeaders} emptyLabel={t('requestHistory.noHeaders')} />
          </details>

          <details className="detail-section" open>
            <summary>{t('requestHistory.requestBody')}</summary>
            <BodySection raw={inspect.requestBody} emptyLabel={t('requestHistory.noBody')} />
          </details>

          <details className="detail-section" open>
            <summary>{t('requestHistory.responseHeaders')}</summary>
            <HeadersSection raw={inspect.responseHeaders} emptyLabel={t('requestHistory.noHeaders')} />
          </details>

          <details className="detail-section" open>
            <summary>{t('requestHistory.responseBody')}</summary>
            <BodySection raw={inspect.responseBody} emptyLabel={t('requestHistory.noBody')} />
          </details>

          <details className="detail-raw">
            <summary>{t('requestHistory.rawRecord')}</summary>
            <div style={{ marginTop: 'var(--spacing-sm)' }}>
              <CodeViewer value={inspect} />
            </div>
          </details>
        </Modal>
      )}

      <ConfirmModal
        isOpen={confirmClear}
        onClose={() => setConfirmClear(false)}
        onConfirm={handleClear}
        message={t('requestHistory.clearConfirm')}
      />
    </>
  );
}

export default RequestHistoryView;
