import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import CopyableId from '../components/CopyableId';
import StatusBadge from '../components/StatusBadge';
import CodeViewer from '../components/CodeViewer';
import { LoadingState, ErrorState } from '../components/States';

function ScopeDetail() {
  const { t } = useTranslation();
  const { scopeId } = useParams();
  const navigate = useNavigate();
  const { apiClient, tenantId } = useAuth();

  const [scope, setScope] = useState(null);
  const [guide, setGuide] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const s = await apiClient.getScope(tenantId, scopeId);
      setScope(s);
    } catch (err) {
      setError(err.message);
      setLoading(false);
      return;
    }
    try {
      setGuide(await apiClient.getGuide(tenantId, scopeId));
    } catch {
      setGuide(null);
    }
    setLoading(false);
  }, [apiClient, tenantId, scopeId]);

  useEffect(() => {
    load();
  }, [load]);

  if (loading) return <LoadingState />;
  if (error) return <ErrorState message={error} onRetry={load} />;

  const caps = guide?.capabilities || {};

  return (
    <>
      <PageHeader
        title={scope?.name || scopeId}
        subtitle={scope?.description || t('scopes.detailTitle')}
        breadcrumbs={
          <>
            <Link to="/dashboard/scopes">{t('scopes.title')}</Link> / {scope?.name || scopeId}
          </>
        }
        actions={
          <>
            <button className="btn-secondary" onClick={() => navigate(`/dashboard/scopes/${scopeId}/categories`)}>
              {t('scopes.openCategories')}
            </button>
            <button className="btn-secondary" onClick={() => navigate(`/dashboard/scopes/${scopeId}/memories`)}>
              {t('scopes.openMemories')}
            </button>
            <button className="btn-primary" onClick={() => navigate(`/dashboard/scopes/${scopeId}/chat`)}>
              {t('scopes.openChat')}
            </button>
          </>
        }
      />

      <div className="section card">
        <div className="section-title">{t('scopes.detailTitle')}</div>
        <dl className="kv-grid">
          <dt>{t('common.id')}</dt>
          <dd>
            <CopyableId value={scope?.id || scopeId} />
          </dd>
          <dt>{t('scopes.storeProvider')}</dt>
          <dd>
            <StatusBadge tone="info">{scope?.storeProvider || '—'}</StatusBadge>
          </dd>
          <dt>{t('scopes.dimensionality')}</dt>
          <dd>{scope?.dimensionality ?? '—'}</dd>
          <dt>{t('scopes.recallCollection')}</dt>
          <dd>{scope?.recallCollectionId ? <CopyableId value={scope.recallCollectionId} /> : '—'}</dd>
          <dt>{t('scopes.embeddingEndpoint')}</dt>
          <dd>{scope?.embeddingEndpointId ? <CopyableId value={scope.embeddingEndpointId} /> : '—'}</dd>
          {scope?.targetPath && (
            <>
              <dt>{t('scopes.targetPath')}</dt>
              <dd className="cell-mono">{scope.targetPath}</dd>
            </>
          )}
        </dl>
      </div>

      {guide && (
        <div className="section card">
          <div className="section-title">Retrieval capabilities</div>
          <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', marginBottom: 'var(--spacing-md)' }}>
            <StatusBadge tone={caps.supportsKeyword ? 'success' : 'neutral'}>Keyword</StatusBadge>
            <StatusBadge tone={caps.supportsSemantic ? 'success' : 'neutral'}>Semantic</StatusBadge>
            <StatusBadge tone={caps.supportsHybrid ? 'success' : 'neutral'}>Hybrid</StatusBadge>
            <StatusBadge tone={caps.requiresEmbedding ? 'warning' : 'neutral'}>
              {caps.requiresEmbedding ? 'Requires embedding' : 'No embedding required'}
            </StatusBadge>
          </div>
          {caps.description && <p className="page-subtitle">{caps.description}</p>}
          {guide.instructions && (
            <div style={{ marginTop: 'var(--spacing-md)' }}>
              <div className="kpi-label" style={{ marginBottom: 4 }}>
                {t('categories.instructions')}
              </div>
              <CodeViewer value={guide.instructions} language="text" maxHeight={200} />
            </div>
          )}
          {Array.isArray(guide.categories) && guide.categories.length > 0 && (
            <div style={{ marginTop: 'var(--spacing-md)' }}>
              <div className="kpi-label" style={{ marginBottom: 4 }}>
                {t('categories.title')} ({guide.categories.length})
              </div>
              <div className="tile-grid">
                {guide.categories.map((c) => (
                  <div className="action-tile" key={c.id}>
                    <span className="tile-title">{c.name}</span>
                    {c.description && <span className="tile-desc">{c.description}</span>}
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </>
  );
}

export default ScopeDetail;
