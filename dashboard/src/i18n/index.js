// i18next bootstrap. Initializes before the first meaningful paint and keeps
// document lang/dir in sync with the active locale.

import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { resources } from './resources';
import {
  DEFAULT_LOCALE,
  LOCALE_STORAGE_KEY,
  SUPPORTED_LOCALES,
  normalizeLocale,
  getDirection
} from './localeRegistry';

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    fallbackLng: DEFAULT_LOCALE,
    supportedLngs: SUPPORTED_LOCALES.map((l) => l.id),
    nonExplicitSupportedLngs: true,
    load: 'currentOnly',
    interpolation: { escapeValue: false },
    detection: {
      order: ['localStorage', 'navigator'],
      lookupLocalStorage: LOCALE_STORAGE_KEY,
      caches: ['localStorage']
    }
  });

function applyLocaleToDocument(locale) {
  const normalized = normalizeLocale(locale);
  document.documentElement.lang = normalized;
  document.documentElement.dir = getDirection(normalized);
}

// Normalize whatever the detector picked, then keep the document synced.
i18n.on('languageChanged', applyLocaleToDocument);
if (i18n.language) {
  const normalized = normalizeLocale(i18n.language);
  if (normalized !== i18n.language) i18n.changeLanguage(normalized);
  else applyLocaleToDocument(normalized);
}

export default i18n;
