import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import ActionMenu from '../components/ActionMenu';
import StatusBadge from '../components/StatusBadge';
import { EmptyState, ErrorBanner } from '../components/States';

function InstructionForm({ initial, onSubmit, onClose, t }) {
  const [form, setForm] = useState(
    initial || { name: '', content: '', position: 0, active: true }
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
        name: form.name,
        content: form.content,
        position: Number(form.position) || 0,
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
      title={initial ? t('instructions.editInstruction') : t('instructions.addInstruction')}
      size="wide"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button className="btn-primary" onClick={submit} disabled={busy || !form.name}>{t('common.save')}</button>
        </>
      }
    >
      <form onSubmit={submit}>
        {err && <div className="error-banner">{err}</div>}
        <div className="field-row">
          <div className="field" style={{ flex: 1 }}>
            <label>{t('common.name')}</label>
            <input value={form.name} onChange={(e) => set('name', e.target.value)} required autoFocus />
          </div>
          <div className="field" style={{ maxWidth: 120 }}>
            <label>{t('instructions.position')}</label>
            <input type="number" value={form.position} onChange={(e) => set('position', e.target.value)} />
          </div>
        </div>
        <div className="field">
          <label>{t('instructions.content')}</label>
          <textarea value={form.content} onChange={(e) => set('content', e.target.value)} rows={10} placeholder={t('instructions.contentPlaceholder')} />
        </div>
        <label className="check-row">
          <input type="checkbox" checked={form.active} onChange={(e) => set('active', e.target.checked)} />
          {t('instructions.active')}
        </label>
      </form>
    </Modal>
  );
}

function InstructionsView() {
  const { t } = useTranslation();
  const { apiClient, tenantId, isAdmin, isTenantAdmin } = useAuth();
  const { addToast } = useApp();

  const canManage = isAdmin || isTenantAdmin;
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.listInstructions(tenantId, { maxResults: 1000 });
      setItems(res.items || []);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient, tenantId]);

  useEffect(() => {
    load();
  }, [load]);

  const handleSubmit = async (body) => {
    if (editing?.id) {
      await apiClient.updateInstruction(tenantId, editing.id, body);
      addToast(t('instructions.updated'), 'success');
    } else {
      await apiClient.createInstruction(tenantId, body);
      addToast(t('instructions.created'), 'success');
    }
    load();
  };

  const handleDelete = async () => {
    await apiClient.deleteInstruction(tenantId, deleteTarget.id);
    addToast(t('instructions.deleted'), 'success');
    setDeleteTarget(null);
    load();
  };

  const openEdit = (x) => {
    if (!canManage) return;
    setEditing(x);
    setShowForm(true);
  };

  const columns = [
    { key: 'position', label: t('instructions.position'), numeric: true, width: '80px', render: (x) => x.position ?? 0 },
    { key: 'name', label: t('common.name'), pinned: true },
    {
      key: 'content',
      label: t('instructions.content'),
      sortable: false,
      render: (x) => <span className="cell-truncate">{(x.content || '').slice(0, 140) || '—'}</span>
    },
    { key: 'active', label: t('instructions.active'), render: (x) => <StatusBadge tone={x.active ? 'success' : 'danger'}>{x.active ? t('common.yes') : t('common.no')}</StatusBadge> },
    {
      key: 'actions',
      label: t('common.actions'),
      pinned: true,
      isAction: true,
      sortable: false,
      width: '60px',
      render: (x) => (
        <ActionMenu
          actions={[
            { label: t('common.edit'), onClick: () => openEdit(x) },
            { label: t('common.duplicate'), onClick: () => { setEditing({ ...x, id: undefined, name: `${x.name} (copy)` }); setShowForm(true); } },
            { divider: true },
            { label: t('common.delete'), danger: true, onClick: () => setDeleteTarget(x) }
          ]}
        />
      )
    }
  ];

  return (
    <>
      <PageHeader
        title={t('instructions.title')}
        subtitle={t('instructions.subtitle')}
        actions={
          canManage ? (
            <button className="btn-primary" onClick={() => { setEditing(null); setShowForm(true); }}>
              + {t('instructions.addInstruction')}
            </button>
          ) : null
        }
      />
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      {!loading && items.length === 0 && !canManage ? (
        <EmptyState title={t('instructions.title')} message={t('instructions.emptyReadonly')} />
      ) : (
        <DataTable
          tableId="instructions"
          columns={columns}
          data={items}
          loading={loading}
          onRefresh={load}
          onRowClick={canManage ? openEdit : null}
          emptyMessage={t('instructions.empty')}
        />
      )}

      {showForm && (
        <InstructionForm initial={editing} t={t} onSubmit={handleSubmit} onClose={() => setShowForm(false)} />
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

export default InstructionsView;
