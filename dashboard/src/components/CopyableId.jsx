import { useState, useCallback } from 'react';
import { IconCopy, IconCheck } from './Icons';

/**
 * Consistent copy-to-clipboard control for IDs, URLs, tokens, and commands.
 * Preserves the exact value and briefly flips to a checkmark on success.
 */
function CopyableId({ value, label, truncate = true, mono = true, iconOnly = false }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(
    async (e) => {
      e.stopPropagation();
      if (!value) return;
      try {
        await navigator.clipboard.writeText(String(value));
      } catch {
        // Fallback for insecure contexts.
        const ta = document.createElement('textarea');
        ta.value = String(value);
        document.body.appendChild(ta);
        ta.select();
        try {
          document.execCommand('copy');
        } catch {
          /* ignore */
        }
        document.body.removeChild(ta);
      }
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    },
    [value]
  );

  if (value === null || value === undefined || value === '') {
    return <span className="text-muted">—</span>;
  }

  return (
    <span className="copyable-id" title={String(value)}>
      {!iconOnly && (
        <span
          className="copy-value"
          style={{ fontFamily: mono ? 'var(--font-mono)' : 'inherit', maxWidth: truncate ? '22ch' : 'none' }}
        >
          {label || value}
        </span>
      )}
      <button
        type="button"
        className={`copy-btn btn-icon${copied ? ' copied' : ''}`}
        onClick={handleCopy}
        aria-label={copied ? 'Copied' : 'Copy'}
        title={copied ? 'Copied' : 'Copy'}
      >
        {copied ? <IconCheck /> : <IconCopy />}
      </button>
    </span>
  );
}

export default CopyableId;
