import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import KpiCard from '../components/KpiCard';
import ActivityChart from '../components/ActivityChart';
import { LoadingState, ErrorBanner } from '../components/States';
import { IconExternal } from '../components/Icons';
import { EXTERNAL_SERVICES } from '../utils/constants';
import { formatNumber } from '../i18n/formatters';

function HomeView() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { apiClient, tenantId } = useAuth();

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [scopes, setScopes] = useState([]);
  const [memoriesPerScope, setMemoriesPerScope] = useState([]);
  const [totals, setTotals] = useState({ memories: 0, categories: 0 });
  const [health, setHealth] = useState({ healthy: 0, unhealthy: 0, total: 0 });

  const load = useCallback(async () => {
    if (!apiClient) return;
    setLoading(true);
    setError(null);
    try {
      const scopeResult = await apiClient.listScopes(tenantId, { maxResults: 1000 });
      const scopeList = scopeResult.items || [];
      setScopes(scopeList);

      // Aggregate per-scope memory + category counts (partial-failure tolerant).
      let memTotal = 0;
      let catTotal = 0;
      const perScope = [];
      await Promise.all(
        scopeList.map(async (scope) => {
          const sid = scope.id || scope.Id;
          try {
            const mem = await apiClient.listMemories(tenantId, sid, { maxResults: 1 });
            const count = mem.totalRecords || 0;
            memTotal += count;
            perScope.push({ label: scope.name || sid, success: count, total: count });
          } catch {
            perScope.push({ label: scope.name || sid, success: 0, total: 0 });
          }
          try {
            const cats = await apiClient.listCategories(tenantId, sid, { maxResults: 1 });
            catTotal += cats.totalRecords || 0;
          } catch {
            /* ignore per-scope category failure */
          }
        })
      );
      setMemoriesPerScope(perScope);
      setTotals({ memories: memTotal, categories: catTotal });

      try {
        const eh = await apiClient.endpointHealth(tenantId);
        const eps = eh?.endpoints || [];
        const healthy = eps.filter((e) => e.status?.isHealthy).length;
        setHealth({ healthy, unhealthy: eps.length - healthy, total: eps.length });
      } catch {
        setHealth({ healthy: 0, unhealthy: 0, total: 0 });
      }
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient, tenantId]);

  useEffect(() => {
    load();
  }, [load]);

  return (
    <>
      <PageHeader
        title={t('home.title')}
        subtitle={t('home.subtitle')}
        actions={
          <button className="btn-secondary" onClick={load}>
            {t('common.refresh')}
          </button>
        }
      />

      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}

      {loading ? (
        <LoadingState />
      ) : (
        <>
          <div className="kpi-grid section">
            <KpiCard
              label={t('home.kpiScopes')}
              value={formatNumber(scopes.length, i18n.language)}
              onClick={() => navigate('/dashboard/scopes')}
            />
            <KpiCard label={t('home.kpiMemories')} value={formatNumber(totals.memories, i18n.language)} />
            <KpiCard label={t('home.kpiCategories')} value={formatNumber(totals.categories, i18n.language)} />
            <KpiCard
              label={t('home.kpiEndpointHealth')}
              value={`${health.healthy}/${health.total}`}
              sub={`${health.unhealthy} ${t('home.unhealthy')}`}
              tone={health.unhealthy > 0 ? 'danger' : 'success'}
              onClick={() => navigate('/dashboard/endpoints/embedding')}
            />
          </div>

          <div className="section">
            <div className="section-title">{t('home.memoriesPerScope')}</div>
            {memoriesPerScope.length ? (
              <ActivityChart
                buckets={memoriesPerScope}
                onBucketClick={(b) => {
                  const scope = scopes.find((s) => (s.name || s.id || s.Id) === b.label);
                  if (scope) navigate(`/dashboard/scopes/${scope.id || scope.Id}/memories`);
                }}
              />
            ) : (
              <div className="card">
                <p className="page-subtitle">{t('home.noScopes')}</p>
              </div>
            )}
          </div>

          <div className="section">
            <div className="section-title">{t('home.quickActions')}</div>
            <div className="tile-grid">
              <button className="action-tile" onClick={() => navigate('/dashboard/scopes')}>
                <span className="tile-title">{t('home.createScope')}</span>
                <span className="tile-desc">{t('scopes.subtitle')}</span>
              </button>
              <button className="action-tile" onClick={() => navigate('/dashboard/endpoints/embedding')}>
                <span className="tile-title">{t('home.addEndpoint')}</span>
                <span className="tile-desc">{t('endpoints.embeddingSubtitle')}</span>
              </button>
              <button className="action-tile" onClick={() => navigate('/dashboard/chat')}>
                <span className="tile-title">{t('home.openChat')}</span>
                <span className="tile-desc">{t('chat.subtitle')}</span>
              </button>
              <button className="action-tile" onClick={() => navigate('/dashboard/api-explorer')}>
                <span className="tile-title">{t('home.openExplorer')}</span>
                <span className="tile-desc">{t('explorer.subtitle')}</span>
              </button>
            </div>
          </div>

          <div className="section">
            <div className="section-title">{t('home.externalServices')}</div>
            <p className="page-subtitle" style={{ marginBottom: 'var(--spacing-md)' }}>
              {t('home.externalServicesHint')}
            </p>
            <div className="tile-grid">
              {EXTERNAL_SERVICES.map((svc) => (
                <a className="action-tile" key={svc.key} href={svc.url} target="_blank" rel="noopener noreferrer">
                  <span className="tile-title">{svc.name}</span>
                  <span className="tile-desc cell-mono">{svc.url}</span>
                  <span className="tile-desc">{t('home.credentials')}: {svc.creds}</span>
                  <span className="tile-open">
                    <IconExternal /> {t('home.openService')}
                  </span>
                </a>
              ))}
            </div>
          </div>
        </>
      )}
    </>
  );
}

export default HomeView;
