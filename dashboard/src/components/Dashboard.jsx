import { Outlet } from 'react-router-dom';
import { useApp } from '../context/AppContext';
import Topbar from './Topbar';
import Sidebar from './Sidebar';

/** Authenticated app shell: sidebar + topbar + scrollable workspace outlet. */
function Dashboard() {
  const { sidebarCollapsed } = useApp();
  return (
    <div className={`dashboard-shell${sidebarCollapsed ? ' collapsed' : ''}`}>
      <Sidebar />
      <Topbar />
      <main className="workspace">
        <Outlet />
      </main>
    </div>
  );
}

export default Dashboard;
