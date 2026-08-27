// Locale-aware formatting helpers. Every Intl usage in the dashboard flows
// through these so formatting is explicit-locale rather than browser-implicit.

import i18n from 'i18next';
import { normalizeLocale, DEFAULT_LOCALE } from './localeRegistry';

function activeLocale(explicit) {
  const locale = explicit || i18n.language || DEFAULT_LOCALE;
  // Intl does not understand the pseudo-locale; fall back to English numerics.
  const normalized = normalizeLocale(locale);
  return normalized === 'en-XA' ? 'en' : normalized;
}

export function formatNumber(value, locale, options = {}) {
  if (value === null || value === undefined || Number.isNaN(value)) return '—';
  return new Intl.NumberFormat(activeLocale(locale), options).format(value);
}

export function formatDate(value, locale) {
  if (!value) return '—';
  const d = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(d.getTime())) return '—';
  return new Intl.DateTimeFormat(activeLocale(locale), { dateStyle: 'medium' }).format(d);
}

export function formatTime(value, locale) {
  if (!value) return '—';
  const d = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(d.getTime())) return '—';
  return new Intl.DateTimeFormat(activeLocale(locale), { timeStyle: 'medium' }).format(d);
}

export function formatDateTime(value, locale) {
  if (!value) return '—';
  const d = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(d.getTime())) return '—';
  return new Intl.DateTimeFormat(activeLocale(locale), {
    dateStyle: 'medium',
    timeStyle: 'short'
  }).format(d);
}

export function formatRelativeTime(value, locale) {
  if (!value) return '—';
  const d = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(d.getTime())) return '—';
  const diffMs = d.getTime() - Date.now();
  const rtf = new Intl.RelativeTimeFormat(activeLocale(locale), { numeric: 'auto' });
  const abs = Math.abs(diffMs);
  const units = [
    ['year', 1000 * 60 * 60 * 24 * 365],
    ['month', 1000 * 60 * 60 * 24 * 30],
    ['day', 1000 * 60 * 60 * 24],
    ['hour', 1000 * 60 * 60],
    ['minute', 1000 * 60],
    ['second', 1000]
  ];
  for (const [unit, ms] of units) {
    if (abs >= ms || unit === 'second') {
      return rtf.format(Math.round(diffMs / ms), unit);
    }
  }
  return '—';
}

export function formatDuration(ms, locale) {
  if (ms === null || ms === undefined || Number.isNaN(ms)) return '—';
  if (ms < 1000) return `${formatNumber(Math.round(ms), locale)} ms`;
  const seconds = ms / 1000;
  if (seconds < 60) return `${formatNumber(Number(seconds.toFixed(1)), locale)} s`;
  const minutes = Math.floor(seconds / 60);
  const remSec = Math.round(seconds % 60);
  if (minutes < 60) return `${minutes}m ${remSec}s`;
  const hours = Math.floor(minutes / 60);
  return `${hours}h ${minutes % 60}m`;
}

export function formatBytes(value, locale) {
  if (value === null || value === undefined || Number.isNaN(value)) return '—';
  if (value === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const exponent = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1);
  const size = value / 1024 ** exponent;
  return `${formatNumber(Number(size.toFixed(exponent === 0 ? 0 : 1)), locale)} ${units[exponent]}`;
}

export function formatPercent(value, locale, fractionDigits = 1) {
  if (value === null || value === undefined || Number.isNaN(value)) return '—';
  return new Intl.NumberFormat(activeLocale(locale), {
    style: 'percent',
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits
  }).format(value);
}

export function formatList(items, locale) {
  if (!items || items.length === 0) return '—';
  return new Intl.ListFormat(activeLocale(locale), { style: 'long', type: 'conjunction' }).format(
    items.map(String)
  );
}
