import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import ScopePicker from '../components/ScopePicker';
import CategoriesPanel from '../components/CategoriesPanel';
import MemoriesPanel from '../components/MemoriesPanel';
import { EmptyState } from '../components/States';

/**
 * Top-level memory browser: filter by tenant (system admins) then by scope, and manage
 * that scope's categories and memories directly. Reuses the same panels the scope drill-in
 * views render, so behavior stays identical everywhere.
 */
function MemoryBrowserView() {
  const { t } = useTranslation();
  const { apiClient, tenantId, isAdmin } = useAuth();

  const [activeTenant, setActiveTenant] = useState(tenantId);
  const [tenants, setTenants] = useState([]);
  const [scopeId, setScopeId] = useState('');

  // System administrators can pick a tenant; the scope list filters to it.
  useEffect(() => {
    if (!isAdmin) return undefined;
    let cancelled = false;
    apiClient
      .listTenants({ maxResults: 1000 })
      .then((res) => !cancelled && setTenants(res.items || []))
      .catch(() => !cancelled && setTenants([]));
    return () => {
      cancelled = true;
    };
  }, [apiClient, isAdmin]);

  const changeTenant = (tid) => {
    setActiveTenant(tid);
    setScopeId('');
  };

  return (
    <>
      <PageHeader title={t('memoryBrowser.title')} subtitle={t('memoryBrowser.subtitle')} />

      <div className="filter-bar">
        {isAdmin && (
          <div className="filter-field">
            <label htmlFor="mem-tenant">{t('settings.tenant')}</label>
            <select id="mem-tenant" value={activeTenant} onChange={(e) => changeTenant(e.target.value)}>
              {tenants.map((tn) => (
                <option key={tn.id || tn.Id} value={tn.id || tn.Id}>
                  {tn.name || tn.id || tn.Id}
                </option>
              ))}
            </select>
          </div>
        )}
        <ScopePicker key={activeTenant} value={scopeId} onChange={setScopeId} tenantId={activeTenant} />
      </div>

      {!scopeId ? (
        <EmptyState title={t('memoryBrowser.title')} message={t('memoryBrowser.selectScope')} />
      ) : (
        <>
          <div className="section-title" style={{ marginTop: 'var(--spacing-lg)' }}>{t('categories.title')}</div>
          <CategoriesPanel key={`c:${activeTenant}:${scopeId}`} tenantId={activeTenant} scopeId={scopeId} />

          <div className="section-title" style={{ marginTop: 'var(--spacing-xl, 2rem)' }}>{t('memories.title')}</div>
          <MemoriesPanel key={`m:${activeTenant}:${scopeId}`} tenantId={activeTenant} scopeId={scopeId} />
        </>
      )}
    </>
  );
}

export default MemoryBrowserView;
