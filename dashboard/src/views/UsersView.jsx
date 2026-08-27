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
import StatusBadge from '../components/StatusBadge';
import { EmptyState, ErrorBanner } from '../components/States';

function roleFor(u, t) {
  if (u.isAdmin) return t('topbar.admin');
  if (u.isTenantAdmin) return t('topbar.tenantAdmin');
  return t('topbar.user');
}

function UserForm({ initial, canGrantAdmin, onSubmit, onClose, t }) {
  const editing = Boolean(initial);
  const [form, setForm] = useState(
    initial || { firstName: '', lastName: '', email: '', password: '', isAdmin: false, isTenantAdmin: false, active: true }
  );
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState(null);
  const set = (k, v) => setForm((f) => ({ ...f, [k]: v }));

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      const body = {
        firstName: form.firstName || null,
        lastName: form.lastName || null,
        email: form.email,
        isAdmin: form.isAdmin,
        isTenantAdmin: form.isTenantAdmin,
        active: form.active
      };
      if (form.password) body.password = form.password;
      await onSubmit(body);
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
      title={editing ? t('users.editUser') : t('users.addUser')}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button className="btn-primary" onClick={submit} disabled={busy || !form.email || (!editing && !form.password)}>
            {t('common.save')}
          </button>
        </>
      }
    >
      <form onSubmit={submit}>
        {err && <div className="error-banner">{err}</div>}
        <div className="field-row">
          <div className="field">
            <label>{t('users.firstName')}</label>
            <input value={form.firstName || ''} onChange={(e) => set('firstName', e.target.value)} autoFocus />
          </div>
          <div className="field">
            <label>{t('users.lastName')}</label>
            <input value={form.lastName || ''} onChange={(e) => set('lastName', e.target.value)} />
          </div>
        </div>
        <div className="field">
          <label>{t('users.email')}</label>
          <input type="email" value={form.email} onChange={(e) => set('email', e.target.value)} required />
        </div>
        <div className="field">
          <label>{editing ? t('users.passwordEdit') : t('users.password')}</label>
          <input
            type="password"
            value={form.password || ''}
            onChange={(e) => set('password', e.target.value)}
            placeholder={editing ? t('users.passwordUnchanged') : ''}
            autoComplete="new-password"
          />
        </div>
        <label className="check-row">
          <input type="checkbox" checked={form.isTenantAdmin} onChange={(e) => set('isTenantAdmin', e.target.checked)} />
          {t('users.isTenantAdmin')}
        </label>
        {canGrantAdmin && (
          <label className="check-row">
            <input type="checkbox" checked={form.isAdmin} onChange={(e) => set('isAdmin', e.target.checked)} />
            {t('users.isAdmin')}
          </label>
        )}
        <label className="check-row">
          <input type="checkbox" checked={form.active} onChange={(e) => set('active', e.target.checked)} />
          {t('users.active')}
        </label>
      </form>
    </Modal>
  );
}

function UsersView() {
  const { t } = useTranslation();
  const { apiClient, tenantId, isAdmin, isTenantAdmin } = useAuth();
  const { addToast } = useApp();

  const canManage = isAdmin || isTenantAdmin;
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.listUsers(tenantId, { maxResults: 1000 });
      setUsers(res.items || []);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient, tenantId]);

  useEffect(() => {
    if (canManage) load();
    else setLoading(false);
  }, [load, canManage]);

  const handleSubmit = async (body) => {
    if (editing?.id) {
      await apiClient.updateUser(tenantId, editing.id, body);
      addToast(t('users.updated'), 'success');
    } else {
      await apiClient.createUser(tenantId, body);
      addToast(t('users.created'), 'success');
    }
    load();
  };

  const handleDelete = async () => {
    await apiClient.deleteUser(tenantId, deleteTarget.id);
    addToast(t('users.deleted'), 'success');
    setDeleteTarget(null);
    load();
  };

  const columns = [
    { key: 'email', label: t('users.email'), pinned: true },
    {
      key: 'name',
      label: t('common.name'),
      render: (x) => [x.firstName, x.lastName].filter(Boolean).join(' ') || '—',
      sortValue: (x) => `${x.firstName || ''} ${x.lastName || ''}`
    },
    { key: 'role', label: t('topbar.role'), sortValue: (x) => roleFor(x, t), render: (x) => <StatusBadge tone="neutral">{roleFor(x, t)}</StatusBadge> },
    { key: 'active', label: t('users.active'), render: (x) => <StatusBadge tone={x.active ? 'success' : 'danger'}>{x.active ? t('common.yes') : t('common.no')}</StatusBadge> },
    { key: 'id', label: t('common.id'), cellClass: 'cell-id', render: (x) => <CopyableId value={x.id} /> },
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
            { divider: true },
            { label: t('common.delete'), danger: true, disabled: x.protected, onClick: () => setDeleteTarget(x) }
          ]}
        />
      )
    }
  ];

  if (!canManage) {
    return (
      <>
        <PageHeader title={t('users.title')} subtitle={t('users.subtitle')} />
        <EmptyState title={t('users.title')} message={t('users.adminOnly')} />
      </>
    );
  }

  return (
    <>
      <PageHeader
        title={t('users.title')}
        subtitle={t('users.subtitle')}
        actions={
          <button className="btn-primary" onClick={() => { setEditing(null); setShowForm(true); }}>
            + {t('users.addUser')}
          </button>
        }
      />
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable
        tableId="users"
        columns={columns}
        data={users}
        loading={loading}
        onRefresh={load}
        onRowClick={(x) => { setEditing(x); setShowForm(true); }}
        emptyMessage={t('users.empty')}
      />

      {showForm && (
        <UserForm
          initial={editing}
          canGrantAdmin={isAdmin}
          t={t}
          onSubmit={handleSubmit}
          onClose={() => setShowForm(false)}
        />
      )}

      <ConfirmModal
        isOpen={Boolean(deleteTarget)}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        message={t('confirm.deleteBody', { name: deleteTarget?.email })}
      />
    </>
  );
}

export default UsersView;
