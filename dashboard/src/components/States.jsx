import { useTranslation } from 'react-i18next';

/** Centered loading state. */
export function LoadingState({ label }) {
  const { t } = useTranslation();
  return (
    <div className="state-block">
      <div className="spinner" />
      <div className="state-desc">{label || t('states.loading')}</div>
    </div>
  );
}

/** Error state with a retry path. */
export function ErrorState({ title, message, onRetry }) {
  const { t } = useTranslation();
  return (
    <div className="state-block error">
      <div className="state-title">{title || t('states.errorTitle')}</div>
      {message && <div className="state-desc">{message}</div>}
      {onRetry && (
        <button className="btn-secondary" onClick={onRetry}>
          {t('common.retry')}
        </button>
      )}
    </div>
  );
}

/** Empty state that explains what is missing and what to do next. */
export function EmptyState({ title, message, action }) {
  const { t } = useTranslation();
  return (
    <div className="state-block">
      <div className="state-title">{title || t('states.emptyTitle')}</div>
      {message && <div className="state-desc">{message}</div>}
      {action}
    </div>
  );
}

/** Dismissible inline error banner. */
export function ErrorBanner({ message, onDismiss, onRetry }) {
  const { t } = useTranslation();
  if (!message) return null;
  return (
    <div className="error-banner" role="alert">
      <span>{message}</span>
      <span style={{ display: 'flex', gap: '0.5rem' }}>
        {onRetry && (
          <button className="btn-sm btn-ghost" onClick={onRetry}>
            {t('common.retry')}
          </button>
        )}
        {onDismiss && (
          <button className="btn-sm btn-ghost" onClick={onDismiss}>
            {t('common.close')}
          </button>
        )}
      </span>
    </div>
  );
}
