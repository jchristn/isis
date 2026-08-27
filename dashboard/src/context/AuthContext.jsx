import { createContext, useContext, useState, useEffect, useCallback } from 'react';
import ApiClient from '../utils/api';
import { STORAGE_KEYS, DEFAULT_TENANT_ID } from '../utils/constants';

/**
 * AuthContext holds a session token (from email/password login) plus the
 * resolved principal (whoami). Login is a 3-step flow: server URL + email →
 * pick tenant by name → password. The token is persisted to localStorage so
 * the session survives a refresh and is validated on mount via /whoami.
 */
const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [apiClient, setApiClient] = useState(null);
  const [serverUrl, setServerUrl] = useState('');
  const [tenantId, setTenantId] = useState(DEFAULT_TENANT_ID);
  const [whoami, setWhoami] = useState(null);

  const clearStorage = () => {
    localStorage.removeItem(STORAGE_KEYS.serverUrl);
    localStorage.removeItem(STORAGE_KEYS.token);
    localStorage.removeItem(STORAGE_KEYS.tenantId);
  };

  // Restore a saved session on mount by validating the token via /whoami.
  useEffect(() => {
    const savedUrl = localStorage.getItem(STORAGE_KEYS.serverUrl);
    const savedToken = localStorage.getItem(STORAGE_KEYS.token);
    const savedTenant = localStorage.getItem(STORAGE_KEYS.tenantId) || DEFAULT_TENANT_ID;

    if (savedUrl && savedToken) {
      const client = new ApiClient(savedUrl, { token: savedToken, tenantId: savedTenant });
      client
        .whoami()
        .then((identity) => {
          setServerUrl(savedUrl);
          setTenantId(identity?.tenantId || savedTenant);
          setApiClient(client);
          setWhoami(identity);
          setIsAuthenticated(true);
        })
        .catch(() => clearStorage())
        .finally(() => setIsLoading(false));
    } else {
      setIsLoading(false);
    }
  }, []);

  /** Pre-auth helper for the login wizard: resolve which tenants an email belongs to. */
  const fetchTenantsForEmail = useCallback(async (url, email) => {
    const client = new ApiClient(url);
    return client.tenantsForEmail(email);
  }, []);

  const login = useCallback(async ({ url, email, password, tenantId: tid }) => {
    const client = new ApiClient(url, { tenantId: tid });
    const result = await client.login(email, password, tid);
    client.token = result.token;
    client.tenantId = result.tenantId || tid;

    let identity = null;
    try {
      identity = await client.whoami();
    } catch {
      identity = null;
    }

    localStorage.setItem(STORAGE_KEYS.serverUrl, url);
    localStorage.setItem(STORAGE_KEYS.token, result.token);
    localStorage.setItem(STORAGE_KEYS.tenantId, result.tenantId || tid);

    setServerUrl(url);
    setTenantId(result.tenantId || tid);
    setApiClient(client);
    setWhoami(identity);
    setIsAuthenticated(true);
  }, []);

  const logout = useCallback(() => {
    // Best-effort server-side revocation; never block logout on it.
    try {
      apiClient?.logout?.().catch(() => {});
    } catch {
      // ignore
    }
    clearStorage();
    setServerUrl('');
    setApiClient(null);
    setWhoami(null);
    setIsAuthenticated(false);
  }, [apiClient]);

  const updateTenantId = useCallback(
    (newTenantId) => {
      if (!newTenantId || !apiClient) return;
      apiClient.tenantId = newTenantId;
      localStorage.setItem(STORAGE_KEYS.tenantId, newTenantId);
      setTenantId(newTenantId);
    },
    [apiClient]
  );

  // Force logout on repeated 401/403 from the client.
  useEffect(() => {
    const handler = () => {
      if (isAuthenticated) logout();
    };
    window.addEventListener('auth:unauthorized', handler);
    return () => window.removeEventListener('auth:unauthorized', handler);
  }, [isAuthenticated, logout]);

  const isAdmin = whoami?.isAdmin === true;
  const isTenantAdmin = whoami?.isTenantAdmin === true;

  const value = {
    isAuthenticated,
    isLoading,
    apiClient,
    serverUrl,
    tenantId,
    whoami,
    isAdmin,
    isTenantAdmin,
    fetchTenantsForEmail,
    login,
    logout,
    updateTenantId
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within an AuthProvider');
  return context;
}

export default AuthContext;
