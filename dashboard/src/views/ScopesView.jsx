import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
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
import { ErrorBanner } from '../components/States';
import { STORE_PROVIDERS, FILESYSTEM_LAYOUTS, FILESYSTEM_LAYOUT_LABELS } from '../utils/constants';

const EMPTY = {
  name: '',
  description: '',
  storeProvider: 'RecallDb',
  filesystemLayout: 'SingleFile',
  targetPath: '',
  dimensionality: 1536,
  recallCollectionId: '',
  embeddingEndpointId: ''
};

function ScopeForm({ initial, onSubmit, onClose, endpoints, t }) {
  const [form, setForm] = useState(initial || EMPTY);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState(null);
  const set = (k, v) => setForm((f) => ({ ...f, [k]: v }));
  const editing = Boolean(initial?.id || initial?.Id);

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await onSubmit({
        name: form.name,
        description: form.description,
        storeProvider: form.storeProvider,
        filesystemLayout: form.storeProvider === 'Filesystem' ? form.filesystemLayout : undefined,
        targetPath: form.storeProvider === 'Filesystem' ? form.targetPath : undefined,
        dimensionality: Number(form.dimensionality) || undefined,
        recallCollectionId: form.recallCollectionId || undefined,
        embeddingEndpointId: form.embeddingEndpointId || undefined
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
      title={editing ? t('common.edit') : t('scopes.addScope')}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button className="btn-primary" onClick={submit} disabled={busy || !form.name}>
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
        <div className="field">
          <label>{t('common.description')}</label>
          <textarea value={form.description} onChange={(e) => set('description', e.target.value)} rows={2} />
        </div>
        <div className="field-row">
          <div className="field">
            <label>{t('scopes.storeProvider')}</label>
            <select value={form.storeProvider} onChange={(e) => set('storeProvider', e.target.value)} disabled={editing}>
              {STORE_PROVIDERS.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label>{t('scopes.dimensionality')}</label>
            <input
              type="number"
              value={form.dimensionality}
              onChange={(e) => set('dimensionality', e.target.value)}
              disabled={editing}
            />
          </div>
        </div>
        {form.storeProvider === 'Filesystem' && (
          <div className="field-row">
            <div className="field">
              <label>{t('scopes.layout')}</label>
              <select value={form.filesystemLayout} onChange={(e) => set('filesystemLayout', e.target.value)}>
                {FILESYSTEM_LAYOUTS.map((l) => (
                  <option key={l} value={l}>
                    {FILESYSTEM_LAYOUT_LABELS[l] || l}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label>{t('scopes.targetPath')}</label>
              <input value={form.targetPath} onChange={(e) => set('targetPath', e.target.value)} placeholder="/data/memory" />
            </div>
          </div>
        )}
        {form.storeProvider === 'RecallDb' && (
          <div className="field">
            <label>{t('scopes.recallCollection')}</label>
            <input
              value={form.recallCollectionId}
              onChange={(e) => set('recallCollectionId', e.target.value)}
              placeholder="col_… (leave blank to auto-provision)"
            />
          </div>
        )}
        <div className="field">
          <label>{t('scopes.embeddingEndpoint')}</label>
          <select value={form.embeddingEndpointId} onChange={(e) => set('embeddingEndpointId', e.target.value)}>
            <option value="">{t('common.none')}</option>
            {endpoints.map((ep) => (
              <option key={ep.id || ep.Id} value={ep.id || ep.Id}>
                {ep.name || ep.id} ({ep.model})
              </option>
            ))}
          </select>
          <div className="field-hint">{t('scopes.dimensionLocked')}</div>
        </div>
      </form>
    </Modal>
  );
}

function ScopesView() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { apiClient, tenantId } = useAuth();
  const { addToast } = useApp();

  const [scopes, setScopes] = useState([]);
  const [endpoints, setEndpoints] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [editing, setEditing] = useState(null); // scope object or EMPTY sentinel
  const [showForm, setShowForm] = useState(false);
  const [jsonScope, setJsonScope] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.listScopes(tenantId, { maxResults: 1000 });
      setScopes(res.items || []);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
    try {
      const eps = await apiClient.listEndpoints(tenantId, 'Embedding', { maxResults: 1000 });
      setEndpoints(eps.items || []);
    } catch {
      setEndpoints([]);
    }
  }, [apiClient, tenantId]);

  useEffect(() => {
    load();
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    setShowForm(true);
  };
  const openEdit = (scope) => {
    setEditing(scope);
    setShowForm(true);
  };

  const handleSubmit = async (body) => {
    if (editing?.id || editing?.Id) {
      await apiClient.updateScope(tenantId, editing.id || editing.Id, body);
      addToast('Scope updated', 'success');
    } else {
      await apiClient.createScope(tenantId, body);
      addToast('Scope created', 'success');
    }
    load();
  };

  const handleDelete = async () => {
    await apiClient.deleteScope(tenantId, deleteTarget.id || deleteTarget.Id);
    addToast('Scope deleted', 'success');
    setDeleteTarget(null);
    load();
  };

  const columns = [
    {
      key: 'id',
      label: t('common.id'),
      pinned: true,
      cellClass: 'cell-id',
      render: (s) => <CopyableId value={s.id || s.Id} />
    },
    { key: 'name', label: t('common.name'), pinned: true, render: (s) => s.name || '—' },
    {
      key: 'storeProvider',
      label: t('scopes.storeProvider'),
      render: (s) => <StatusBadge tone="info">{s.storeProvider || '—'}</StatusBadge>
    },
    { key: 'dimensionality', label: t('scopes.dimensionality'), numeric: true, render: (s) => s.dimensionality ?? '—' },
    {
      key: 'recallCollectionId',
      label: t('scopes.recallCollection'),
      cellClass: 'cell-mono',
      render: (s) => (s.recallCollectionId ? <CopyableId value={s.recallCollectionId} /> : '—')
    },
    {
      key: 'actions',
      label: t('common.actions'),
      pinned: true,
      sortable: false,
      isAction: true,
      width: '60px',
      render: (s) => (
        <ActionMenu
          actions={[
            { label: t('scopes.detailTitle'), onClick: () => navigate(`/dashboard/scopes/${s.id || s.Id}`) },
            { label: t('scopes.openCategories'), onClick: () => navigate(`/dashboard/scopes/${s.id || s.Id}/categories`) },
            { label: t('scopes.openMemories'), onClick: () => navigate(`/dashboard/scopes/${s.id || s.Id}/memories`) },
            { label: t('scopes.openChat'), onClick: () => navigate(`/dashboard/scopes/${s.id || s.Id}/chat`) },
            { divider: true },
            { label: t('common.edit'), onClick: () => openEdit(s) },
            { label: t('common.duplicate'), onClick: () => { setEditing({ ...s, id: undefined, Id: undefined, name: `${s.name || ''} (copy)` }); setShowForm(true); } },
            { label: t('common.viewJson'), onClick: () => setJsonScope(s) },
            { divider: true },
            { label: t('common.delete'), danger: true, onClick: () => setDeleteTarget(s) }
          ]}
        />
      )
    }
  ];

  return (
    <>
      <PageHeader
        title={t('scopes.title')}
        subtitle={t('scopes.subtitle')}
        actions={
          <button className="btn-primary" onClick={openCreate}>
            + {t('scopes.addScope')}
          </button>
        }
      />
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable
        tableId="scopes"
        columns={columns}
        data={scopes}
        loading={loading}
        onRefresh={load}
        onRowClick={(s) => openEdit(s)}
        emptyMessage={t('scopes.empty')}
      />

      {showForm && (
        <ScopeForm
          initial={editing}
          endpoints={endpoints}
          t={t}
          onSubmit={handleSubmit}
          onClose={() => setShowForm(false)}
        />
      )}

      {jsonScope && (
        <Modal isOpen onClose={() => setJsonScope(null)} title={`${jsonScope.name || jsonScope.id} · JSON`} size="wide">
          <CodeViewer value={jsonScope} />
        </Modal>
      )}

      <ConfirmModal
        isOpen={Boolean(deleteTarget)}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        message={t('confirm.deleteBody', { name: deleteTarget?.name || deleteTarget?.id })}
      />
    </>
  );
}

export default ScopesView;
