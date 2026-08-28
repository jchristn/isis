import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ThemeProvider } from './context/ThemeContext';
import { AppProvider } from './context/AppContext';
import Login from './components/Login';
import Dashboard from './components/Dashboard';
import ToastStack from './components/Toast';
import HomeView from './views/HomeView';
import ScopesView from './views/ScopesView';
import ScopeDetail from './views/ScopeDetail';
import CategoriesView from './views/CategoriesView';
import MemoriesView from './views/MemoriesView';
import MemoryBrowserView from './views/MemoryBrowserView';
import SearchExplorerView from './views/SearchExplorerView';
import ChatView from './views/ChatView';
import EmbeddingEndpointsView from './views/EmbeddingEndpointsView';
import InferenceEndpointsView from './views/InferenceEndpointsView';
import CollectionsView from './views/CollectionsView';
import RequestHistoryView from './views/RequestHistoryView';
import ApiExplorerView from './views/ApiExplorerView';
import SettingsView from './views/SettingsView';
import TenantsView from './views/TenantsView';
import UsersView from './views/UsersView';
import CredentialsView from './views/CredentialsView';
import InstructionsView from './views/InstructionsView';
import './App.css';

function PrivateRoute({ children }) {
  const { isAuthenticated, isLoading } = useAuth();
  if (isLoading) return <div className="app-loading"><div className="spinner" /></div>;
  return isAuthenticated ? children : <Navigate to="/" replace />;
}

function PublicRoute({ children }) {
  const { isAuthenticated, isLoading } = useAuth();
  if (isLoading) return <div className="app-loading"><div className="spinner" /></div>;
  return !isAuthenticated ? children : <Navigate to="/dashboard/home" replace />;
}

function AppRoutes() {
  return (
    <Routes>
      <Route
        path="/"
        element={
          <PublicRoute>
            <Login />
          </PublicRoute>
        }
      />
      <Route
        path="/dashboard"
        element={
          <PrivateRoute>
            <Dashboard />
          </PrivateRoute>
        }
      >
        <Route index element={<Navigate to="home" replace />} />
        <Route path="home" element={<HomeView />} />
        <Route path="scopes" element={<ScopesView />} />
        <Route path="memories" element={<MemoryBrowserView />} />
        <Route path="instructions" element={<InstructionsView />} />
        <Route path="scopes/:scopeId" element={<ScopeDetail />} />
        <Route path="scopes/:scopeId/categories" element={<CategoriesView />} />
        <Route path="scopes/:scopeId/memories" element={<MemoriesView />} />
        <Route path="scopes/:scopeId/chat" element={<ChatView />} />
        <Route path="search" element={<SearchExplorerView />} />
        <Route path="chat" element={<ChatView />} />
        <Route path="endpoints/embedding" element={<EmbeddingEndpointsView />} />
        <Route path="endpoints/inference" element={<InferenceEndpointsView />} />
        <Route path="collections" element={<CollectionsView />} />
        <Route path="request-history" element={<RequestHistoryView />} />
        <Route path="api-explorer" element={<ApiExplorerView />} />
        <Route path="settings" element={<SettingsView />} />
        <Route path="tenants" element={<TenantsView />} />
        <Route path="users" element={<UsersView />} />
        <Route path="credentials" element={<CredentialsView />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

function App() {
  return (
    <ThemeProvider>
      <AuthProvider>
        <AppProvider>
          <BrowserRouter>
            <AppRoutes />
            <ToastStack />
          </BrowserRouter>
        </AppProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

export default App;
