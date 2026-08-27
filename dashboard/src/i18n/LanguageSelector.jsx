import { useTranslation } from 'react-i18next';
import { SUPPORTED_LOCALES } from './localeRegistry';

/** Shared locale selector, surfaced in login, topbar, and settings. */
function LanguageSelector({ compact = false }) {
  const { i18n, t } = useTranslation();

  return (
    <select
      className={`language-selector${compact ? ' compact' : ''}`}
      value={SUPPORTED_LOCALES.some((l) => l.id === i18n.language) ? i18n.language : 'en'}
      onChange={(e) => i18n.changeLanguage(e.target.value)}
      aria-label={t('topbar.language')}
      title={t('topbar.language')}
    >
      {SUPPORTED_LOCALES.map((locale) => (
        <option key={locale.id} value={locale.id}>
          {locale.label}
        </option>
      ))}
    </select>
  );
}

export default LanguageSelector;
