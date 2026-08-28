import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import ActionMenu from '../components/ActionMenu';
import CopyableId from '../components/CopyableId';
import CodeViewer from '../components/CodeViewer';
import StatusBadge from '../components/StatusBadge';
import { EmptyState, ErrorBanner } from '../components/States';

function TenantForm({ initial, onSubmit, onClose, t }) {
  const [form, setForm] = useState(
    initial
      ? { name: initial.name || '', description: initial.description || '', active: initial.active !== false }
      : { name: '', description: '', active: true }
  );
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState(null);
  const set = (k, v) => setForm((f) => ({ ...f, [k]: v }));

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await onSubmit({ name: form.name, description: form.description, active: form.active });
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
        <div className="field">
          <label style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <input type="checkbox" style={{ width: 'auto' }} checked={form.active} onChange={(e) => set('active', e.target.checked)} />
            {t('common.active')}
          </label>
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
  const [nukeTarget, setNukeTarget] = useState(null);
  const [nukeInput, setNukeInput] = useState('');
  const [nukeBusy, setNukeBusy] = useState(false);
  const [provisioned, setProvisioned] = useState(null);

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
      addToast(t('tenants.updated'), 'success');
    } else {
      const res = await apiClient.createTenant(body);
      addToast(t('tenants.created'), 'success');
      // Provisioning returns the generated admin + credential once — reveal them.
      if (res?.admin || res?.credential) setProvisioned(res);
    }
    load();
  };

  const openNuke = (x) => {
    setNukeTarget(x);
    setNukeInput('');
  };

  const handleNuke = async () => {
    setNukeBusy(true);
    try {
      await apiClient.deleteTenant(nukeTarget.id || nukeTarget.Id);
      addToast(t('tenants.nuked'), 'success');
      setNukeTarget(null);
      load();
    } catch (err) {
      addToast(err.message, 'error');
    } finally {
      setNukeBusy(false);
    }
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
      key: 'active',
      label: t('common.status'),
      render: (x) => (
        <StatusBadge tone={x.active === false ? 'neutral' : 'success'}>
          {x.active === false ? t('common.inactive') : t('common.active')}
        </StatusBadge>
      )
    },
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
            { label: t('common.duplicate'), onClick: () => { setEditing({ ...x, id: undefined, Id: undefined, name: `${x.name} (copy)` }); setShowForm(true); } },
            { label: t('common.viewJson'), onClick: () => setJsonItem(x) },
            { divider: true },
            { label: t('tenants.nuke'), danger: true, disabled: x.protected || x.Protected, onClick: () => openNuke(x) }
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

      {nukeTarget && (
        <Modal
          isOpen
          onClose={() => setNukeTarget(null)}
          title={t('tenants.nukeTitle')}
          footer={
            <>
              <button className="btn-secondary" onClick={() => setNukeTarget(null)} disabled={nukeBusy}>{t('common.cancel')}</button>
              <button
                className="btn-danger"
                onClick={handleNuke}
                disabled={nukeBusy || nukeInput.trim() !== (nukeTarget.id || nukeTarget.Id)}
              >
                {nukeBusy ? t('common.loading') : t('tenants.nukeConfirmButton')}
              </button>
            </>
          }
        >
          <div className="error-banner">{t('tenants.nukeWarning', { name: nukeTarget.name })}</div>
          <div className="field" style={{ marginTop: 'var(--spacing-md)' }}>
            <label>{t('tenants.nukeTypeId')}</label>
            <div style={{ marginBottom: '0.35rem' }}><CopyableId value={nukeTarget.id || nukeTarget.Id} truncate={false} mono /></div>
            <input value={nukeInput} onChange={(e) => setNukeInput(e.target.value)} placeholder={nukeTarget.id || nukeTarget.Id} autoFocus autoComplete="off" />
          </div>
        </Modal>
      )}

      {provisioned && (
        <Modal
          isOpen
          onClose={() => setProvisioned(null)}
          title={t('tenants.provisionedTitle')}
          footer={<button className="btn-primary" onClick={() => setProvisioned(null)}>{t('common.close')}</button>}
        >
          <div className="error-banner" style={{ marginBottom: 'var(--spacing-md)' }}>{t('tenants.provisionedWarning')}</div>
          <dl className="kv-grid">
            <dt>{t('login.email')}</dt>
            <dd><CopyableId value={provisioned.admin?.email} truncate={false} mono /></dd>
            <dt>{t('login.password')}</dt>
            <dd><CopyableId value={provisioned.admin?.password} truncate={false} mono /></dd>
            <dt>{t('credentials.accessKey')}</dt>
            <dd><CopyableId value={provisioned.credential?.accessKey} truncate={false} mono /></dd>
            <dt>{t('credentials.secretKeyFull')}</dt>
            <dd><CopyableId value={provisioned.credential?.secretKey} truncate={false} mono /></dd>
          </dl>
        </Modal>
      )}
    </>
  );
}

export default TenantsView;
