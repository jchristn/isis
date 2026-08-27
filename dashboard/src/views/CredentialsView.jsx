import { useState, useEffect, useCallback, useMemo } from 'react';
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

function CredentialForm({ initial, users, onSubmit, onClose, t }) {
  const editing = Boolean(initial);
  const [form, setForm] = useState(
    initial || { name: '', userId: users[0]?.id || '', active: true }
  );
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState(null);
  const set = (k, v) => setForm((f) => ({ ...f, [k]: v }));

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      const body = editing
        ? { name: form.name || null, active: form.active }
        : { name: form.name || null, userId: form.userId, active: form.active };
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
      title={editing ? t('credentials.editCredential') : t('credentials.addCredential')}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button className="btn-primary" onClick={submit} disabled={busy || (!editing && !form.userId)}>
            {t('common.save')}
          </button>
        </>
      }
    >
      <form onSubmit={submit}>
        {err && <div className="error-banner">{err}</div>}
        <div className="field">
          <label>{t('common.name')}</label>
          <input value={form.name || ''} onChange={(e) => set('name', e.target.value)} placeholder={t('credentials.namePlaceholder')} autoFocus />
        </div>
        {!editing && (
          <div className="field">
            <label>{t('credentials.owner')}</label>
            <select value={form.userId} onChange={(e) => set('userId', e.target.value)} required>
              {users.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.email}
                </option>
              ))}
            </select>
          </div>
        )}
        <label className="check-row">
          <input type="checkbox" checked={form.active} onChange={(e) => set('active', e.target.checked)} />
          {t('credentials.active')}
        </label>
      </form>
    </Modal>
  );
}

function CredentialsView() {
  const { t } = useTranslation();
  const { apiClient, tenantId, isAdmin, isTenantAdmin } = useAuth();
  const { addToast } = useApp();

  const canManage = isAdmin || isTenantAdmin;
  const [credentials, setCredentials] = useState([]);
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [revealed, setRevealed] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [creds, us] = await Promise.all([
        apiClient.listCredentials(tenantId, { maxResults: 1000 }),
        apiClient.listUsers(tenantId, { maxResults: 1000 })
      ]);
      setCredentials(creds.items || []);
      setUsers(us.items || []);
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

  const emailById = useMemo(() => {
    const map = {};
    for (const u of users) map[u.id] = u.email;
    return map;
  }, [users]);

  const handleSubmit = async (body) => {
    if (editing?.id) {
      await apiClient.updateCredential(tenantId, editing.id, body);
      addToast(t('credentials.updated'), 'success');
    } else {
      const created = await apiClient.createCredential(tenantId, body);
      addToast(t('credentials.created'), 'success');
      if (created?.secretKey) setRevealed(created);
    }
    load();
  };

  const handleDelete = async () => {
    await apiClient.deleteCredential(tenantId, deleteTarget.id);
    addToast(t('credentials.deleted'), 'success');
    setDeleteTarget(null);
    load();
  };

  const columns = [
    { key: 'name', label: t('common.name'), pinned: true, render: (x) => x.name || '—' },
    { key: 'accessKey', label: t('credentials.accessKey'), cellClass: 'cell-id', render: (x) => <CopyableId value={x.accessKey} /> },
    { key: 'secretKeyLast4', label: t('credentials.secret'), render: (x) => (x.secretKeyLast4 ? `••••${x.secretKeyLast4}` : '—') },
    { key: 'userId', label: t('credentials.owner'), render: (x) => emailById[x.userId] || x.userId },
    { key: 'active', label: t('credentials.active'), render: (x) => <StatusBadge tone={x.active ? 'success' : 'danger'}>{x.active ? t('common.yes') : t('common.no')}</StatusBadge> },
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
        <PageHeader title={t('credentials.title')} subtitle={t('credentials.subtitle')} />
        <EmptyState title={t('credentials.title')} message={t('credentials.adminOnly')} />
      </>
    );
  }

  return (
    <>
      <PageHeader
        title={t('credentials.title')}
        subtitle={t('credentials.subtitle')}
        actions={
          <button className="btn-primary" disabled={users.length === 0} onClick={() => { setEditing(null); setShowForm(true); }}>
            + {t('credentials.addCredential')}
          </button>
        }
      />
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable
        tableId="credentials"
        columns={columns}
        data={credentials}
        loading={loading}
        onRefresh={load}
        emptyMessage={t('credentials.empty')}
      />

      {showForm && (
        <CredentialForm
          initial={editing}
          users={users}
          t={t}
          onSubmit={handleSubmit}
          onClose={() => setShowForm(false)}
        />
      )}

      {revealed && (
        <Modal
          isOpen
          onClose={() => setRevealed(null)}
          title={t('credentials.secretRevealedTitle')}
          footer={<button className="btn-primary" onClick={() => setRevealed(null)}>{t('common.close')}</button>}
        >
          <div className="error-banner" style={{ marginBottom: 'var(--spacing-md)' }}>{t('credentials.secretRevealedWarning')}</div>
          <dl className="kv-grid">
            <dt>{t('credentials.accessKey')}</dt>
            <dd><CopyableId value={revealed.accessKey} truncate={false} mono /></dd>
            <dt>{t('credentials.secretKeyFull')}</dt>
            <dd><CopyableId value={revealed.secretKey} truncate={false} mono /></dd>
          </dl>
        </Modal>
      )}

      <ConfirmModal
        isOpen={Boolean(deleteTarget)}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        message={t('confirm.deleteBody', { name: deleteTarget?.name || deleteTarget?.accessKey })}
      />
    </>
  );
}

export default CredentialsView;
