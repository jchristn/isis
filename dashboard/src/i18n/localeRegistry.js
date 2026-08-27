// Central locale registry using canonical BCP 47 identifiers.
// The pseudo-locale (`en-XA`) is an expansion locale used to smoke-test layout
// against longer strings.

import { STORAGE_KEYS } from '../utils/constants';

export const LOCALE_STORAGE_KEY = STORAGE_KEYS.locale;
export const DEFAULT_LOCALE = 'en';

export const SUPPORTED_LOCALES = [
  { id: 'en', label: 'English', dir: 'ltr' },
  { id: 'en-XA', label: 'Pseudo (expansion)', dir: 'ltr' }
];

// Alias normalization: fold browser-detected variants onto supported locales.
const ALIASES = {
  'en-US': 'en',
  'en-GB': 'en',
  en_US: 'en'
};

export function normalizeLocale(locale) {
  if (!locale) return DEFAULT_LOCALE;
  if (ALIASES[locale]) return ALIASES[locale];
  if (SUPPORTED_LOCALES.some((l) => l.id === locale)) return locale;
  const base = locale.split('-')[0];
  if (SUPPORTED_LOCALES.some((l) => l.id === base)) return base;
  return DEFAULT_LOCALE;
}

export function getDirection(locale) {
  const match = SUPPORTED_LOCALES.find((l) => l.id === normalizeLocale(locale));
  return match ? match.dir : 'ltr';
}
