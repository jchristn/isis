import { useState, useEffect, useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import ActionMenu from '../components/ActionMenu';
import CopyableId from '../components/CopyableId';
import CodeViewer from '../components/CodeViewer';
import StatusBadge from '../components/StatusBadge';
import HealthHistogram from '../components/HealthHistogram';
import { ErrorBanner } from '../components/States';
import { API_FORMATS, HEALTH_METHODS } from '../utils/constants';
import { formatDateTime } from '../i18n/formatters';

const FORMAT_DEFAULTS = {
  Ollama: { port: 11434, useSsl: false, healthCheckUrl: '/api/tags', healthCheckUseAuth: false },
  OpenAI: { port: 443, useSsl: true, healthCheckUrl: '/v1/models', healthCheckUseAuth: true },
  VLlm: { port: 8000, useSsl: false, healthCheckUrl: '/v1/models', healthCheckUseAuth: false },
  Gemini: { port: 443, useSsl: true, healthCheckUrl: '/v1beta/models', healthCheckUseAuth: true }
};

function emptyForm(kind) {
  return {
    name: '',
    kind,
    apiFormat: 'Ollama',
    hostname: '127.0.0.1',
    port: 11434,
    useSsl: false,
    apiKey: '',
    model: '',
    dimensionality: kind === 'Embedding' ? 1536 : '',
    healthCheckUrl: '/api/tags',
    healthCheckMethod: 'GET',
    healthCheckIntervalMs: 5000,
    healthCheckExpectedStatusCode: 200,
    healthCheckUseAuth: false,
    active: true
  };
}

function EndpointForm({ kind, initial, onSubmit, onClose, t }) {
  const [form, setForm] = useState(
    initial
      ? {
          name: initial.name || '',
          kind,
          apiFormat: initial.apiFormat || 'Ollama',
          hostname: initial.hostname || '',
          port: initial.port ?? 11434,
          useSsl: initial.useSsl ?? false,
          apiKey: initial.apiKey || '',
          model: initial.model || '',
          dimensionality: initial.dimensionality ?? (kind === 'Embedding' ? 1536 : ''),
          healthCheckUrl: initial.healthCheckUrl || '/',
          healthCheckMethod: initial.healthCheckMethod || 'GET',
          healthCheckIntervalMs: initial.healthCheckIntervalMs ?? 5000,
          healthCheckExpectedStatusCode: initial.healthCheckExpectedStatusCode ?? 200,
          healthCheckUseAuth: initial.healthCheckUseAuth ?? false,
          active: initial.active !== false
        }
      : emptyForm(kind)
  );
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState(null);
  const set = (k, v) => setForm((f) => ({ ...f, [k]: v }));

  const changeFormat = (fmt) => {
    const d = FORMAT_DEFAULTS[fmt] || {};
    setForm((f) => ({ ...f, apiFormat: fmt, ...d }));
  };

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await onSubmit({
        name: form.name,
        kind,
        apiFormat: form.apiFormat,
        hostname: form.hostname,
        port: Number(form.port),
        useSsl: form.useSsl,
        apiKey: form.apiKey || undefined,
        model: form.model,
        dimensionality: kind === 'Embedding' ? Number(form.dimensionality) || undefined : undefined,
        healthCheckUrl: form.healthCheckUrl,
        healthCheckMethod: form.healthCheckMethod,
        healthCheckIntervalMs: Number(form.healthCheckIntervalMs),
        healthCheckExpectedStatusCode: Number(form.healthCheckExpectedStatusCode),
        healthCheckUseAuth: form.healthCheckUseAuth,
        active: form.active
      });
      onClose();
    } catch (e2) {
      setErr(e2.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      isOpen
      onClose={onClose}
      title={initial ? t('common.edit') : kind === 'Embedding' ? t('endpoints.addEmbedding') : t('endpoints.addInference')}
      size="wide"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button className="btn-primary" onClick={submit} disabled={busy || !form.name || !form.hostname}>
            {t('common.save')}
          </button>
        </>
      }
    >
      <form onSubmit={submit}>
        {err && <div className="error-banner">{err}</div>}
        <div className="field">
          <label>{t('common.name')}</label>
          <input value={form.name} onChange={(e) => set('name', e.target.value)} required autoFocus />
        </div>
        <div className="field-row">
          <div className="field">
            <label>{t('endpoints.apiFormat')}</label>
            <select value={form.apiFormat} onChange={(e) => changeFormat(e.target.value)}>
              {API_FORMATS.map((f) => (
                <option key={f} value={f}>
                  {f}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label>{t('endpoints.model')}</label>
            <input value={form.model} onChange={(e) => set('model', e.target.value)} placeholder="nomic-embed-text" />
          </div>
          {kind === 'Embedding' && (
            <div className="field">
              <label>{t('endpoints.dimensionality')}</label>
              <input type="number" value={form.dimensionality} onChange={(e) => set('dimensionality', e.target.value)} />
            </div>
          )}
        </div>
        <div className="field-row">
          <div className="field">
            <label>{t('endpoints.hostname')}</label>
            <input value={form.hostname} onChange={(e) => set('hostname', e.target.value)} required />
          </div>
          <div className="field" style={{ maxWidth: 120 }}>
            <label>{t('endpoints.port')}</label>
            <input type="number" value={form.port} onChange={(e) => set('port', e.target.value)} />
          </div>
          <div className="field checkbox-field" style={{ maxWidth: 140, alignSelf: 'flex-end' }}>
            <input id="useSsl" type="checkbox" checked={form.useSsl} onChange={(e) => set('useSsl', e.target.checked)} />
            <label htmlFor="useSsl">{t('endpoints.useSsl')}</label>
          </div>
        </div>
        <div className="field">
          <label>{t('endpoints.apiKey')} ({t('common.optional')})</label>
          <input type="password" value={form.apiKey} onChange={(e) => set('apiKey', e.target.value)} autoComplete="new-password" />
        </div>

        <h3 style={{ margin: 'var(--spacing-md) 0 var(--spacing-sm)' }}>{t('endpoints.healthConfig')}</h3>
        <div className="field-row">
          <div className="field">
            <label>{t('endpoints.healthCheckUrl')}</label>
            <input value={form.healthCheckUrl} onChange={(e) => set('healthCheckUrl', e.target.value)} />
          </div>
          <div className="field" style={{ maxWidth: 120 }}>
            <label>{t('endpoints.healthCheckMethod')}</label>
            <select value={form.healthCheckMethod} onChange={(e) => set('healthCheckMethod', e.target.value)}>
              {HEALTH_METHODS.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
          </div>
          <div className="field" style={{ maxWidth: 120 }}>
            <label>{t('endpoints.healthCheckStatus')}</label>
            <input
              type="number"
              value={form.healthCheckExpectedStatusCode}
              onChange={(e) => set('healthCheckExpectedStatusCode', e.target.value)}
            />
          </div>
          <div className="field" style={{ maxWidth: 140 }}>
            <label>{t('endpoints.healthCheckInterval')}</label>
            <input
              type="number"
              value={form.healthCheckIntervalMs}
              onChange={(e) => set('healthCheckIntervalMs', e.target.value)}
            />
          </div>
        </div>
        <div className="field-row">
          <div className="field checkbox-field">
            <input
              id="hcAuth"
              type="checkbox"
              checked={form.healthCheckUseAuth}
              onChange={(e) => set('healthCheckUseAuth', e.target.checked)}
            />
            <label htmlFor="hcAuth">{t('endpoints.healthCheckAuth')}</label>
          </div>
          <div className="field checkbox-field">
            <input id="active" type="checkbox" checked={form.active} onChange={(e) => set('active', e.target.checked)} />
            <label htmlFor="active">{t('endpoints.active')}</label>
          </div>
        </div>
      </form>
    </Modal>
  );
}

function EndpointsView({ kind }) {
  const { t, i18n } = useTranslation();
  const { apiClient, tenantId } = useAuth();
  const { addToast } = useApp();

  const [endpoints, setEndpoints] = useState([]);
  const [healthById, setHealthById] = useState({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState(null);
  const [jsonItem, setJsonItem] = useState(null);
  const [testItem, setTestItem] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const historyRef = useRef({}); // id -> [bool,...] rolling client-side history

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.listEndpoints(tenantId, kind, { maxResults: 1000 });
      setEndpoints(res.items || []);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient, tenantId, kind]);

  const pollHealth = useCallback(async () => {
    try {
      const res = await apiClient.endpointHealth(tenantId);
      const map = {};
      (res.endpoints || []).forEach((ep) => {
        map[ep.id] = ep;
        const prev = historyRef.current[ep.id] || [];
        historyRef.current[ep.id] = [...prev, Boolean(ep.status?.isHealthy)].slice(-24);
      });
      setHealthById(map);
    } catch {
      // health is supplementary
    }
  }, [apiClient, tenantId]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    pollHealth();
    const id = setInterval(pollHealth, 15000);
    return () => clearInterval(id);
  }, [pollHealth]);

  const handleSubmit = async (body) => {
    if (editing?.id || editing?.Id) {
      await apiClient.updateEndpoint(tenantId, editing.id || editing.Id, body);
      addToast('Endpoint updated', 'success');
    } else {
      await apiClient.createEndpoint(tenantId, body);
      addToast('Endpoint created', 'success');
    }
    load();
  };

  const handleDelete = async () => {
    await apiClient.deleteEndpoint(tenantId, deleteTarget.id || deleteTarget.Id);
    addToast('Endpoint deleted', 'success');
    setDeleteTarget(null);
    load();
  };

  const runTest = async (ep) => {
    setTestItem({ endpoint: ep, loading: true });
    await pollHealth();
    const health = healthById[ep.id || ep.Id];
    setTestItem({ endpoint: ep, loading: false, health });
  };

  const healthTone = (ep) => {
    if (ep.active === false) return { tone: 'neutral', label: t('endpoints.inactive') };
    const h = healthById[ep.id || ep.Id];
    if (!h) return { tone: 'warning', label: t('endpoints.awaiting') };
    return h.status?.isHealthy
      ? { tone: 'success', label: t('endpoints.healthy') }
      : { tone: 'danger', label: t('endpoints.unhealthy') };
  };

  const columns = [
    {
      key: 'id',
      label: t('common.id'),
      pinned: true,
      cellClass: 'cell-id',
      render: (e) => <CopyableId value={e.id || e.Id} />
    },
    { key: 'name', label: t('common.name'), pinned: true },
    { key: 'apiFormat', label: t('endpoints.apiFormat'), render: (e) => <StatusBadge tone="info">{e.apiFormat}</StatusBadge> },
    { key: 'model', label: t('endpoints.model'), cellClass: 'cell-mono', render: (e) => e.model || '—' },
    {
      key: 'endpoint',
      label: 'Endpoint',
      sortable: false,
      cellClass: 'cell-mono',
      render: (e) => `${e.useSsl ? 'https' : 'http'}://${e.hostname}:${e.port}`
    },
    {
      key: 'health',
      label: t('endpoints.health'),
      sortable: false,
      render: (e) => {
        const { tone, label } = healthTone(e);
        const hist = historyRef.current[e.id || e.Id] || [];
        return (
          <span style={{ display: 'inline-flex', gap: '0.5rem', alignItems: 'center' }}>
            <StatusBadge tone={tone}>{label}</StatusBadge>
            {hist.length > 0 && <HealthHistogram history={hist} />}
          </span>
        );
      }
    },
    {
      key: 'active',
      label: t('endpoints.active'),
      render: (e) => <StatusBadge tone={e.active !== false ? 'success' : 'neutral'}>{e.active !== false ? t('common.yes') : t('common.no')}</StatusBadge>
    },
    {
      key: 'actions',
      label: t('common.actions'),
      pinned: true,
      isAction: true,
      sortable: false,
      width: '60px',
      render: (e) => (
        <ActionMenu
          actions={[
            { label: t('endpoints.test'), onClick: () => runTest(e) },
            { label: t('common.edit'), onClick: () => { setEditing(e); setShowForm(true); } },
            { label: t('common.viewJson'), onClick: () => setJsonItem(e) },
            { divider: true },
            { label: t('common.delete'), danger: true, onClick: () => setDeleteTarget(e) }
          ]}
        />
      )
    }
  ];

  const title = kind === 'Embedding' ? t('endpoints.embeddingTitle') : t('endpoints.inferenceTitle');
  const subtitle = kind === 'Embedding' ? t('endpoints.embeddingSubtitle') : t('endpoints.inferenceSubtitle');
  const addLabel = kind === 'Embedding' ? t('endpoints.addEmbedding') : t('endpoints.addInference');

  return (
    <>
      <PageHeader
        title={title}
        subtitle={subtitle}
        actions={
          <button className="btn-primary" onClick={() => { setEditing(null); setShowForm(true); }}>
            + {addLabel}
          </button>
        }
      />
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <div className="notice-banner">{t('endpoints.pollNote')}</div>

      <DataTable
        tableId={`endpoints-${kind}`}
        columns={columns}
        data={endpoints}
        loading={loading}
        onRefresh={() => { load(); pollHealth(); }}
        onRowClick={(e) => { setEditing(e); setShowForm(true); }}
        emptyMessage={t('endpoints.empty')}
      />

      {showForm && (
        <EndpointForm kind={kind} initial={editing} t={t} onSubmit={handleSubmit} onClose={() => setShowForm(false)} />
      )}

      {jsonItem && (
        <Modal isOpen onClose={() => setJsonItem(null)} title={`${jsonItem.name} · JSON`} size="wide">
          <CodeViewer value={jsonItem} />
        </Modal>
      )}

      {testItem && (
        <Modal isOpen onClose={() => setTestItem(null)} title={`${t('endpoints.test')}: ${testItem.endpoint.name}`}>
          {testItem.loading ? (
            <div className="state-block"><div className="spinner" /></div>
          ) : testItem.health ? (
            <dl className="kv-grid">
              <dt>{t('endpoints.health')}</dt>
              <dd>
                <StatusBadge tone={testItem.health.status?.isHealthy ? 'success' : 'danger'}>
                  {testItem.health.status?.isHealthy ? t('endpoints.healthy') : t('endpoints.unhealthy')}
                </StatusBadge>
              </dd>
              <dt>Base URL</dt>
              <dd className="cell-mono">{testItem.health.baseUrl || '—'}</dd>
              <dt>{t('endpoints.lastCheck')}</dt>
              <dd>{formatDateTime(testItem.health.status?.lastCheckUtc, i18n.language)}</dd>
              <dt>Last status code</dt>
              <dd>{testItem.health.status?.lastStatusCode ?? '—'}</dd>
              <dt>Consecutive success / fail</dt>
              <dd>
                {testItem.health.status?.consecutiveSuccesses ?? 0} / {testItem.health.status?.consecutiveFailures ?? 0}
              </dd>
              {testItem.health.status?.lastError && (
                <>
                  <dt>{t('endpoints.lastError')}</dt>
                  <dd style={{ color: 'var(--color-danger)' }}>{testItem.health.status.lastError}</dd>
                </>
              )}
            </dl>
          ) : (
            <p className="page-subtitle">{t('endpoints.awaiting')}.</p>
          )}
        </Modal>
      )}

      <ConfirmModal
        isOpen={Boolean(deleteTarget)}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        message={t('confirm.deleteBody', { name: deleteTarget?.name })}
      />
    </>
  );
}

export default EndpointsView;
