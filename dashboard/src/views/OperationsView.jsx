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

// Time windows for the operation chart — identical selector/controls to Request History. The /operations
// endpoint has no time/aggregate params, so bucketing is client-side over the loaded records: each window
// rebuilds a zero-filled, gap-free set of buckets at its step size — hour→1m, day→15m, week→1h, month→6h.
const RANGES = [
  { id: 'hour', hours: 1, stepMs: 60 * 1000 },
  { id: 'day', hours: 24, stepMs: 15 * 60 * 1000 },
  { id: 'week', hours: 24 * 7, stepMs: 60 * 60 * 1000 },
  { id: 'month', hours: 24 * 30, stepMs: 6 * 60 * 60 * 1000 }
];

// Per-operation styling shared by the chart's legend + stacked segments.
const OP_STYLE = {
  create: { color: 'var(--color-primary)', labelKey: 'operations.opCreate' },
  read: { color: 'var(--color-info)', labelKey: 'operations.opRead' },
  update: { color: 'var(--color-warning)', labelKey: 'operations.opUpdate' },
  delete: { color: 'var(--color-danger)', labelKey: 'operations.opDelete' },
  search: { color: 'var(--color-success)', labelKey: 'operations.opSearch' },
  chat: { color: 'var(--color-primary)', labelKey: 'operations.opChat' },
  login: { color: 'var(--color-success)', labelKey: 'operations.opLogin' },
  logout: { color: 'var(--color-text-muted)', labelKey: 'operations.opLogout' }
};

// One tab per resource type, in display order, with the operations it can exhibit.
const TABS = [
  { resource: 'scope', titleKey: 'operations.chartScope', ops: ['create', 'read', 'update', 'delete'] },
  { resource: 'memory', titleKey: 'operations.chartMemory', ops: ['create', 'read', 'update', 'delete'] },
  { resource: 'search', titleKey: 'operations.chartSearch', ops: ['search'] },
  { resource: 'chat', titleKey: 'operations.chartChat', ops: ['chat'] },
  { resource: 'category', titleKey: 'operations.chartCategory', ops: ['create', 'read', 'update', 'delete'] },
  { resource: 'instruction', titleKey: 'operations.chartInstruction', ops: ['create', 'read', 'update', 'delete'] },
  { resource: 'endpoint', titleKey: 'operations.chartEndpoint', ops: ['create', 'read', 'update', 'delete'] },
  { resource: 'auth', titleKey: 'operations.chartAuth', ops: ['login', 'logout', 'read'] }
];

function statusTone(code) {
  if (code >= 500) return 'danger';
  if (code >= 400) return 'warning';
  if (code >= 300) return 'info';
  if (code >= 200) return 'success';
  return 'neutral';
}

function OperationsView() {
  const { t, i18n } = useTranslation();
  const { apiClient, isAdmin, isTenantAdmin } = useAuth();
  const { addToast } = useApp();

  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [operation, setOperation] = useState('');
  const [search, setSearch] = useState('');
  const [activeResource, setActiveResource] = useState(() => {
    try {
      const stored = localStorage.getItem('isis_operations_tab');
      return TABS.some((c) => c.resource === stored) ? stored : TABS[0].resource;
    } catch {
      return TABS[0].resource;
    }
  });
  const [rangeId, setRangeId] = useState(() => {
    try {
      const stored = localStorage.getItem('isis_operations_range');
      return RANGES.some((r) => r.id === stored) ? stored : 'day';
    } catch {
      return 'day';
    }
  });

  // Remember the selected tab + chart timeframe across visits.
  useEffect(() => {
    try {
      localStorage.setItem('isis_operations_tab', activeResource);
    } catch {
      /* ignore storage failures */
    }
  }, [activeResource]);
  useEffect(() => {
    try {
      localStorage.setItem('isis_operations_range', rangeId);
    } catch {
      /* ignore storage failures */
    }
  }, [rangeId]);

  // Clear any operation sub-filter when switching tabs (the ops differ per resource).
  useEffect(() => {
    setOperation('');
  }, [activeResource]);

  const [inspect, setInspect] = useState(null);
  const [confirmClear, setConfirmClear] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.getOperations({ maxResults: 5000 });
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

  const range = useMemo(() => RANGES.find((r) => r.id === rangeId) || RANGES[1], [rangeId]);
  const activeTab = useMemo(() => TABS.find((c) => c.resource === activeResource) || TABS[0], [activeResource]);

  // Everything is derived from a single time window so the chart, KPIs, per-tab counts, and table stay
  // consistent. `windowRows` = all resources in-window (drives the per-tab counts); `tabRows` = the active
  // resource (drives the chart + KPIs); `tableRows` = tabRows narrowed by the operation + search filters.
  const { tabRows, tabCounts, timeBuckets } = useMemo(() => {
    const endMs = Date.now();
    const startMs = endMs - range.hours * 3600 * 1000;
    const floorToStep = (ts) => Math.floor(ts / range.stepMs) * range.stepMs;
    const timeOnly = range.id === 'hour' || range.id === 'day';

    const inWindow = [];
    const counts = {};
    for (const r of rows) {
      const ts = new Date(r.createdUtc).getTime();
      if (Number.isNaN(ts) || ts < startMs || ts > endMs) continue;
      inWindow.push(r);
      counts[r.resourceType] = (counts[r.resourceType] || 0) + 1;
    }

    const activeRows = inWindow.filter((r) => r.resourceType === activeResource);

    // Zero-filled, gap-free scaffold keyed by bucket start, one field per operation of the active tab.
    const scaffold = new Map();
    for (let b = floorToStep(startMs); b <= endMs; b += range.stepMs) {
      const bucket = { key: b };
      for (const op of activeTab.ops) bucket[op] = 0;
      scaffold.set(b, bucket);
    }
    for (const r of activeRows) {
      const bucket = scaffold.get(floorToStep(new Date(r.createdUtc).getTime()));
      if (!bucket || bucket[r.operation] === undefined) continue;
      bucket[r.operation] += 1;
    }

    const buckets = [...scaffold.values()].map((b) => {
      const when = new Date(b.key);
      const out = {
        label: timeOnly
          ? formatTimeShort(when, i18n.language)
          : `${formatDate(when, i18n.language)} ${formatTimeShort(when, i18n.language)}`
      };
      for (const op of activeTab.ops) out[op] = b[op];
      return out;
    });

    return { tabRows: activeRows, tabCounts: counts, timeBuckets: buckets };
  }, [rows, range, activeResource, activeTab, i18n.language]);

  const tableRows = useMemo(() => {
    const term = search.trim().toLowerCase();
    return tabRows.filter((r) => {
      if (operation && r.operation !== operation) return false;
      if (term && !(`${r.path || ''} ${r.principalName || ''} ${r.resourceId || ''}`.toLowerCase().includes(term))) return false;
      return true;
    });
  }, [tabRows, operation, search]);

  const stats = useMemo(() => {
    const total = tabRows.length;
    const errors = tabRows.filter((r) => (r.statusCode || 0) >= 400).length;
    const avg = total ? tabRows.reduce((s, r) => s + (r.durationMs || 0), 0) / total : 0;
    const errorRate = total ? (errors / total) * 100 : 0;
    return { total, errors, avg, errorRate };
  }, [tabRows]);

  const handleClear = async () => {
    await apiClient.clearOperations();
    addToast(t('operations.cleared'), 'success');
    setConfirmClear(false);
    load();
  };

  const series = useMemo(
    () => activeTab.ops.map((op) => ({ key: op, label: t(OP_STYLE[op].labelKey), color: OP_STYLE[op].color })),
    [activeTab, t]
  );

  const columns = [
    { key: 'createdUtc', label: t('operations.time'), render: (r) => formatDateTime(r.createdUtc, i18n.language), sortValue: (r) => r.createdUtc || '' },
    { key: 'operation', label: t('operations.operation'), width: '110px', render: (r) => <StatusBadge tone="info">{r.operation}</StatusBadge> },
    { key: 'method', label: t('operations.method'), width: '80px', render: (r) => <StatusBadge tone="neutral">{r.method}</StatusBadge> },
    { key: 'path', label: t('operations.path'), render: (r) => <span className="cell-truncate cell-mono">{r.path}</span> },
    { key: 'statusCode', label: t('operations.status'), width: '90px', numeric: true, render: (r) => <StatusBadge tone={statusTone(r.statusCode)}>{r.statusCode}</StatusBadge> },
    { key: 'durationMs', label: t('operations.duration'), numeric: true, render: (r) => `${Math.round(r.durationMs || 0)} ms` },
    { key: 'scopeId', label: t('operations.scopeId'), render: (r) => (r.scopeId ? <CopyableId value={r.scopeId} /> : '—') },
    { key: 'principalName', label: t('operations.principal'), render: (r) => r.principalName || '—' }
  ];

  const canClear = isAdmin || isTenantAdmin;

  return (
    <>
      <PageHeader
        title={t('operations.title')}
        subtitle={t('operations.subtitle')}
        actions={
          <>
            <button className="btn-secondary" onClick={load}>{t('common.refresh')}</button>
            {canClear && <button className="btn-secondary" onClick={() => setConfirmClear(true)}>{t('operations.clear')}</button>}
          </>
        }
      />

      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}

      {loading ? (
        <LoadingState />
      ) : (
        <>
          <div className="kpi-grid section">
            <KpiCard label={t('operations.kpiTotal')} value={formatNumber(stats.total, i18n.language)} />
            <KpiCard label={t('operations.kpiErrors')} value={formatNumber(stats.errors, i18n.language)} tone={stats.errors > 0 ? 'danger' : 'success'} />
            <KpiCard label={t('operations.kpiErrorRate')} value={`${stats.errorRate.toFixed(1)}%`} tone={stats.errorRate > 0 ? 'warning' : 'success'} />
            <KpiCard label={t('operations.kpiAvgLatency')} value={`${Math.round(stats.avg)} ms`} />
          </div>

          <div className="section">
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 'var(--spacing-sm)', marginBottom: 'var(--spacing-sm)' }}>
              <div className="section-title" style={{ marginBottom: 0 }}>{t('operations.overTime')}</div>
              <div role="tablist" aria-label={t('operations.overTime')} style={{ display: 'flex', gap: '0.25rem' }}>
                {RANGES.map((r) => (
                  <button
                    key={r.id}
                    type="button"
                    role="tab"
                    aria-selected={rangeId === r.id}
                    className={`btn-sm ${rangeId === r.id ? 'btn-primary' : 'btn-secondary'}`}
                    onClick={() => setRangeId(r.id)}
                  >
                    {t(`operations.range${r.id.charAt(0).toUpperCase()}${r.id.slice(1)}`)}
                  </button>
                ))}
              </div>
            </div>

            <div className="tabs" role="tablist" aria-label={t('operations.resourceType')} style={{ overflowX: 'auto' }}>
              {TABS.map((tab) => (
                <button
                  key={tab.resource}
                  type="button"
                  role="tab"
                  aria-selected={activeResource === tab.resource}
                  className={`tab${activeResource === tab.resource ? ' active' : ''}`}
                  style={{ whiteSpace: 'nowrap' }}
                  onClick={() => setActiveResource(tab.resource)}
                >
                  {t(tab.titleKey)}
                  <span style={{ marginLeft: '0.4rem', color: 'var(--color-text-muted)', fontVariantNumeric: 'tabular-nums' }}>
                    {formatNumber(tabCounts[tab.resource] || 0, i18n.language)}
                  </span>
                </button>
              ))}
            </div>

            <ActivityChart buckets={timeBuckets} series={series} height={280} emptyLabel={t('operations.empty')} />
          </div>

          <div className="filter-bar section">
            <div className="field">
              <label>{t('operations.operation')}</label>
              <select value={operation} onChange={(e) => setOperation(e.target.value)}>
                <option value="">{t('operations.allOperations')}</option>
                {activeTab.ops.map((op) => <option key={op} value={op}>{t(OP_STYLE[op].labelKey)}</option>)}
              </select>
            </div>
            <div className="field" style={{ flex: 1, minWidth: 200 }}>
              <label>{t('common.search')}</label>
              <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('operations.searchPlaceholder')} />
            </div>
          </div>

          <DataTable
            tableId="operations"
            columns={columns}
            data={tableRows}
            loading={false}
            onRefresh={load}
            onRowClick={(r) => setInspect(r)}
            emptyMessage={t('operations.empty')}
          />
        </>
      )}

      {inspect && (
        <Modal isOpen onClose={() => setInspect(null)} title={`${inspect.resourceType} · ${inspect.operation}`} size="full">
          <div className="detail-section">
            <div className="section-title">{t('requestHistory.sectionMetadata')}</div>
            <dl className="kv-grid">
              <dt>{t('operations.resourceType')}</dt><dd>{inspect.resourceType}</dd>
              <dt>{t('operations.operation')}</dt><dd>{inspect.operation}</dd>
              <dt>{t('operations.resourceId')}</dt><dd>{inspect.resourceId ? <CopyableId value={inspect.resourceId} /> : '—'}</dd>
              <dt>{t('operations.scopeId')}</dt><dd>{inspect.scopeId ? <CopyableId value={inspect.scopeId} /> : '—'}</dd>
              <dt>{t('operations.method')}</dt><dd>{inspect.method}</dd>
              <dt>{t('operations.path')}</dt><dd className="cell-mono">{inspect.path}</dd>
              <dt>{t('operations.status')}</dt><dd><StatusBadge tone={statusTone(inspect.statusCode)}>{inspect.statusCode}</StatusBadge></dd>
              <dt>{t('operations.duration')}</dt><dd>{Math.round(inspect.durationMs || 0)} ms</dd>
              <dt>{t('operations.principal')}</dt><dd>{inspect.principalName || '—'}</dd>
              <dt>{t('settings.tenant')}</dt><dd>{inspect.tenantId ? <CopyableId value={inspect.tenantId} /> : '—'}</dd>
              <dt>{t('operations.sourceIp')}</dt><dd className="cell-mono">{inspect.sourceIp || '—'}</dd>
              <dt>{t('operations.time')}</dt><dd>{formatDateTime(inspect.createdUtc, i18n.language)}</dd>
            </dl>
          </div>

          <details className="detail-raw">
            <summary>{t('operations.rawRecord')}</summary>
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
        message={t('operations.clearConfirm')}
      />
    </>
  );
}

export default OperationsView;
