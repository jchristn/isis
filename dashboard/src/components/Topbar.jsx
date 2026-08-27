import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';
import { useApp } from '../context/AppContext';
import LanguageSelector from '../i18n/LanguageSelector';
import CopyableId from './CopyableId';
import { IconSun, IconMoon, IconGithub, IconLogout, IconMenu } from './Icons';
import { GITHUB_URL } from '../utils/constants';

/**
 * Topbar: server context + principal/role on the left, health/live status and
 * utility actions on the right. Health is polled every 20s.
 */
function Topbar() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { logout, serverUrl, apiClient, whoami, isAdmin, isTenantAdmin, tenantId } = useAuth();
  const { theme, toggleTheme } = useTheme();
  const { toggleSidebar } = useApp();

  const [health, setHealth] = useState('unknown');
  const [version, setVersion] = useState(null);

  const poll = useCallback(async () => {
    if (!apiClient) return;
    try {
      const h = await apiClient.health();
      setHealth(h?.status && String(h.status).toLowerCase().includes('down') ? 'down' : 'up');
    } catch {
      setHealth('down');
    }
  }, [apiClient]);

  useEffect(() => {
    poll();
    const id = setInterval(poll, 20000);
    return () => clearInterval(id);
  }, [poll]);

  useEffect(() => {
    let cancelled = false;
    apiClient
      ?.serverInfo()
      .then((info) => !cancelled && setVersion(info?.version))
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [apiClient]);

  const handleLogout = () => {
    logout();
    navigate('/');
  };

  const roleLabel = isAdmin
    ? t('topbar.admin')
    : isTenantAdmin
      ? t('topbar.tenantAdmin')
      : t('topbar.user');

  return (
    <header className="topbar">
      <div className="topbar-left">
        <button className="btn-icon" onClick={toggleSidebar} aria-label="Toggle sidebar">
          <IconMenu />
        </button>
        <span className="topbar-chip" title={serverUrl}>
          <CopyableId value={serverUrl} truncate mono={false} />
        </span>
        {version && (
          <span className="topbar-chip hide-narrow">
            {t('topbar.version')}: <strong>{version}</strong>
          </span>
        )}
        <span className="topbar-chip hide-narrow">
          {t('topbar.tenant')}: <strong>{whoami?.tenantId || tenantId}</strong>
        </span>
      </div>

      <div className="topbar-right">
        <span className="topbar-chip" title={whoami?.principalName || ''}>
          <span className={`health-dot ${health}`} />
          <strong>{roleLabel}</strong>
        </span>
        <LanguageSelector compact />
        <a
          className="btn-icon"
          href={GITHUB_URL}
          target="_blank"
          rel="noopener noreferrer"
          title={t('topbar.github')}
          aria-label={t('topbar.github')}
        >
          <IconGithub />
        </a>
        <button className="btn-icon" onClick={toggleTheme} title={t('topbar.toggleTheme')} aria-label={t('topbar.toggleTheme')}>
          {theme === 'light' ? <IconMoon /> : <IconSun />}
        </button>
        <button className="btn-icon" onClick={handleLogout} title={t('topbar.logout')} aria-label={t('topbar.logout')}>
          <IconLogout />
        </button>
      </div>
    </header>
  );
}

export default Topbar;
