import { useState, useEffect, useCallback } from 'react';
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
import { EmptyState, ErrorBanner } from '../components/States';

function TenantForm({ initial, onSubmit, onClose, t }) {
  const [form, setForm] = useState(initial || { name: '', description: '' });
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState(null);
  const set = (k, v) => setForm((f) => ({ ...f, [k]: v }));

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await onSubmit({ name: form.name, description: form.description });
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
      title={initial ? t('common.edit') : t('tenants.addTenant')}
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
      </form>
    </Modal>
  );
}

function TenantsView() {
  const { t } = useTranslation();
  const { apiClient, isAdmin } = useAuth();
  const { addToast } = useApp();

  const [tenants, setTenants] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState(null);
  const [jsonItem, setJsonItem] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.listTenants({ maxResults: 1000 });
      setTenants(res.items || []);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient]);

  useEffect(() => {
    if (isAdmin) load();
    else setLoading(false);
  }, [load, isAdmin]);

  const handleSubmit = async (body) => {
    if (editing?.id || editing?.Id) {
      await apiClient.updateTenant(editing.id || editing.Id, body);
      addToast('Tenant updated', 'success');
    } else {
      await apiClient.createTenant(body);
      addToast('Tenant created', 'success');
    }
    load();
  };

  const handleDelete = async () => {
    await apiClient.deleteTenant(deleteTarget.id || deleteTarget.Id);
    addToast('Tenant deleted', 'success');
    setDeleteTarget(null);
    load();
  };

  const columns = [
    {
      key: 'id',
      label: t('common.id'),
      pinned: true,
      cellClass: 'cell-id',
      render: (x) => <CopyableId value={x.id || x.Id} />
    },
    { key: 'name', label: t('common.name'), pinned: true },
    { key: 'description', label: t('common.description'), render: (x) => x.description || '—' },
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
            { label: t('common.edit'), onClick: () => { setEditing(x); setShowForm(true); } },
            { label: t('common.viewJson'), onClick: () => setJsonItem(x) },
            { divider: true },
            { label: t('common.delete'), danger: true, onClick: () => setDeleteTarget(x) }
          ]}
        />
      )
    }
  ];

  if (!isAdmin) {
    return (
      <>
        <PageHeader title={t('tenants.title')} subtitle={t('tenants.subtitle')} />
        <EmptyState title={t('tenants.title')} message={t('tenants.adminOnly')} />
      </>
    );
  }

  return (
    <>
      <PageHeader
        title={t('tenants.title')}
        subtitle={t('tenants.subtitle')}
        actions={
          <button className="btn-primary" onClick={() => { setEditing(null); setShowForm(true); }}>
            + {t('tenants.addTenant')}
          </button>
        }
      />
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable
        tableId="tenants"
        columns={columns}
        data={tenants}
        loading={loading}
        onRefresh={load}
        onRowClick={(x) => { setEditing(x); setShowForm(true); }}
        emptyMessage={t('tenants.empty')}
      />

      {showForm && <TenantForm initial={editing} t={t} onSubmit={handleSubmit} onClose={() => setShowForm(false)} />}

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

export default TenantsView;
