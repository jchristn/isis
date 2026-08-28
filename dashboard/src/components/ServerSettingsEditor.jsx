import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import StatusBadge from './StatusBadge';
import ConfirmModal from './ConfirmModal';
import { LoadingState } from './States';

const SECRET_RE = /(password|secret|signingkey|apikey)/i;
function isSecret(key) {
  return SECRET_RE.test(key) || /key$/i.test(key);
}
function toTitle(k) {
  return k.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase()).trim();
}

/**
 * Form-driven editor for the server settings JSON. Sections read per-request apply immediately on save;
 * the rest require a restart, which is annotated per section. A Restart Server control exits the node so
 * Docker relaunches it with the saved settings.
 */
function ServerSettingsEditor({ apiClient, addToast }) {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [draft, setDraft] = useState(null);
  const [liveSections, setLiveSections] = useState([]);
  const [saving, setSaving] = useState(false);
  const [restartRequired, setRestartRequired] = useState(false);
  const [confirmRestart, setConfirmRestart] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.getServerSettings();
      setDraft(res.settings || {});
      setLiveSections(res.liveSections || []);
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient]);

  useEffect(() => {
    load();
  }, [load]);

  const setTop = (k, v) => setDraft((d) => ({ ...d, [k]: v }));
  const setNested = (section, k, v) => setDraft((d) => ({ ...d, [section]: { ...d[section], [k]: v } }));

  const save = async () => {
    setSaving(true);
    try {
      const res = await apiClient.updateServerSettings(draft);
      setRestartRequired(Boolean(res.restartRequired));
      addToast(t('settings.saved'), 'success');
    } catch (e) {
      addToast(e.message, 'error');
    } finally {
      setSaving(false);
    }
  };

  const restart = async () => {
    setConfirmRestart(false);
    try {
      await apiClient.restartServer();
    } catch {
      // A dropped connection is expected as the node exits; treat as success.
    }
    addToast(t('settings.restarting'), 'info');
  };

  if (loading) return <LoadingState />;
  if (error) return <div className="error-banner">{error}</div>;
  if (!draft) return null;

  const renderField = (k, v, onChange) => {
    const label = toTitle(k);
    if (typeof v === 'boolean') {
      return (
        <label className="check-row" key={k}>
          <input type="checkbox" checked={v} onChange={(e) => onChange(e.target.checked)} />
          {label}
        </label>
      );
    }
    if (typeof v === 'number') {
      return (
        <div className="field" key={k}>
          <label>{label}</label>
          <input type="number" value={v} onChange={(e) => onChange(e.target.value === '' ? 0 : Number(e.target.value))} />
        </div>
      );
    }
    if (v && typeof v === 'object') {
      return (
        <div className="field" key={k}>
          <label>{label}</label>
          <textarea
            className="cell-mono"
            rows={3}
            defaultValue={JSON.stringify(v, null, 2)}
            onChange={(e) => {
              try {
                onChange(JSON.parse(e.target.value));
              } catch {
                /* keep last valid value until JSON parses */
              }
            }}
          />
        </div>
      );
    }
    return (
      <div className="field" key={k}>
        <label>{label}</label>
        <input type={isSecret(k) ? 'password' : 'text'} value={v ?? ''} onChange={(e) => onChange(e.target.value)} autoComplete="off" />
      </div>
    );
  };

  const scalars = Object.entries(draft).filter(([, v]) => v === null || typeof v !== 'object' || Array.isArray(v));
  const sections = Object.entries(draft).filter(([, v]) => v && typeof v === 'object' && !Array.isArray(v));

  return (
    <>
      {scalars.length > 0 && (
        <div className="section card">
          <div className="section-title" style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
            <span>{t('settings.general')}</span>
            <StatusBadge tone="warning">{t('settings.restartRequired')}</StatusBadge>
          </div>
          {scalars.map(([k, v]) => renderField(k, v, (val) => setTop(k, val)))}
        </div>
      )}

      {sections.map(([section, val]) => {
        const live = liveSections.includes(section);
        return (
          <div className="section card" key={section}>
            <div className="section-title" style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
              <span>{toTitle(section)}</span>
              <StatusBadge tone={live ? 'success' : 'warning'}>
                {live ? t('settings.appliesImmediately') : t('settings.restartRequired')}
              </StatusBadge>
            </div>
            {Object.entries(val).map(([k, v]) => renderField(k, v, (nv) => setNested(section, k, nv)))}
          </div>
        );
      })}

      {restartRequired && <div className="notice-banner">{t('settings.restartNeeded')}</div>}

      <div className="section" style={{ display: 'flex', gap: '0.5rem', justifyContent: 'space-between', flexWrap: 'wrap' }}>
        <button className="btn-primary" onClick={save} disabled={saving}>
          {saving ? t('common.loading') : t('settings.saveSettings')}
        </button>
        <button className="btn-secondary" onClick={() => setConfirmRestart(true)}>
          {t('settings.restartServer')}
        </button>
      </div>

      <ConfirmModal
        isOpen={confirmRestart}
        onClose={() => setConfirmRestart(false)}
        onConfirm={restart}
        title={t('settings.restartServer')}
        message={t('settings.restartConfirm')}
        confirmLabel={t('settings.restartServer')}
      />
    </>
  );
}

export default ServerSettingsEditor;
