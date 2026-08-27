import { useState, useEffect, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import FilterBar from '../components/FilterBar';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import ActionMenu from '../components/ActionMenu';
import CopyableId from '../components/CopyableId';
import CodeViewer from '../components/CodeViewer';
import StatusBadge from '../components/StatusBadge';
import { ErrorBanner } from '../components/States';
import { MEMORY_TYPES } from '../utils/constants';
import { formatDateTime } from '../i18n/formatters';

const EMPTY = { categoryId: '', slug: '', title: '', summary: '', body: '', type: 'Project', tags: '' };

function MemoryForm({ initial, categories, onSubmit, onClose, t }) {
  const [form, setForm] = useState(
    initial
      ? {
          categoryId: initial.categoryId || '',
          slug: initial.slug || '',
          title: initial.title || '',
          summary: initial.summary || '',
          body: initial.body || '',
          type: initial.type || 'Project',
          tags: Array.isArray(initial.tags) ? initial.tags.join(', ') : ''
        }
      : EMPTY
  );
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState(null);
  const set = (k, v) => setForm((f) => ({ ...f, [k]: v }));

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await onSubmit({
        categoryId: form.categoryId,
        slug: form.slug,
        title: form.title,
        summary: form.summary,
        body: form.body,
        type: form.type,
        tags: form.tags
          .split(',')
          .map((s) => s.trim())
          .filter(Boolean)
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
      title={initial ? t('common.edit') : t('memories.addMemory')}
      size="wide"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button className="btn-primary" onClick={submit} disabled={busy || !form.slug || !form.categoryId}>
            {t('common.save')}
          </button>
        </>
      }
    >
      <form onSubmit={submit}>
        {err && <div className="error-banner">{err}</div>}
        <div className="notice-banner">{t('memories.upsertHint')}</div>
        <div className="field-row">
          <div className="field">
            <label>{t('memories.category')}</label>
            <select value={form.categoryId} onChange={(e) => set('categoryId', e.target.value)} required>
              <option value="">—</option>
              {categories.map((c) => (
                <option key={c.id || c.Id} value={c.id || c.Id}>
                  {c.name}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label>{t('memories.slug')}</label>
            <input value={form.slug} onChange={(e) => set('slug', e.target.value)} required placeholder="filesystem-layout" />
          </div>
          <div className="field">
            <label>{t('common.type')}</label>
            <select value={form.type} onChange={(e) => set('type', e.target.value)}>
              {MEMORY_TYPES.map((ty) => (
                <option key={ty} value={ty}>
                  {ty}
                </option>
              ))}
            </select>
          </div>
        </div>
        <div className="field">
          <label>{t('memories.titleField')}</label>
          <input value={form.title} onChange={(e) => set('title', e.target.value)} />
        </div>
        <div className="field">
          <label>{t('memories.summary')}</label>
          <textarea value={form.summary} onChange={(e) => set('summary', e.target.value)} rows={2} />
        </div>
        <div className="field">
          <label>{t('memories.body')}</label>
          <textarea value={form.body} onChange={(e) => set('body', e.target.value)} rows={6} />
        </div>
        <div className="field">
          <label>{t('memories.tags')}</label>
          <input value={form.tags} onChange={(e) => set('tags', e.target.value)} placeholder="comma, separated, tags" />
        </div>
      </form>
    </Modal>
  );
}

function MemoriesView() {
  const { t, i18n } = useTranslation();
  const { scopeId } = useParams();
  const { apiClient, tenantId } = useAuth();
  const { addToast } = useApp();

  const [memories, setMemories] = useState([]);
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [filters, setFilters] = useState({ category: '', maxResults: 100 });
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState(null);
  const [viewItem, setViewItem] = useState(null);
  const [jsonItem, setJsonItem] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);

  const load = useCallback(
    async (applied = filters) => {
      setLoading(true);
      setError(null);
      try {
        const res = await apiClient.listMemories(tenantId, scopeId, {
          category: applied.category || undefined,
          maxResults: applied.maxResults || 100
        });
        setMemories(res.items || []);
      } catch (err) {
        setError(err.message);
      } finally {
        setLoading(false);
      }
    },
    [apiClient, tenantId, scopeId, filters]
  );

  const loadCategories = useCallback(async () => {
    try {
      const res = await apiClient.listCategories(tenantId, scopeId, { maxResults: 1000 });
      setCategories(res.items || []);
    } catch {
      setCategories([]);
    }
  }, [apiClient, tenantId, scopeId]);

  useEffect(() => {
    load();
    loadCategories();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scopeId]);

  const openView = async (mem) => {
    // Fetch full body (list may omit it).
    try {
      const full = await apiClient.getMemory(tenantId, scopeId, mem.id || mem.Id);
      setViewItem(full);
    } catch {
      setViewItem(mem);
    }
  };

  const handleSubmit = async (body) => {
    await apiClient.upsertMemory(tenantId, scopeId, body);
    addToast('Memory upserted', 'success');
    load();
  };

  const handleDelete = async () => {
    await apiClient.deleteMemory(tenantId, scopeId, deleteTarget.id || deleteTarget.Id);
    addToast('Memory deleted', 'success');
    setDeleteTarget(null);
    load();
  };

  const categoryName = (id) => categories.find((c) => (c.id || c.Id) === id)?.name || id || '—';

  const columns = [
    {
      key: 'id',
      label: t('common.id'),
      pinned: true,
      cellClass: 'cell-id',
      render: (m) => <CopyableId value={m.id || m.Id} />
    },
    { key: 'slug', label: t('memories.slug'), pinned: true, cellClass: 'cell-mono' },
    { key: 'title', label: t('memories.titleField'), render: (m) => m.title || '—' },
    { key: 'type', label: t('common.type'), render: (m) => <StatusBadge tone="info">{m.type || '—'}</StatusBadge> },
    { key: 'categoryId', label: t('memories.category'), render: (m) => categoryName(m.categoryId) },
    {
      key: 'updatedUtc',
      label: t('common.updated'),
      render: (m) => formatDateTime(m.updatedUtc || m.createdUtc, i18n.language)
    },
    {
      key: 'actions',
      label: t('common.actions'),
      pinned: true,
      isAction: true,
      sortable: false,
      width: '60px',
      render: (m) => (
        <ActionMenu
          actions={[
            { label: t('common.view'), onClick: () => openView(m) },
            { label: t('common.edit'), onClick: () => { setEditing(m); setShowForm(true); } },
            { label: t('common.viewJson'), onClick: () => setJsonItem(m) },
            { divider: true },
            { label: t('common.delete'), danger: true, onClick: () => setDeleteTarget(m) }
          ]}
        />
      )
    }
  ];

  return (
    <>
      <PageHeader
        title={t('memories.title')}
        subtitle={t('memories.subtitle')}
        breadcrumbs={
          <>
            <Link to="/dashboard/scopes">{t('scopes.title')}</Link> /{' '}
            <Link to={`/dashboard/scopes/${scopeId}`}>{scopeId}</Link> / {t('memories.title')}
          </>
        }
        actions={
          <button className="btn-primary" onClick={() => { setEditing(null); setShowForm(true); }}>
            + {t('memories.addMemory')}
          </button>
        }
      />
      {error && <ErrorBanner message={error} onRetry={() => load()} onDismiss={() => setError(null)} />}

      <FilterBar
        fields={[
          {
            name: 'category',
            label: t('memories.filterCategory'),
            type: 'select',
            options: [{ value: '', label: t('common.none') }, ...categories.map((c) => ({ value: c.id || c.Id, label: c.name }))]
          },
          { name: 'maxResults', label: t('memories.maxResults'), type: 'number', placeholder: '100' }
        ]}
        values={filters}
        onChange={(k, v) => setFilters((f) => ({ ...f, [k]: v }))}
        onApply={() => load()}
        onClear={() => {
          const reset = { category: '', maxResults: 100 };
          setFilters(reset);
          load(reset);
        }}
      />

      <DataTable
        tableId="memories"
        columns={columns}
        data={memories}
        loading={loading}
        onRefresh={() => load()}
        onRowClick={openView}
        emptyMessage={t('memories.empty')}
      />

      {showForm && (
        <MemoryForm
          initial={editing}
          categories={categories}
          t={t}
          onSubmit={handleSubmit}
          onClose={() => setShowForm(false)}
        />
      )}

      {viewItem && (
        <Modal isOpen onClose={() => setViewItem(null)} title={viewItem.title || viewItem.slug} size="wide">
          <dl className="kv-grid">
            <dt>{t('common.id')}</dt>
            <dd><CopyableId value={viewItem.id || viewItem.Id} /></dd>
            <dt>{t('memories.slug')}</dt>
            <dd className="cell-mono">{viewItem.slug}</dd>
            <dt>{t('common.type')}</dt>
            <dd>{viewItem.type || '—'}</dd>
            <dt>{t('memories.category')}</dt>
            <dd>{categoryName(viewItem.categoryId)}</dd>
            <dt>{t('memories.summary')}</dt>
            <dd>{viewItem.summary || '—'}</dd>
            <dt>{t('memories.tags')}</dt>
            <dd>{Array.isArray(viewItem.tags) && viewItem.tags.length ? viewItem.tags.join(', ') : '—'}</dd>
          </dl>
          <div style={{ marginTop: 'var(--spacing-md)' }}>
            <div className="kpi-label" style={{ marginBottom: 4 }}>{t('memories.body')}</div>
            <CodeViewer value={viewItem.body || '(empty)'} language="text" maxHeight={280} />
          </div>
        </Modal>
      )}

      {jsonItem && (
        <Modal isOpen onClose={() => setJsonItem(null)} title={`${jsonItem.slug} · JSON`} size="wide">
          <CodeViewer value={jsonItem} />
        </Modal>
      )}

      <ConfirmModal
        isOpen={Boolean(deleteTarget)}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        message={t('confirm.deleteBody', { name: deleteTarget?.slug })}
      />
    </>
  );
}

export default MemoriesView;
