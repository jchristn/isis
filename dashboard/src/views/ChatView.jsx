import { useState, useEffect, useCallback, useRef } from 'react';
import { useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import ScopePicker from '../components/ScopePicker';
import StatusBadge from '../components/StatusBadge';
import Modal from '../components/Modal';
import { EmptyState, ErrorBanner } from '../components/States';

/** Format a millisecond duration compactly (e.g. "820 ms", "1.2 s"). */
function formatMs(ms) {
  if (ms == null) return null;
  if (ms < 1000) return `${Math.round(ms)} ms`;
  return `${(ms / 1000).toFixed(1)} s`;
}

/** Collapsible model reasoning, shown above the answer only when thinking text has arrived. */
function Thinking({ text }) {
  const { t } = useTranslation();
  if (!text) return null;
  return (
    <details className="chat-thinking">
      <summary>{t('chat.thinkingLabel')}</summary>
      <div className="chat-thinking-content">{text}</div>
    </details>
  );
}

/**
 * Collapsible retrieval trace — Isis's analog of a tool call. Lists the memories that grounded the
 * answer; each row expands to reveal its snippet.
 */
function RetrievalTrace({ retrieval }) {
  const { t } = useTranslation();
  if (!retrieval || !Array.isArray(retrieval.hits) || retrieval.hits.length === 0) return null;
  return (
    <details className="chat-tool-trace">
      <summary>{t('chat.retrievalTitle', { count: retrieval.hits.length })}</summary>
      <ul>
        {retrieval.hits.map((hit, index) => (
          <li key={hit.slug || index} className="chat-tool-item-wrap">
            <details className="chat-tool-item">
              <summary>
                <span className="tool-slug">{hit.slug || hit.title || `#${index + 1}`}</span>
                {hit.title && hit.slug ? <span className="tool-title">{hit.title}</span> : null}
                {typeof hit.score === 'number' ? (
                  <StatusBadge tone="info" title={t('chat.retrievalScore')} >
                    {hit.score.toFixed(2)}
                  </StatusBadge>
                ) : null}
              </summary>
              <div className="chat-tool-snippet">{hit.snippet || t('chat.retrievalSnippet')}</div>
            </details>
          </li>
        ))}
      </ul>
    </details>
  );
}

/** Per-answer telemetry surfaced behind an (i) trigger that opens a details modal. */
function StatsInfo({ stats }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  if (!stats) return null;

  const total =
    stats.totalTokens || ((stats.promptTokens || 0) + (stats.completionTokens || 0)) || null;
  const rows = [];
  if (stats.model) rows.push([t('chat.stats.model'), stats.model]);
  if (stats.timeToFirstTokenMs) rows.push([t('chat.stats.ttft'), formatMs(stats.timeToFirstTokenMs)]);
  if (stats.generationMs) rows.push([t('chat.stats.generation'), formatMs(stats.generationMs)]);
  if (stats.promptTokens) rows.push([t('chat.stats.promptTokens'), String(stats.promptTokens)]);
  if (stats.completionTokens) rows.push([t('chat.stats.completionTokens'), String(stats.completionTokens)]);
  if (total) rows.push([t('chat.stats.totalTokens'), String(total)]);
  if (stats.tokensPerSecond) rows.push([t('chat.stats.tps'), stats.tokensPerSecond.toFixed(1)]);
  if (rows.length === 0) return null;

  return (
    <div className="chat-stats-info">
      <button
        type="button"
        className="chat-stats-trigger"
        onClick={() => setOpen(true)}
        aria-label={t('chat.stats.aria')}
      >
        <span className="chat-stats-i" aria-hidden="true">i</span>
        <span>{t('chat.stats.trigger')}</span>
      </button>
      <Modal isOpen={open} onClose={() => setOpen(false)} title={t('chat.stats.title')} size="small">
        <dl className="kv-grid">
          {rows.map(([k, v]) => (
            <div key={k} style={{ display: 'contents' }}>
              <dt>{k}</dt>
              <dd>{v}</dd>
            </div>
          ))}
        </dl>
      </Modal>
    </div>
  );
}

/** Renders a slash-command result rendered as a system bubble (help / context tables, or a message). */
function SystemMessage({ system }) {
  const { t } = useTranslation();
  if (system.kind === 'help') {
    const cmds = [
      ['/help, /?', t('chat.cmd.help')],
      ['/context', t('chat.cmd.context')],
      ['/clear, /new', t('chat.cmd.clear')]
    ];
    return (
      <>
        <div className="chat-sys-title">{t('chat.cmd.title')}</div>
        <table className="chat-sys-table">
          <thead>
            <tr>
              <th>{t('chat.cmd.colCommand')}</th>
              <th>{t('chat.cmd.colDescription')}</th>
            </tr>
          </thead>
          <tbody>
            {cmds.map(([c, d]) => (
              <tr key={c}>
                <td className="cell-mono">{c}</td>
                <td>{d}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </>
    );
  }
  if (system.kind === 'context') {
    const s = system.stats;
    if (!s) return <span>{t('chat.cmd.contextNone')}</span>;
    const total = s.totalTokens || ((s.promptTokens || 0) + (s.completionTokens || 0));
    const rows = [
      [t('chat.cmd.contextModel'), s.model || '—'],
      [t('chat.cmd.contextTotalTokens'), total ? total.toLocaleString() : '—'],
      [t('chat.cmd.contextPromptTokens'), (s.promptTokens || 0).toLocaleString()],
      [t('chat.cmd.contextCompletionTokens'), (s.completionTokens || 0).toLocaleString()],
      [t('chat.cmd.contextTurns'), String(system.turns)]
    ];
    return (
      <>
        <div className="chat-sys-title">{t('chat.cmd.contextTitle')}</div>
        <table className="chat-sys-table">
          <thead>
            <tr>
              <th>{t('chat.cmd.colMetric')}</th>
              <th>{t('chat.cmd.colValue')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(([k, v]) => (
              <tr key={k}>
                <td>{k}</td>
                <td className="cell-mono">{v}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </>
    );
  }
  return <span>{system.text}</span>;
}

function ChatView() {
  const { t } = useTranslation();
  const { scopeId: routeScopeId } = useParams();
  const { apiClient, tenantId, isAdmin } = useAuth();

  const [activeTenant, setActiveTenant] = useState(tenantId);
  const [tenants, setTenants] = useState([]);
  const [scopeId, setScopeId] = useState(routeScopeId || '');
  const [inferenceEndpoints, setInferenceEndpoints] = useState([]);
  const [inferenceEndpointId, setInferenceEndpointId] = useState('');
  const [question, setQuestion] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [keywordOnly, setKeywordOnly] = useState(false);
  const [messages, setMessages] = useState([]);
  const logRef = useRef(null);
  const abortRef = useRef(null);

  const hasInference = inferenceEndpoints.some((e) => e.active !== false);

  // Mutate the last (assistant) message in place as stream events arrive.
  const patchLast = useCallback((patch) => {
    setMessages((prev) => {
      if (prev.length === 0) return prev;
      const next = prev.slice();
      const last = { ...next[next.length - 1] };
      patch(last);
      next[next.length - 1] = last;
      return next;
    });
  }, []);

  const reset = useCallback(() => {
    abortRef.current?.abort();
    abortRef.current = null;
    setMessages([]);
    setBusy(false);
    setError(null);
  }, []);

  // System administrators can pick a tenant; the scope + endpoint lists filter to it.
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

  // Load inference endpoints for the active tenant and default the selection.
  useEffect(() => {
    let cancelled = false;
    apiClient
      .listEndpoints(activeTenant, 'Inference', { maxResults: 1000 })
      .then((res) => {
        if (cancelled) return;
        const list = res.items || [];
        setInferenceEndpoints(list);
        const firstActive = list.find((e) => e.active !== false);
        setInferenceEndpointId(firstActive ? firstActive.id || firstActive.Id : '');
      })
      .catch(() => !cancelled && setInferenceEndpoints([]));
    return () => {
      cancelled = true;
    };
  }, [apiClient, activeTenant]);

  // Detect keyword-only retrieval for the selected scope via its guide.
  useEffect(() => {
    if (!scopeId) return;
    let cancelled = false;
    apiClient
      .getGuide(activeTenant, scopeId)
      .then((g) => {
        if (cancelled) return;
        const caps = g?.capabilities || {};
        setKeywordOnly(caps.supportsSemantic === false);
      })
      .catch(() => setKeywordOnly(false));
    return () => {
      cancelled = true;
    };
  }, [apiClient, activeTenant, scopeId]);

  // Reset the scope selection and conversation when the tenant changes.
  const changeTenant = (tid) => {
    setActiveTenant(tid);
    setScopeId('');
    reset();
  };

  useEffect(() => {
    reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scopeId]);

  // Abort any in-flight stream on unmount.
  useEffect(() => () => abortRef.current?.abort(), []);

  useEffect(() => {
    logRef.current?.scrollTo({ top: logRef.current.scrollHeight, behavior: 'smooth' });
  }, [messages]);

  const runSlashCommand = useCallback(
    (term) => {
      const cmd = term.slice(1).split(/\s+/)[0].toLowerCase();
      if (cmd === 'clear' || cmd === 'new') {
        reset();
        return;
      }
      let system;
      if (cmd === 'help' || cmd === '?') {
        system = { kind: 'help' };
      } else if (cmd === 'context') {
        const last = [...messages].reverse().find((m) => m.role === 'assistant' && m.stats);
        const turns = messages.filter((m) => m.role === 'user').length;
        system = { kind: 'context', stats: last?.stats || null, turns };
      } else {
        system = { kind: 'text', text: t('chat.cmd.unknown', { cmd: `/${cmd}` }) };
      }
      setMessages((prev) => [...prev, { role: 'system', system }]);
    },
    [messages, reset, t]
  );

  const send = useCallback(async () => {
    const q = question.trim();
    if (!q || busy) return;

    // Slash commands are handled client-side and never hit the network.
    if (q.startsWith('/')) {
      setQuestion('');
      runSlashCommand(q);
      return;
    }

    if (!scopeId) return;
    setError(null);
    setBusy(true);
    setQuestion('');
    setMessages((prev) => [
      ...prev,
      { role: 'user', text: q },
      {
        role: 'assistant',
        thinking: '',
        retrieval: null,
        answer: '',
        citations: [],
        notice: null,
        stats: null,
        done: false
      }
    ]);

    const controller = new AbortController();
    abortRef.current = controller;

    try {
      const body = { question: q, topK: 8 };
      if (inferenceEndpointId) body.inferenceEndpointId = inferenceEndpointId;
      await apiClient.chatStream(activeTenant, scopeId, body, {
        signal: controller.signal,
        onEvent: (evt) => {
          if (evt.type === 'retrieval') {
            patchLast((m) => {
              m.retrieval = { mode: evt.mode, hits: evt.hits || [], notice: evt.notice };
              if (evt.notice) m.notice = evt.notice;
            });
          } else if (evt.type === 'thinking') {
            patchLast((m) => {
              m.thinking += evt.text || '';
            });
          } else if (evt.type === 'delta') {
            patchLast((m) => {
              m.answer += evt.text || '';
            });
          } else if (evt.type === 'complete') {
            patchLast((m) => {
              if (evt.answer) m.answer = evt.answer;
              m.citations = Array.isArray(evt.citations) ? evt.citations : [];
              if (evt.notice) m.notice = evt.notice;
              if (evt.retrievalMode && m.retrieval) m.retrieval.mode = evt.retrievalMode;
              m.stats = {
                model: evt.model || null,
                promptTokens: evt.promptTokens || 0,
                completionTokens: evt.completionTokens || 0,
                totalTokens: evt.totalTokens || 0,
                timeToFirstTokenMs: evt.timeToFirstTokenMs || 0,
                generationMs: evt.generationMs || 0,
                tokensPerSecond: evt.tokensPerSecond || 0
              };
              m.done = true;
            });
          } else if (evt.type === 'error') {
            setError(evt.message || t('chat.streamError'));
            patchLast((m) => {
              m.done = true;
            });
          }
        }
      });
    } catch (err) {
      if (err?.name !== 'AbortError') setError(err.message || t('chat.streamError'));
      patchLast((m) => {
        m.done = true;
      });
    } finally {
      setBusy(false);
      abortRef.current = null;
    }
  }, [question, busy, scopeId, apiClient, activeTenant, inferenceEndpointId, patchLast, runSlashCommand, t]);

  return (
    <>
      <PageHeader title={t('chat.title')} subtitle={t('chat.subtitle')} />

      {!routeScopeId && (
        <div className="filter-bar">
          {isAdmin && (
            <div className="filter-field">
              <label htmlFor="chat-tenant">{t('settings.tenant')}</label>
              <select id="chat-tenant" value={activeTenant} onChange={(e) => changeTenant(e.target.value)}>
                {tenants.map((tn) => (
                  <option key={tn.id || tn.Id} value={tn.id || tn.Id}>
                    {tn.name || tn.id || tn.Id}
                  </option>
                ))}
              </select>
            </div>
          )}
          <ScopePicker key={activeTenant} value={scopeId} onChange={setScopeId} tenantId={activeTenant} />
          <div className="filter-field">
            <label htmlFor="chat-inference">{t('chat.inferenceEndpoint')}</label>
            <select
              id="chat-inference"
              value={inferenceEndpointId}
              onChange={(e) => setInferenceEndpointId(e.target.value)}
              disabled={inferenceEndpoints.length === 0}
            >
              {inferenceEndpoints.length === 0 && <option value="">—</option>}
              {inferenceEndpoints.map((e) => (
                <option key={e.id || e.Id} value={e.id || e.Id}>
                  {e.name}{e.active === false ? ` (${t('endpoints.inactive')})` : ''}
                </option>
              ))}
            </select>
          </div>
        </div>
      )}

      {!hasInference && <div className="notice-banner">{t('chat.noInference')}</div>}
      {keywordOnly && <div className="notice-banner">{t('chat.keywordOnly')}</div>}
      {error && <ErrorBanner message={error} onDismiss={() => setError(null)} />}

      <div className="card">
        <div className="chat-log" ref={logRef} style={{ maxHeight: '52vh', overflowY: 'auto' }}>
          {messages.length === 0 ? (
            <EmptyState title={t('chat.title')} message={t('chat.empty')} />
          ) : (
            messages.map((msg, i) => {
              if (msg.role === 'user') {
                return (
                  <div key={i} className="chat-msg user">
                    {msg.text}
                  </div>
                );
              }
              if (msg.role === 'system') {
                return (
                  <div key={i} className="chat-msg assistant chat-msg-system">
                    <SystemMessage system={msg.system} />
                  </div>
                );
              }
              // assistant
              return (
                <div key={i} className="chat-msg assistant">
                  <Thinking text={msg.thinking} />
                  <RetrievalTrace retrieval={msg.retrieval} />
                  {msg.answer && (
                    <div className="chat-answer chat-markdown">
                      <ReactMarkdown remarkPlugins={[remarkGfm]}>{msg.answer}</ReactMarkdown>
                    </div>
                  )}
                  {!msg.done && !msg.answer && (
                    <div className="chat-working" aria-live="polite">
                      <span className="chat-working-dots" aria-hidden="true"><span /><span /><span /></span>
                      <span className="chat-working-label">
                        {msg.thinking
                          ? t('chat.working.thinking')
                          : msg.retrieval
                          ? t('chat.working.generating')
                          : t('chat.working.retrieving')}
                      </span>
                    </div>
                  )}
                  {msg.notice && (
                    <div className="notice-banner" style={{ marginTop: 8, marginBottom: 0 }}>
                      {msg.notice}
                    </div>
                  )}
                  {msg.citations?.length > 0 && (
                    <div className="chat-citations">
                      {msg.citations.map((c, ci) => (
                        <StatusBadge key={ci} tone="info" title={`${t('chat.retrievalScore')}: ${c.score}`}>
                          {c.title || c.slug}
                        </StatusBadge>
                      ))}
                    </div>
                  )}
                  {msg.done && <StatsInfo stats={msg.stats} />}
                </div>
              );
            })
          )}
        </div>

        <div className="chat-input-row" style={{ marginTop: 'var(--spacing-md)' }}>
          <textarea
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
            placeholder={t('chat.placeholder')}
            disabled={busy}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                send();
              }
            }}
          />
          <button
            className="btn-primary"
            onClick={send}
            disabled={busy || !question.trim() || (!scopeId && !question.trim().startsWith('/'))}
          >
            {t('chat.send')}
          </button>
        </div>
      </div>
    </>
  );
}

export default ChatView;
