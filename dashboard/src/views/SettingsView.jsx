import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import PageHeader from '../components/PageHeader';
import CopyableId from '../components/CopyableId';
import StatusBadge from '../components/StatusBadge';
import CodeViewer from '../components/CodeViewer';
import ServerSettingsEditor from '../components/ServerSettingsEditor';
import LanguageSelector from '../i18n/LanguageSelector';
import { LoadingState } from '../components/States';
import { formatDateTime } from '../i18n/formatters';

function SettingsView() {
  const { t, i18n } = useTranslation();
  const { apiClient, serverUrl, tenantId, whoami, isAdmin, isTenantAdmin, updateTenantId } = useAuth();
  const { addToast } = useApp();

  const [serverInfo, setServerInfo] = useState(null);
  const [health, setHealth] = useState(null);
  const [loading, setLoading] = useState(true);
  const [tenantInput, setTenantInput] = useState(tenantId);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [info, h] = await Promise.allSettled([apiClient.serverInfo(), apiClient.health()]);
      setServerInfo(info.status === 'fulfilled' ? info.value : null);
      setHealth(h.status === 'fulfilled' ? h.value : null);
    } finally {
      setLoading(false);
    }
  }, [apiClient]);

  useEffect(() => {
    load();
  }, [load]);

  const roleLabel = isAdmin ? t('topbar.admin') : isTenantAdmin ? t('topbar.tenantAdmin') : whoami?.principalType || '—';
  const base = serverUrl.replace(/\/$/, '');

  return (
    <>
      <PageHeader
        title={t('settings.title')}
        subtitle={t('settings.subtitle')}
        actions={
          <button className="btn-secondary" onClick={load}>
            {t('common.refresh')}
          </button>
        }
      />

      {loading ? (
        <LoadingState />
      ) : (
        <>
          <div className="section card">
            <div className="section-title">{t('settings.serverInfo')}</div>
            <dl className="kv-grid">
              <dt>{t('settings.product')}</dt>
              <dd>{serverInfo?.product || '—'}</dd>
              <dt>{t('settings.version')}</dt>
              <dd>{serverInfo?.version || '—'}</dd>
              <dt>{t('settings.node')}</dt>
              <dd>{serverInfo?.node || '—'}</dd>
              <dt>{t('settings.health')}</dt>
              <dd>
                <StatusBadge tone={health ? 'success' : 'danger'}>{health?.status || 'unknown'}</StatusBadge>
              </dd>
              <dt>{t('settings.utc')}</dt>
              <dd>{formatDateTime(serverInfo?.utc || health?.utc, i18n.language)}</dd>
            </dl>
          </div>

          <div className="section card">
            <div className="section-title">{t('settings.connection')}</div>
            <dl className="kv-grid">
              <dt>{t('settings.endpoint')}</dt>
              <dd><CopyableId value={base} truncate={false} mono /></dd>
              <dt>{t('settings.health')}</dt>
              <dd><CopyableId value={`${base}/v1.0/api/health`} truncate={false} /></dd>
              <dt>{t('settings.openapi')}</dt>
              <dd><CopyableId value={`${base}/openapi.json`} truncate={false} /></dd>
              <dt>{t('settings.whoami')}</dt>
              <dd><CopyableId value={`${base}/v1.0/api/whoami`} truncate={false} /></dd>
              <dt>{t('topbar.language')}</dt>
              <dd><LanguageSelector compact /></dd>
            </dl>
          </div>

          <div className="section card">
            <div className="section-title">{t('settings.authContext')}</div>
            <dl className="kv-grid">
              <dt>{t('settings.authScheme')}</dt>
              <dd>
                <StatusBadge tone="info">{t('settings.sessionToken')}</StatusBadge>
              </dd>
              <dt>{t('settings.principal')}</dt>
              <dd>{whoami?.principalName || '—'}</dd>
              <dt>{t('settings.principalType')}</dt>
              <dd>{whoami?.principalType || '—'}</dd>
              <dt>{t('settings.role')}</dt>
              <dd><StatusBadge tone="neutral">{roleLabel}</StatusBadge></dd>
              <dt>{t('settings.tenant')}</dt>
              <dd><CopyableId value={whoami?.tenantId || tenantId} /></dd>
              {whoami?.credentialId && (
                <>
                  <dt>{t('settings.credentialId')}</dt>
                  <dd><CopyableId value={whoami.credentialId} /></dd>
                </>
              )}
            </dl>

            <div className="field-row" style={{ marginTop: 'var(--spacing-md)', alignItems: 'flex-end' }}>
              <div className="field" style={{ maxWidth: 280 }}>
                <label>{t('settings.changeTenant')}</label>
                <input value={tenantInput} onChange={(e) => setTenantInput(e.target.value)} />
              </div>
              <button
                className="btn-secondary"
                onClick={() => {
                  updateTenantId(tenantInput.trim());
                  addToast('Active tenant updated', 'success');
                }}
              >
                {t('settings.updateTenant')}
              </button>
            </div>
          </div>

          {isAdmin && (
            <>
              <div className="section-title" style={{ marginTop: 'var(--spacing-lg)' }}>{t('settings.serverSettings')}</div>
              <p className="page-subtitle" style={{ marginBottom: 'var(--spacing-md)' }}>{t('settings.serverSettingsHint')}</p>
              <ServerSettingsEditor apiClient={apiClient} addToast={addToast} />
            </>
          )}

          {whoami && (
            <div className="section card">
              <div className="section-title">{t('settings.whoami')}</div>
              <CodeViewer value={whoami} />
            </div>
          )}
        </>
      )}
    </>
  );
}

export default SettingsView;
