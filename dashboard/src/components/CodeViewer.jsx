import { useMemo } from 'react';
import CopyableId from './CopyableId';

/** Read-only code / JSON block with a copy control. */
function CodeViewer({ value, language = 'json', maxHeight = 360 }) {
  const text = useMemo(() => {
    if (value === null || value === undefined) return '';
    if (typeof value === 'string') {
      if (language === 'json') {
        try {
          return JSON.stringify(JSON.parse(value), null, 2);
        } catch {
          return value;
        }
      }
      return value;
    }
    try {
      return JSON.stringify(value, null, 2);
    } catch {
      return String(value);
    }
  }, [value, language]);

  return (
    <div className="code-viewer">
      <div className="code-copy">
        <CopyableId value={text} label="" truncate={false} />
      </div>
      <pre style={{ maxHeight }}>{text}</pre>
    </div>
  );
}

export default CodeViewer;
