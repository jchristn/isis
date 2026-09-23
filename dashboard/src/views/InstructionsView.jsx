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
import { INSTRUCTION_MERGE_MODES } from '../utils/constants';

function sourceTone(source) {
  if (source === 'ScopeOverride') return 'warning';
  if (source === 'ScopeAdded') return 'info';
  return 'neutral';
}

function InstructionForm({ initial, scoped, onSubmit, onClose, t }) {
  const [form, setForm] = useState(
    initial || { name: '', content: '', position: 0, active: true, mergeMode: 'Append' }
  );
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState(null);
  const set = (k, v) => setForm((f) => ({ ...f, [k]: v }));
  const isHide = scoped && form.mergeMode === 'Hide';

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await onSubmit({
        name: form.name,
        content: isHide ? '' : form.content,
        position: Number(form.position) || 0,
        active: form.active,
        mergeMode: scoped ? form.mergeMode : 'Append'
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
      title={initial ? t('common.edit') : scoped ? t('instructions.addScopeInstruction') : t('instructions.addInstruction')}
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
        {scoped && (
          <div className="field">
            <label>{t('instructions.mergeMode')}</label>
            <select value={form.mergeMode} onChange={(e) => set('mergeMode', e.target.value)}>
              {INSTRUCTION_MERGE_MODES.map((m) => (
                <option key={m} value={m}>{t(`instructions.merge_${m}`)}</option>
              ))}
            </select>
            <span className="field-hint">{t('instructions.mergeHint')}</span>
          </div>
        )}
        {!isHide && (
          <div className="field">
            <label>{t('instructions.content')}</label>
            <textarea value={form.content} onChange={(e) => set('content', e.target.value)} rows={10} placeholder={t('instructions.contentPlaceholder')} />
          </div>
        )}
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
  const [scopes, setScopes] = useState([]);
  const [scopeId, setScopeId] = useState(''); // '' = tenant-global
  const scoped = scopeId !== '';

  const [items, setItems] = useState([]); // raw rows for the current view (global or this scope)
  const [effective, setEffective] = useState([]); // resolved list (scope view only)
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);

  // Load the tenant's scopes once for the selector.
  useEffect(() => {
    apiClient.listScopes(tenantId, { maxResults: 1000 }).then((res) => setScopes(res.items || [])).catch(() => setScopes([]));
  }, [apiClient, tenantId]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      if (scoped) {
        const [raw, resolved] = await Promise.all([
          apiClient.listScopeInstructions(tenantId, scopeId, { maxResults: 1000 }),
          apiClient.resolveInstructions(tenantId, scopeId)
        ]);
        setItems(raw.items || []);
        setEffective(resolved.items || []);
      } else {
        const res = await apiClient.listInstructions(tenantId, { maxResults: 1000 });
        setItems(res.items || []);
        setEffective([]);
      }
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient, tenantId, scopeId, scoped]);

  useEffect(() => {
    load();
  }, [load]);

  const handleSubmit = async (body) => {
    if (scoped) {
      if (editing?.id) await apiClient.updateScopeInstruction(tenantId, scopeId, editing.id, body);
      else await apiClient.createScopeInstruction(tenantId, scopeId, body);
    } else if (editing?.id) {
      await apiClient.updateInstruction(tenantId, editing.id, body);
    } else {
      await apiClient.createInstruction(tenantId, body);
    }
    addToast(editing?.id ? t('instructions.updated') : t('instructions.created'), 'success');
    load();
  };

  const handleDelete = async () => {
    if (scoped) await apiClient.deleteScopeInstruction(tenantId, scopeId, deleteTarget.id);
    else await apiClient.deleteInstruction(tenantId, deleteTarget.id);
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
    ...(scoped ? [{ key: 'mergeMode', label: t('instructions.mergeMode'), width: '110px', render: (x) => <StatusBadge tone="info">{t(`instructions.merge_${x.mergeMode || 'Append'}`)}</StatusBadge> }] : []),
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
        subtitle={scoped ? t('instructions.scopeSubtitle') : t('instructions.subtitle')}
        actions={
          canManage ? (
            <button className="btn-primary" onClick={() => { setEditing(null); setShowForm(true); }}>
              + {scoped ? t('instructions.addScopeInstruction') : t('instructions.addInstruction')}
            </button>
          ) : null
        }
      />

      <div className="filter-bar section">
        <div className="field" style={{ maxWidth: 360 }}>
          <label>{t('instructions.scopeLabel')}</label>
          <select value={scopeId} onChange={(e) => setScopeId(e.target.value)}>
            <option value="">{t('instructions.tenantGlobal')}</option>
            {scopes.map((s) => (
              <option key={s.id || s.Id} value={s.id || s.Id}>{s.name || s.id || s.Id}</option>
            ))}
          </select>
        </div>
      </div>

      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}

      {!loading && items.length === 0 && !canManage ? (
        <EmptyState title={t('instructions.title')} message={t('instructions.emptyReadonly')} />
      ) : (
        <DataTable
          tableId={scoped ? 'instructions-scope' : 'instructions'}
          columns={columns}
          data={items}
          loading={loading}
          onRefresh={load}
          onRowClick={canManage ? openEdit : null}
          emptyMessage={scoped ? t('instructions.emptyScope') : t('instructions.empty')}
        />
      )}

      {scoped && (
        <div className="section">
          <div className="section-title">{t('instructions.effective')}</div>
          <p className="page-subtitle" style={{ marginBottom: 'var(--spacing-sm)' }}>{t('instructions.effectiveHint')}</p>
          {effective.length === 0 ? (
            <div className="card"><p className="page-subtitle">{t('instructions.emptyReadonly')}</p></div>
          ) : (
            effective.map((r) => (
              <div className="card" key={r.id} style={{ marginBottom: 'var(--spacing-sm)' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--spacing-sm)', marginBottom: '0.35rem' }}>
                  <strong>{r.name}</strong>
                  <StatusBadge tone={sourceTone(r.source)}>{t(`instructions.source_${r.source}`)}</StatusBadge>
                </div>
                <div className="page-subtitle" style={{ whiteSpace: 'pre-wrap' }}>{r.content || '—'}</div>
              </div>
            ))
          )}
        </div>
      )}

      {showForm && (
        <InstructionForm initial={editing} scoped={scoped} t={t} onSubmit={handleSubmit} onClose={() => setShowForm(false)} />
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
