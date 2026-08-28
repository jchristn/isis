import { useMemo, useState } from 'react';
import CopyableId from './CopyableId';

/** A small braces glyph used for the pretty-print toggle. */
function IconBraces() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M7 4a2 2 0 0 0-2 2v3a2 2 0 0 1-2 2 2 2 0 0 1 2 2v3a2 2 0 0 0 2 2" />
      <path d="M17 4a2 2 0 0 1 2 2v3a2 2 0 0 0 2 2 2 2 0 0 0-2 2v3a2 2 0 0 1-2 2" />
    </svg>
  );
}

/**
 * Read-only code / JSON block with a copy control and, when the content is JSON, a pretty-print toggle
 * (so raw or minified JSON text can be formatted on demand). Objects are pretty-printed by default.
 */
// eslint-disable-next-line no-unused-vars
function CodeViewer({ value, language = 'json', maxHeight = 360 }) {
  const [pretty, setPretty] = useState(true);

  const { raw, prettyText, canPretty } = useMemo(() => {
    if (value === null || value === undefined) return { raw: '', prettyText: '', canPretty: false };
    if (typeof value !== 'string') {
      let formatted;
      try {
        formatted = JSON.stringify(value, null, 2);
      } catch {
        formatted = String(value);
      }
      return { raw: formatted, prettyText: formatted, canPretty: false };
    }
    try {
      const formatted = JSON.stringify(JSON.parse(value), null, 2);
      return { raw: value, prettyText: formatted, canPretty: formatted !== value };
    } catch {
      return { raw: value, prettyText: value, canPretty: false };
    }
  }, [value]);

  const text = pretty && canPretty ? prettyText : raw;

  return (
    <div className="code-viewer">
      <div className="code-copy">
        {canPretty && (
          <button
            type="button"
            className={`btn-icon${pretty ? ' active' : ''}`}
            onClick={() => setPretty((p) => !p)}
            title={pretty ? 'Show raw' : 'Pretty-print'}
            aria-label={pretty ? 'Show raw' : 'Pretty-print'}
            aria-pressed={pretty}
          >
            <IconBraces />
          </button>
        )}
        <CopyableId value={text} iconOnly />
      </div>
      <pre style={{ maxHeight }}>{text}</pre>
    </div>
  );
}

export default CodeViewer;
