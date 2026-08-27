import { useState, useEffect, useCallback, useMemo } from 'react';
import { flattenOpenApiSpec, substitutePathParams, getParameterDefault, getRequestBodyTemplate } from '../utils/openApi';
import { STORAGE_KEYS } from '../utils/constants';

const HISTORY_CAP = 12;

/**
 * Holds the API Explorer's state: the loaded spec, the selected operation, the
 * composed request fields, the last response, and a capped localStorage history.
 * Authentication is inherited from the dashboard's ApiClient.
 */
export function useApiExplorer(apiClient) {
  const [spec, setSpec] = useState(null);
  const [specError, setSpecError] = useState(null);
  const [specLoading, setSpecLoading] = useState(true);
  const [operationId, setOperationId] = useState(null);
  const [pathParams, setPathParams] = useState({});
  const [queryParams, setQueryParams] = useState({});
  const [headers, setHeaders] = useState({});
  const [body, setBody] = useState('');
  const [response, setResponse] = useState(null);
  const [executing, setExecuting] = useState(false);
  const [history, setHistory] = useState(() => {
    try {
      return JSON.parse(localStorage.getItem(STORAGE_KEYS.explorerHistory) || '[]');
    } catch {
      return [];
    }
  });

  const operations = useMemo(() => flattenOpenApiSpec(spec), [spec]);
  const operation = useMemo(
    () => operations.find((op) => op.id === operationId) || null,
    [operations, operationId]
  );

  useEffect(() => {
    let cancelled = false;
    setSpecLoading(true);
    setSpecError(null);
    apiClient
      .getOpenApiSpec()
      .then((doc) => {
        if (cancelled) return;
        setSpec(doc);
      })
      .catch((err) => {
        if (cancelled) return;
        setSpecError(err.message || 'Failed to load OpenAPI document');
      })
      .finally(() => {
        if (!cancelled) setSpecLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [apiClient]);

  // Seed request fields when the operation changes.
  const selectOperation = useCallback(
    (id) => {
      setOperationId(id);
      setResponse(null);
      const op = operations.find((o) => o.id === id);
      if (!op) return;
      const nextPath = {};
      const nextQuery = {};
      for (const p of op.parameters) {
        if (p.in === 'path') nextPath[p.name] = getParameterDefault(p);
        else if (p.in === 'query') nextQuery[p.name] = getParameterDefault(p);
      }
      setPathParams(nextPath);
      setQueryParams(nextQuery);
      setHeaders({});
      setBody(getRequestBodyTemplate(op.requestBody, spec));
    },
    [operations, spec]
  );

  const persistHistory = useCallback((entry) => {
    setHistory((prev) => {
      const next = [entry, ...prev].slice(0, HISTORY_CAP);
      try {
        localStorage.setItem(STORAGE_KEYS.explorerHistory, JSON.stringify(next));
      } catch {
        // ignore
      }
      return next;
    });
  }, []);

  const deleteHistory = useCallback((index) => {
    setHistory((prev) => {
      const next = prev.filter((_, i) => i !== index);
      try {
        localStorage.setItem(STORAGE_KEYS.explorerHistory, JSON.stringify(next));
      } catch {
        // ignore
      }
      return next;
    });
  }, []);

  const loadHistory = useCallback((entry) => {
    setOperationId(entry.operationId);
    setPathParams(entry.pathParams || {});
    setQueryParams(entry.queryParams || {});
    setHeaders(entry.headers || {});
    setBody(entry.body || '');
    setResponse(null);
  }, []);

  const execute = useCallback(async () => {
    if (!operation) return;
    setExecuting(true);
    const resolvedPath = substitutePathParams(operation.path, pathParams);
    const start = performance.now();
    try {
      const raw = await apiClient.executeExplorer({
        method: operation.method,
        path: resolvedPath,
        query: queryParams,
        headers,
        body: ['GET', 'HEAD'].includes(operation.method) ? undefined : body
      });
      const text = await raw.text();
      const respHeaders = {};
      raw.headers.forEach((value, key) => {
        respHeaders[key] = value;
      });
      const parsed = {
        status: raw.status,
        statusText: raw.statusText,
        headers: respHeaders,
        body: text,
        durationMs: performance.now() - start,
        byteLength: new Blob([text]).size
      };
      setResponse(parsed);
      persistHistory({
        operationId: operation.id,
        method: operation.method,
        path: operation.path,
        pathParams,
        queryParams,
        headers,
        body,
        status: parsed.status,
        at: new Date().toISOString()
      });
    } catch (err) {
      setResponse({
        status: 0,
        statusText: 'Request failed',
        headers: {},
        body: err.message || String(err),
        durationMs: performance.now() - start,
        byteLength: 0,
        error: true
      });
    } finally {
      setExecuting(false);
    }
  }, [apiClient, operation, pathParams, queryParams, headers, body, persistHistory]);

  return {
    spec,
    specError,
    specLoading,
    operations,
    operation,
    operationId,
    selectOperation,
    pathParams,
    setPathParams,
    queryParams,
    setQueryParams,
    headers,
    setHeaders,
    body,
    setBody,
    response,
    executing,
    execute,
    history,
    loadHistory,
    deleteHistory
  };
}

export default useApiExplorer;
