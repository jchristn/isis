import { useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import ScopePicker from '../components/ScopePicker';
import StatusBadge from '../components/StatusBadge';
import CopyableId from '../components/CopyableId';
import { EmptyState, ErrorBanner } from '../components/States';
import { SEARCH_MODES } from '../utils/constants';

function SearchExplorerView() {
  const { t } = useTranslation();
  const { apiClient, tenantId } = useAuth();

  const [scopeId, setScopeId] = useState('');
  const [query, setQuery] = useState('');
  const [mode, setMode] = useState('Hybrid');
  const [topK, setTopK] = useState(10);
  const [weight, setWeight] = useState(0.5);
  const [categoryFilter, setCategoryFilter] = useState('');
  const [result, setResult] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [searched, setSearched] = useState(false);

  const run = useCallback(async () => {
    if (!scopeId || !query.trim()) return;
    setLoading(true);
    setError(null);
    setSearched(true);
    try {
      const res = await apiClient.searchMemories(tenantId, scopeId, {
        queryText: query,
        mode,
        topK: Number(topK) || 10,
        textWeight: Number(weight),
        categoryFilter: categoryFilter || undefined
      });
      setResult(res);
    } catch (err) {
      setError(err.message);
      setResult(null);
    } finally {
      setLoading(false);
    }
  }, [apiClient, tenantId, scopeId, query, mode, topK, weight, categoryFilter]);

  const hits = result?.hits || [];

  return (
    <>
      <PageHeader title={t('search.title')} subtitle={t('search.subtitle')} />

      <div className="card section">
        <div className="filter-bar" style={{ marginBottom: 0 }}>
          <ScopePicker value={scopeId} onChange={setScopeId} />
          <div className="filter-field" style={{ flex: 2, minWidth: 240 }}>
            <label htmlFor="q">{t('search.query')}</label>
            <input
              id="q"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={t('search.queryPlaceholder')}
              onKeyDown={(e) => e.key === 'Enter' && run()}
            />
          </div>
          <div className="filter-field">
            <label htmlFor="mode">{t('search.mode')}</label>
            <select id="mode" value={mode} onChange={(e) => setMode(e.target.value)}>
              {SEARCH_MODES.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
          </div>
          <div className="filter-field" style={{ minWidth: 90 }}>
            <label htmlFor="topk">{t('search.topK')}</label>
            <input id="topk" type="number" min={1} value={topK} onChange={(e) => setTopK(e.target.value)} />
          </div>
          <div className="filter-field" style={{ minWidth: 160 }}>
            <label htmlFor="weight">
              {t('search.weight')}: {Number(weight).toFixed(2)}
            </label>
            <input
              id="weight"
              type="range"
              min={0}
              max={1}
              step={0.05}
              value={weight}
              onChange={(e) => setWeight(e.target.value)}
              disabled={mode === 'Keyword' || mode === 'Semantic'}
            />
          </div>
          <div className="filter-field" style={{ minWidth: 150 }}>
            <label htmlFor="cat">{t('search.categoryFilter')}</label>
            <input id="cat" value={categoryFilter} onChange={(e) => setCategoryFilter(e.target.value)} placeholder="cat_…" />
          </div>
          <div className="filter-field" style={{ minWidth: 'auto', flexDirection: 'row', alignItems: 'flex-end' }}>
            <button className="btn-primary" onClick={run} disabled={loading || !scopeId || !query.trim()}>
              {loading ? t('search.running') : t('search.run')}
            </button>
          </div>
        </div>
      </div>

      {error && <ErrorBanner message={error} onRetry={run} onDismiss={() => setError(null)} />}

      <div className="section">
        <div className="section-title" style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
          {t('search.results')}
          {result?.effectiveMode && (
            <StatusBadge tone="info">
              {t('search.effectiveMode')}: {result.effectiveMode}
            </StatusBadge>
          )}
        </div>
        {result?.notice && <div className="notice-banner">{result.notice}</div>}
        {!searched ? (
          <EmptyState title={t('search.title')} message={t('search.empty')} />
        ) : hits.length === 0 && !loading ? (
          <EmptyState title={t('search.noHits')} message={t('search.noHits')} />
        ) : (
          <div className="result-list">
            {hits.map((hit, i) => (
              <div className="result-item" key={hit.storeKey || hit.slug || i}>
                <div className="result-head">
                  <strong>{hit.title || hit.slug}</strong>
                  <StatusBadge tone="neutral">
                    {t('search.score')}: {typeof hit.score === 'number' ? hit.score.toFixed(4) : hit.score}
                  </StatusBadge>
                </div>
                <div className="result-snippet">{hit.snippet || '—'}</div>
                <div style={{ marginTop: 6, display: 'flex', gap: '0.75rem', fontSize: 'var(--font-size-xs)' }}>
                  {hit.slug && <CopyableId value={hit.slug} label={`slug: ${hit.slug}`} />}
                  {hit.storeKey && <CopyableId value={hit.storeKey} label={`key: ${hit.storeKey}`} />}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </>
  );
}

export default SearchExplorerView;
