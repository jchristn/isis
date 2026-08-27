import { useState, useEffect, useCallback, useRef } from 'react';
import { useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import ScopePicker from '../components/ScopePicker';
import StatusBadge from '../components/StatusBadge';
import { EmptyState, ErrorBanner } from '../components/States';

/** Types an answer out character-by-character to give a streamed feel. */
function useTypedAnswer() {
  const [messages, setMessages] = useState([]);
  const timers = useRef([]);

  useEffect(
    () => () => {
      timers.current.forEach(clearTimeout);
    },
    []
  );

  const pushUser = (text) => setMessages((m) => [...m, { role: 'user', text }]);

  const streamAssistant = (fullText, citations, meta) => {
    const id = Date.now();
    setMessages((m) => [...m, { id, role: 'assistant', text: '', citations, meta, done: false }]);
    const chars = [...(fullText || '')];
    let idx = 0;
    const step = () => {
      idx += Math.max(2, Math.round(chars.length / 60));
      const partial = chars.slice(0, idx).join('');
      setMessages((m) => m.map((msg) => (msg.id === id ? { ...msg, text: partial } : msg)));
      if (idx < chars.length) {
        timers.current.push(setTimeout(step, 24));
      } else {
        setMessages((m) => m.map((msg) => (msg.id === id ? { ...msg, done: true } : msg)));
      }
    };
    step();
  };

  const reset = () => setMessages([]);
  return { messages, pushUser, streamAssistant, reset };
}

function ChatView() {
  const { t } = useTranslation();
  const { scopeId: routeScopeId } = useParams();
  const { apiClient, tenantId } = useAuth();

  const [scopeId, setScopeId] = useState(routeScopeId || '');
  const [question, setQuestion] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [hasInference, setHasInference] = useState(true);
  const [keywordOnly, setKeywordOnly] = useState(false);
  const logRef = useRef(null);
  const { messages, pushUser, streamAssistant, reset } = useTypedAnswer();

  // Detect inference availability once for the tenant.
  useEffect(() => {
    let cancelled = false;
    apiClient
      .listEndpoints(tenantId, 'Inference', { maxResults: 1 })
      .then((res) => !cancelled && setHasInference((res.items || []).some((e) => e.active !== false)))
      .catch(() => !cancelled && setHasInference(true));
    return () => {
      cancelled = true;
    };
  }, [apiClient, tenantId]);

  // Detect keyword-only retrieval for the selected scope via its guide.
  useEffect(() => {
    if (!scopeId) return;
    let cancelled = false;
    apiClient
      .getGuide(tenantId, scopeId)
      .then((g) => {
        if (cancelled) return;
        const caps = g?.capabilities || {};
        setKeywordOnly(caps.supportsSemantic === false);
      })
      .catch(() => setKeywordOnly(false));
    return () => {
      cancelled = true;
    };
  }, [apiClient, tenantId, scopeId]);

  useEffect(() => {
    reset();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scopeId]);

  useEffect(() => {
    logRef.current?.scrollTo({ top: logRef.current.scrollHeight, behavior: 'smooth' });
  }, [messages]);

  const send = useCallback(async () => {
    const q = question.trim();
    if (!q || !scopeId || busy) return;
    setError(null);
    setBusy(true);
    pushUser(q);
    setQuestion('');
    try {
      const res = await apiClient.chat(tenantId, scopeId, { question: q, topK: 8 });
      streamAssistant(res.answer || '(no answer returned)', res.citations || [], {
        retrievalMode: res.retrievalMode,
        notice: res.notice
      });
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  }, [question, scopeId, busy, apiClient, tenantId, pushUser, streamAssistant]);

  return (
    <>
      <PageHeader title={t('chat.title')} subtitle={t('chat.subtitle')} />

      {!routeScopeId && (
        <div className="filter-bar">
          <ScopePicker value={scopeId} onChange={setScopeId} />
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
            messages.map((msg, i) => (
              <div key={msg.id || i} className={`chat-msg ${msg.role}`}>
                {msg.text || (msg.role === 'assistant' && !msg.done ? '…' : '')}
                {msg.role === 'assistant' && msg.meta?.notice && (
                  <div className="notice-banner" style={{ marginTop: 8, marginBottom: 0 }}>
                    {msg.meta.notice}
                  </div>
                )}
                {msg.role === 'assistant' && msg.citations?.length > 0 && (
                  <div className="chat-citations">
                    {msg.citations.map((c, ci) => (
                      <StatusBadge key={ci} tone="info" title={`score: ${c.score}`}>
                        {c.title || c.slug}
                      </StatusBadge>
                    ))}
                  </div>
                )}
              </div>
            ))
          )}
          {busy && <div className="chat-msg assistant">{t('chat.thinking')}</div>}
        </div>

        <div className="chat-input-row" style={{ marginTop: 'var(--spacing-md)' }}>
          <textarea
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
            placeholder={t('chat.placeholder')}
            disabled={!scopeId || busy}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                send();
              }
            }}
          />
          <button className="btn-primary" onClick={send} disabled={!scopeId || busy || !question.trim()}>
            {t('chat.send')}
          </button>
        </div>
      </div>
    </>
  );
}

export default ChatView;
