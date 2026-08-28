import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import DataTable from './DataTable';
import Modal from './Modal';
import ConfirmModal from './ConfirmModal';
import ActionMenu from './ActionMenu';
import CopyableId from './CopyableId';
import CodeViewer from './CodeViewer';
import { ErrorBanner } from './States';

function CategoryForm({ initial, onSubmit, onClose, t }) {
  const [form, setForm] = useState(initial || { name: '', description: '', instructions: '' });
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState(null);
  const set = (k, v) => setForm((f) => ({ ...f, [k]: v }));
  const editing = Boolean(initial?.id || initial?.Id);

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await onSubmit({ name: form.name, description: form.description, instructions: form.instructions });
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
      title={editing ? t('common.edit') : t('categories.addCategory')}
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
        <div className="field">
          <label>{t('categories.instructions')}</label>
          <textarea
            value={form.instructions}
            onChange={(e) => set('instructions', e.target.value)}
            rows={5}
            placeholder={t('categories.instructionsPlaceholder')}
          />
        </div>
      </form>
    </Modal>
  );
}

/**
 * Self-contained category management for one (tenant, scope): a toolbar with an add
 * action, a DataTable with row actions, and create/edit/view/JSON/delete modals.
 * Rendered by both the scope drill-in view and the top-level Memories browser.
 */
function CategoriesPanel({ tenantId, scopeId, onCountChange }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const { addToast } = useApp();

  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState(null);
  const [jsonItem, setJsonItem] = useState(null);
  const [viewItem, setViewItem] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);

  const load = useCallback(async () => {
    if (!scopeId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.listCategories(tenantId, scopeId, { maxResults: 1000 });
      setCategories(res.items || []);
      onCountChange?.(res.items ? res.items.length : 0);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient, tenantId, scopeId, onCountChange]);

  useEffect(() => {
    load();
  }, [load]);

  const handleSubmit = async (body) => {
    if (editing?.id || editing?.Id) {
      await apiClient.updateCategory(tenantId, scopeId, editing.id || editing.Id, body);
      addToast(t('categories.updated'), 'success');
    } else {
      await apiClient.createCategory(tenantId, scopeId, body);
      addToast(t('categories.created'), 'success');
    }
    load();
  };

  const handleDelete = async () => {
    await apiClient.deleteCategory(tenantId, scopeId, deleteTarget.id || deleteTarget.Id);
    addToast(t('categories.deleted'), 'success');
    setDeleteTarget(null);
    load();
  };

  const columns = [
    {
      key: 'id',
      label: t('common.id'),
      pinned: true,
      cellClass: 'cell-id',
      render: (c) => <CopyableId value={c.id || c.Id} />
    },
    { key: 'name', label: t('common.name'), pinned: true },
    { key: 'description', label: t('common.description'), render: (c) => c.description || '—' },
    {
      key: 'instructions',
      label: t('categories.instructions'),
      sortable: false,
      render: (c) => (c.instructions ? `${c.instructions.slice(0, 60)}${c.instructions.length > 60 ? '…' : ''}` : '—')
    },
    {
      key: 'actions',
      label: t('common.actions'),
      pinned: true,
      isAction: true,
      sortable: false,
      width: '60px',
      render: (c) => (
        <ActionMenu
          actions={[
            { label: t('common.view'), onClick: () => setViewItem(c) },
            { label: t('common.edit'), onClick: () => { setEditing(c); setShowForm(true); } },
            { label: t('common.duplicate'), onClick: () => { setEditing({ ...c, id: undefined, Id: undefined, name: `${c.name} (copy)` }); setShowForm(true); } },
            { label: t('common.viewJson'), onClick: () => setJsonItem(c) },
            { divider: true },
            { label: t('common.delete'), danger: true, onClick: () => setDeleteTarget(c) }
          ]}
        />
      )
    }
  ];

  return (
    <>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable
        tableId="categories"
        columns={columns}
        data={categories}
        loading={loading}
        onRefresh={load}
        onRowClick={(c) => { setEditing(c); setShowForm(true); }}
        emptyMessage={t('categories.empty')}
        toolbarLeft={
          <button className="btn-primary btn-sm" onClick={() => { setEditing(null); setShowForm(true); }}>
            + {t('categories.addCategory')}
          </button>
        }
      />

      {showForm && (
        <CategoryForm initial={editing} t={t} onSubmit={handleSubmit} onClose={() => setShowForm(false)} />
      )}

      {viewItem && (
        <Modal isOpen onClose={() => setViewItem(null)} title={viewItem.name}>
          <dl className="kv-grid">
            <dt>{t('common.id')}</dt>
            <dd><CopyableId value={viewItem.id || viewItem.Id} /></dd>
            <dt>{t('common.description')}</dt>
            <dd>{viewItem.description || '—'}</dd>
          </dl>
          <div style={{ marginTop: 'var(--spacing-md)' }}>
            <div className="kpi-label" style={{ marginBottom: 4 }}>{t('categories.instructions')}</div>
            <CodeViewer value={viewItem.instructions || '(none)'} language="text" maxHeight={220} />
          </div>
        </Modal>
      )}

      {jsonItem && (
        <Modal isOpen onClose={() => setJsonItem(null)} title={`${jsonItem.name} · JSON`} size="wide">
          <CodeViewer value={jsonItem} />
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

export default CategoriesPanel;
