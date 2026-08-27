import { createContext, useContext, useState, useCallback, useMemo } from 'react';

const AppContext = createContext(null);

/** App-wide UI state: sidebar collapse + transient toast notifications. */
export function AppProvider({ children }) {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const [toasts, setToasts] = useState([]);

  const toggleSidebar = useCallback(() => setSidebarCollapsed((prev) => !prev), []);

  const removeToast = useCallback((id) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const addToast = useCallback(
    (message, type = 'info', duration = 4000) => {
      const id = Date.now() + Math.random();
      setToasts((prev) => [...prev, { id, message, type }]);
      if (duration > 0) setTimeout(() => removeToast(id), duration);
      return id;
    },
    [removeToast]
  );

  const value = useMemo(
    () => ({ sidebarCollapsed, toggleSidebar, toasts, addToast, removeToast }),
    [sidebarCollapsed, toggleSidebar, toasts, addToast, removeToast]
  );

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}

export function useApp() {
  const context = useContext(AppContext);
  if (!context) throw new Error('useApp must be used within an AppProvider');
  return context;
}

export default AppContext;
