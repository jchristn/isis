import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import {
  IconHome, IconLayers, IconTag, IconNote, IconSearch, IconChat,
  IconCpu, IconDatabase, IconHistory, IconPlay, IconGear, IconUsers, IconKey
} from './Icons';

const IconBook = IconNote;

/**
 * Grouped, workflow-oriented navigation. Administration is only shown to admins
 * (client-side gating is UX only; the server still enforces authorization).
 */
function Sidebar() {
  const { t } = useTranslation();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { sidebarCollapsed } = useApp();

  const groups = [
    {
      label: t('nav.groups.memory'),
      items: [
        { to: '/dashboard/home', label: t('nav.home'), icon: IconHome },
        { to: '/dashboard/scopes', label: t('nav.scopes'), icon: IconLayers },
        { to: '/dashboard/memories', label: t('nav.memories'), icon: IconNote },
        { to: '/dashboard/instructions', label: t('nav.instructions'), icon: IconBook }
      ]
    },
    {
      label: t('nav.groups.recall'),
      items: [
        { to: '/dashboard/search', label: t('nav.search'), icon: IconSearch },
        { to: '/dashboard/chat', label: t('nav.chat'), icon: IconChat }
      ]
    },
    {
      label: t('nav.groups.inference'),
      items: [
        { to: '/dashboard/endpoints/embedding', label: t('nav.embedding'), icon: IconCpu },
        { to: '/dashboard/endpoints/inference', label: t('nav.inference'), icon: IconCpu }
      ]
    },
    {
      label: t('nav.groups.collections'),
      items: [{ to: '/dashboard/collections', label: t('nav.collectionsRecall'), icon: IconDatabase }]
    },
    {
      label: t('nav.groups.observability'),
      items: [
        { to: '/dashboard/request-history', label: t('nav.requestHistory'), icon: IconHistory },
        { to: '/dashboard/api-explorer', label: t('nav.apiExplorer'), icon: IconPlay }
      ]
    },
    {
      label: t('nav.groups.system'),
      items: [{ to: '/dashboard/settings', label: t('nav.settings'), icon: IconGear }]
    }
  ];

  if (isAdmin || isTenantAdmin) {
    const adminItems = [{ to: '/dashboard/users', label: t('nav.users'), icon: IconUsers },
      { to: '/dashboard/credentials', label: t('nav.credentials'), icon: IconKey }];
    if (isAdmin) adminItems.unshift({ to: '/dashboard/tenants', label: t('nav.tenants'), icon: IconLayers });
    groups.push({
      label: t('nav.groups.administration'),
      items: adminItems
    });
  }

  return (
    <aside className={`sidebar${sidebarCollapsed ? ' collapsed' : ''}`}>
      <div className="sidebar-brand">
        <img src="/logo.png" alt="Isis" />
        <div className="brand-text">
          <span className="brand-name">{t('app.name')}</span>
          <span className="brand-tag">{t('app.tagline')}</span>
        </div>
      </div>
      <nav className="sidebar-nav" aria-label="Primary">
        {groups.map((group) => (
          <div className="nav-group" key={group.label}>
            <div className="nav-group-label">{group.label}</div>
            {group.items.map((item) => {
              const Icon = item.icon;
              return (
                <NavLink
                  key={item.to}
                  to={item.to}
                  className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}
                  title={item.label}
                >
                  <span className="nav-icon">
                    <Icon />
                  </span>
                  <span className="nav-label">{item.label}</span>
                </NavLink>
              );
            })}
          </div>
        ))}
      </nav>
      <div className="sidebar-footer">
        <span className="nav-label">Isis 0.1.0 · ALPHA</span>
      </div>
    </aside>
  );
}

export default Sidebar;
