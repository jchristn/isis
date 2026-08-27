import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';

/**
 * Loads scopes for the active tenant and renders a labeled selector. Calls
 * onChange with the selected scope id. Auto-selects the first scope.
 */
function ScopePicker({ value, onChange, onScopesLoaded }) {
  const { t } = useTranslation();
  const { apiClient, tenantId } = useAuth();
  const [scopes, setScopes] = useState([]);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .listScopes(tenantId, { maxResults: 1000 })
      .then((res) => {
        if (cancelled) return;
        const list = res.items || [];
        setScopes(list);
        onScopesLoaded?.(list);
        if (!value && list.length) onChange(list[0].id || list[0].Id);
      })
      .catch(() => setScopes([]));
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiClient, tenantId]);

  return (
    <div className="filter-field">
      <label htmlFor="scope-picker">{t('common.selectScope')}</label>
      <select id="scope-picker" value={value || ''} onChange={(e) => onChange(e.target.value)}>
        {!scopes.length && <option value="">—</option>}
        {scopes.map((s) => (
          <option key={s.id || s.Id} value={s.id || s.Id}>
            {s.name || s.id || s.Id}
          </option>
        ))}
      </select>
    </div>
  );
}

export default ScopePicker;
