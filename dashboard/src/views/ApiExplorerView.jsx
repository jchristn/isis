import { useState, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import CopyableId from '../components/CopyableId';
import CodeViewer from '../components/CodeViewer';
import ConfirmModal from '../components/ConfirmModal';
import StatusBadge from '../components/StatusBadge';
import { LoadingState, ErrorState, EmptyState } from '../components/States';
import { useApiExplorer } from '../hooks/useApiExplorer';
import { groupByTag, substitutePathParams, buildCodeSnippets } from '../utils/openApi';
import { formatDuration, formatBytes, formatDateTime } from '../i18n/formatters';

function isDestructive(op) {
  return op.method === 'DELETE' || /\/(bulk|delete|reset|restore)/i.test(op.path);
}

function ApiExplorerView() {
  const { t, i18n } = useTranslation();
  const { apiClient, serverUrl } = useAuth();
  const ex = useApiExplorer(apiClient);
  const [filter, setFilter] = useState('');
  const [tab, setTab] = useState('body');
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [showHistory, setShowHistory] = useState(false);

  const groups = useMemo(() => {
    const filtered = ex.operations.filter((op) => {
      if (!filter) return true;
      const q = filter.toLowerCase();
      return (
        op.path.toLowerCase().includes(q) ||
        op.method.toLowerCase().includes(q) ||
        (op.summary || '').toLowerCase().includes(q) ||
        op.id.toLowerCase().includes(q)
      );
    });
    return groupByTag(filtered);
  }, [ex.operations, filter]);

  const op = ex.operation;
  const resolvedPath = op ? substitutePathParams(op.path, ex.pathParams) : '';
  const resolvedUrl = op
    ? (() => {
        const url = new URL(serverUrl.replace(/\/$/, '') + resolvedPath);
        for (const [k, v] of Object.entries(ex.queryParams)) {
          if (v !== undefined && v !== null && v !== '') url.searchParams.append(k, v);
        }
        return url.toString();
      })()
    : '';

  const snippets = useMemo(() => {
    if (!op) return null;
    const headers = { 'Content-Type': 'application/json', ...ex.headers };
    return buildCodeSnippets({
      method: op.method,
      url: resolvedUrl,
      headers,
      body: ['GET', 'HEAD'].includes(op.method) ? null : ex.body
    });
  }, [op, resolvedUrl, ex.headers, ex.body]);

  const bodyIsValidJson = useMemo(() => {
    if (!ex.body || ['GET', 'HEAD'].includes(op?.method)) return true;
    try {
      JSON.parse(ex.body);
      return true;
    } catch {
      return false;
    }
  }, [ex.body, op]);

  const doExecute = () => {
    if (op && isDestructive(op)) setConfirmOpen(true);
    else ex.execute();
  };

  if (ex.specLoading) return <LoadingState />;
  if (ex.specError) {
    return (
      <>
        <PageHeader title={t('explorer.title')} subtitle={t('explorer.subtitle')} />
        <ErrorState title={t('explorer.noSpec')} message={`${t('explorer.noSpecHint')} (${ex.specError})`} onRetry={() => window.location.reload()} />
      </>
    );
  }

  const pathParamList = op ? op.parameters.filter((p) => p.in === 'path') : [];
  const queryParamList = op ? op.parameters.filter((p) => p.in === 'query') : [];
  const allowsBody = op && !['GET', 'HEAD'].includes(op.method);

  return (
    <>
      <PageHeader
        title={t('explorer.title')}
        subtitle={t('explorer.subtitle')}
        actions={
          <button className="btn-secondary" onClick={() => setShowHistory((v) => !v)}>
            {t('explorer.history')} ({ex.history.length})
          </button>
        }
      />
      <div className="notice-banner">{t('explorer.authInherited')}</div>

      <div className="explorer-layout">
        <div className="explorer-ops">
          <div style={{ padding: '0.5rem' }}>
            <input
              placeholder={t('explorer.searchOps')}
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
            />
          </div>
          <div className="explorer-ops-list">
            {groups.map((group) => (
              <div key={group.tag}>
                <div className="explorer-op-group-label">{group.tag}</div>
                {group.operations.map((o) => (
                  <button
                    key={o.id}
                    className={`explorer-op${ex.operationId === o.id ? ' active' : ''}`}
                    onClick={() => ex.selectOperation(o.id)}
                  >
                    <span className={`method-badge method-${o.method}`}>{o.method}</span>
                    <span className="op-path">{o.path}</span>
                  </button>
                ))}
              </div>
            ))}
          </div>
        </div>

        <div className="explorer-panel">
          {!op ? (
            <EmptyState title={t('explorer.title')} message={t('explorer.pickOperation')} />
          ) : (
            <>
              <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', marginBottom: 'var(--spacing-md)', flexWrap: 'wrap' }}>
                <span className={`method-badge method-${op.method}`}>{op.method}</span>
                <span className="cell-mono">{op.path}</span>
                {isDestructive(op) && <StatusBadge tone="danger">destructive</StatusBadge>}
              </div>
              {op.summary && <p className="page-subtitle" style={{ marginBottom: 'var(--spacing-md)' }}>{op.summary}</p>}

              {pathParamList.length > 0 && (
                <div className="explorer-subsection">
                  <h4>{t('explorer.pathParams')}</h4>
                  {pathParamList.map((p) => (
                    <div className="field" key={p.name}>
                      <label>
                        {p.name} {p.required && <span style={{ color: 'var(--color-danger)' }}>*</span>}
                      </label>
                      <input
                        value={ex.pathParams[p.name] ?? ''}
                        onChange={(e) => ex.setPathParams({ ...ex.pathParams, [p.name]: e.target.value })}
                      />
                    </div>
                  ))}
                </div>
              )}

              {queryParamList.length > 0 && (
                <div className="explorer-subsection">
                  <h4>{t('explorer.queryParams')}</h4>
                  {queryParamList.map((p) => (
                    <div className="field" key={p.name}>
                      <label>{p.name}</label>
                      <input
                        value={ex.queryParams[p.name] ?? ''}
                        onChange={(e) => ex.setQueryParams({ ...ex.queryParams, [p.name]: e.target.value })}
                      />
                    </div>
                  ))}
                </div>
              )}

              {allowsBody && (
                <div className="explorer-subsection">
                  <h4>{t('explorer.body')}</h4>
                  <textarea
                    className="cell-mono"
                    rows={8}
                    value={ex.body}
                    onChange={(e) => ex.setBody(e.target.value)}
                    style={{ borderColor: bodyIsValidJson ? undefined : 'var(--color-danger)' }}
                  />
                  {!bodyIsValidJson && <div className="field-hint" style={{ color: 'var(--color-danger)' }}>{t('explorer.invalidJson')}</div>}
                </div>
              )}

              <div className="explorer-subsection">
                <h4>{t('explorer.resolvedUrl')}</h4>
                <CopyableId value={resolvedUrl} truncate={false} />
              </div>

              <div style={{ display: 'flex', gap: '0.5rem', marginBottom: 'var(--spacing-md)' }}>
                <button className="btn-primary" onClick={doExecute} disabled={ex.executing || !bodyIsValidJson}>
                  {ex.executing ? t('explorer.executing') : t('explorer.execute')}
                </button>
              </div>

              {ex.response && (
                <div className="explorer-subsection">
                  <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center', marginBottom: '0.5rem', flexWrap: 'wrap' }}>
                    <StatusBadge tone={ex.response.status >= 200 && ex.response.status < 300 ? 'success' : ex.response.status === 0 ? 'danger' : 'warning'}>
                      {t('explorer.responseStatus')}: {ex.response.status} {ex.response.statusText}
                    </StatusBadge>
                    <span className="page-subtitle">
                      {t('explorer.responseTime')}: {formatDuration(ex.response.durationMs, i18n.language)} · {formatBytes(ex.response.byteLength, i18n.language)}
                    </span>
                  </div>
                  <div className="tabs">
                    <button className={`tab${tab === 'body' ? ' active' : ''}`} onClick={() => setTab('body')}>
                      {t('explorer.responseBody')}
                    </button>
                    <button className={`tab${tab === 'headers' ? ' active' : ''}`} onClick={() => setTab('headers')}>
                      {t('explorer.responseHeaders')}
                    </button>
                    <button className={`tab${tab === 'code' ? ' active' : ''}`} onClick={() => setTab('code')}>
                      {t('explorer.codeSnippets')}
                    </button>
                  </div>
                  {tab === 'body' && <CodeViewer value={ex.response.body} language="json" />}
                  {tab === 'headers' && <CodeViewer value={ex.response.headers} language="json" />}
                  {tab === 'code' && snippets && (
                    <>
                      <h4 style={{ margin: '0.5rem 0' }}>curl</h4>
                      <CodeViewer value={snippets.curl} language="text" maxHeight={180} />
                      <h4 style={{ margin: '0.5rem 0' }}>fetch</h4>
                      <CodeViewer value={snippets.fetch} language="text" maxHeight={180} />
                      <h4 style={{ margin: '0.5rem 0' }}>C#</h4>
                      <CodeViewer value={snippets.csharp} language="text" maxHeight={220} />
                    </>
                  )}
                </div>
              )}

              {!ex.response && snippets && (
                <div className="explorer-subsection">
                  <h4>{t('explorer.codeSnippets')}</h4>
                  <CodeViewer value={snippets.curl} language="text" maxHeight={180} />
                </div>
              )}
            </>
          )}

          {showHistory && (
            <div className="explorer-subsection" style={{ marginTop: 'var(--spacing-lg)', borderTop: '1px solid var(--color-border)', paddingTop: 'var(--spacing-md)' }}>
              <h4>{t('explorer.history')}</h4>
              {ex.history.length === 0 ? (
                <p className="page-subtitle">—</p>
              ) : (
                <div className="result-list">
                  {ex.history.map((h, i) => (
                    <div className="result-item" key={i}>
                      <div className="result-head">
                        <span>
                          <span className={`method-badge method-${h.method}`}>{h.method}</span>{' '}
                          <span className="cell-mono">{h.path}</span>
                        </span>
                        <span style={{ display: 'flex', gap: '0.4rem' }}>
                          <button className="btn-sm btn-secondary" onClick={() => ex.loadHistory(h)}>
                            {t('explorer.loadHistory')}
                          </button>
                          <button className="btn-sm btn-ghost" onClick={() => ex.deleteHistory(i)}>
                            {t('explorer.deleteHistory')}
                          </button>
                        </span>
                      </div>
                      <div className="result-snippet">
                        {h.status ? `→ ${h.status}` : ''} · {formatDateTime(h.at, i18n.language)}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      <ConfirmModal
        isOpen={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        onConfirm={async () => {
          setConfirmOpen(false);
          await ex.execute();
        }}
        title={t('explorer.confirmDestructive')}
        message={t('explorer.confirmDestructiveBody', { method: op?.method, path: op?.path })}
        confirmLabel={t('explorer.execute')}
      />
    </>
  );
}

export default ApiExplorerView;
