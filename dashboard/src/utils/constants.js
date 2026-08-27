// Application-wide constants for the Isis dashboard.

export const STORAGE_KEYS = {
  serverUrl: 'isis_server_url',
  token: 'isis_token',
  tenantId: 'isis_tenant_id',
  theme: 'isis_theme',
  locale: 'isis.locale',
  explorerHistory: 'isis_api_explorer_history'
};

export const DEFAULT_SERVER_URL =
  typeof __DEFAULT_SERVER_URL__ !== 'undefined' ? __DEFAULT_SERVER_URL__ : 'http://127.0.0.1:8700';
export const DEFAULT_ADMIN_EMAIL =
  typeof __DEFAULT_ADMIN_EMAIL__ !== 'undefined' ? __DEFAULT_ADMIN_EMAIL__ : 'admin@isis.local';
export const DEFAULT_TENANT_ID =
  typeof __DEFAULT_TENANT_ID__ !== 'undefined' ? __DEFAULT_TENANT_ID__ : 'ten_default';

export const GITHUB_URL = 'https://github.com/jchristn/isis';

export const PAGE_SIZE_OPTIONS = [10, 25, 50, 100, 250, 500, 1000];
export const DEFAULT_PAGE_SIZE = 25;

export const STORE_PROVIDERS = ['RecallDb', 'Verbex', 'Filesystem'];
export const FILESYSTEM_LAYOUTS = ['SingleFile', 'Hierarchy'];
export const MEMORY_TYPES = ['User', 'Feedback', 'Project', 'Reference'];
export const SEARCH_MODES = ['Keyword', 'Semantic', 'Hybrid'];
export const ENDPOINT_KINDS = ['Embedding', 'Inference'];
export const API_FORMATS = ['Ollama', 'OpenAI', 'VLlm', 'Gemini'];
export const HEALTH_METHODS = ['GET', 'HEAD'];

// External observability services surfaced on the Home page (local dev defaults).
export const EXTERNAL_SERVICES = [
  { key: 'grafana', name: 'Grafana', url: 'http://127.0.0.1:3000', creds: 'admin / admin' },
  { key: 'prometheus', name: 'Prometheus', url: 'http://127.0.0.1:9090', creds: '—' },
  { key: 'tempo', name: 'Tempo', url: 'http://127.0.0.1:3200', creds: '—' },
  { key: 'recalldb', name: 'RecallDB Console', url: 'http://127.0.0.1:8601', creds: '—' }
];
