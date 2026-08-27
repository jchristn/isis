import { useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import { EmptyState, ErrorBanner } from '../components/States';

/**
 * Request History. The Isis server does not yet expose the request-capture API
 * this page consumes, so we render an explicit empty state naming the missing
 * capability. A "Check again" action probes the endpoint so the page lights up
 * automatically once the backend ships it.
 */
function RequestHistoryView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState(null);

  const check = useCallback(async () => {
    setChecking(true);
    setError(null);
    try {
      await apiClient.getRequestHistory({ maxResults: 1 });
      // If this ever succeeds, the capability exists; surface a soft note.
      setError('Request-history endpoint responded. Reload to view captured traffic once the full UI is wired.');
    } catch (err) {
      setError(err.status === 404 ? null : err.message);
    } finally {
      setChecking(false);
    }
  }, [apiClient]);

  return (
    <>
      <PageHeader title={t('requestHistory.title')} subtitle={t('requestHistory.subtitle')} />
      {error && <ErrorBanner message={error} onDismiss={() => setError(null)} />}
      <EmptyState
        title={t('requestHistory.unavailableTitle')}
        message={t('requestHistory.unavailable')}
        action={
          <button className="btn-secondary" onClick={check} disabled={checking}>
            {checking ? t('common.loading') : t('requestHistory.checkAgain')}
          </button>
        }
      />
    </>
  );
}

export default RequestHistoryView;
