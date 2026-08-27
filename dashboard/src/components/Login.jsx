import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';
import LanguageSelector from '../i18n/LanguageSelector';
import { IconSun, IconMoon } from './Icons';
import { DEFAULT_SERVER_URL, DEFAULT_ADMIN_EMAIL } from '../utils/constants';

/**
 * Three-step credential login:
 *   1. server URL + email
 *   2. pick the tenant by name (auto-selected when the email belongs to one)
 *   3. password
 */
function Login() {
  const { fetchTenantsForEmail, login } = useAuth();
  const { theme, toggleTheme } = useTheme();
  const { t } = useTranslation();
  const navigate = useNavigate();

  const [step, setStep] = useState(1);
  const [serverUrl, setServerUrl] = useState(DEFAULT_SERVER_URL);
  const [email, setEmail] = useState(DEFAULT_ADMIN_EMAIL);
  const [tenants, setTenants] = useState([]);
  const [tenantId, setTenantId] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const tenantName = tenants.find((x) => x.id === tenantId)?.name || tenantId;

  const submitStep1 = async (e) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const found = await fetchTenantsForEmail(serverUrl.trim(), email.trim());
      if (!found || found.length === 0) {
        setError(t('login.noTenants'));
        return;
      }
      setTenants(found);
      if (found.length === 1) {
        setTenantId(found[0].id);
        setStep(3);
      } else {
        setTenantId(found[0].id);
        setStep(2);
      }
    } catch (err) {
      setError(err.message || t('login.failed'));
    } finally {
      setLoading(false);
    }
  };

  const submitStep2 = (e) => {
    e.preventDefault();
    if (!tenantId) return;
    setError('');
    setStep(3);
  };

  const submitStep3 = async (e) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await login({ url: serverUrl.trim(), email: email.trim(), password, tenantId });
      navigate('/dashboard/home');
    } catch (err) {
      setError(err.message || t('login.failed'));
    } finally {
      setLoading(false);
    }
  };

  const back = () => {
    setError('');
    setPassword('');
    setStep(tenants.length > 1 ? 2 : 1);
  };

  return (
    <div className="login-page">
      <div className="login-card">
        <div className="login-brand">
          <img src="/logo.png" alt="Isis" />
          <div>
            <div className="brand-name">{t('app.name')}</div>
            <div className="brand-tag">{t('app.tagline')}</div>
          </div>
        </div>

        <div className="login-title">{t('login.title')}</div>
        <div className="login-subtitle">{t('login.subtitle')}</div>

        <ol className="login-steps" aria-hidden="true">
          <li className={step >= 1 ? 'active' : ''}>1</li>
          <li className={step >= 2 ? 'active' : ''}>2</li>
          <li className={step >= 3 ? 'active' : ''}>3</li>
        </ol>

        {step === 1 && (
          <form onSubmit={submitStep1}>
            <div className="field">
              <label htmlFor="serverUrl">{t('login.serverUrl')}</label>
              <input
                id="serverUrl"
                type="text"
                value={serverUrl}
                onChange={(e) => setServerUrl(e.target.value)}
                placeholder="http://127.0.0.1:8700"
                required
                disabled={loading}
              />
            </div>
            <div className="field">
              <label htmlFor="email">{t('login.email')}</label>
              <input
                id="email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="you@example.com"
                required
                autoComplete="username"
                disabled={loading}
              />
            </div>
            {error && <div className="error-banner" role="alert">{error}</div>}
            <button type="submit" className="btn-primary" style={{ width: '100%' }} disabled={loading}>
              {loading ? t('login.finding') : t('login.continue')}
            </button>
          </form>
        )}

        {step === 2 && (
          <form onSubmit={submitStep2}>
            <div className="field">
              <label htmlFor="tenant">{t('login.selectTenant')}</label>
              <select id="tenant" value={tenantId} onChange={(e) => setTenantId(e.target.value)} disabled={loading}>
                {tenants.map((x) => (
                  <option key={x.id} value={x.id}>
                    {x.name}
                  </option>
                ))}
              </select>
            </div>
            {error && <div className="error-banner" role="alert">{error}</div>}
            <div className="field-row">
              <button type="button" className="btn-secondary" onClick={() => { setError(''); setStep(1); }} disabled={loading}>
                {t('login.back')}
              </button>
              <button type="submit" className="btn-primary" style={{ flex: 1 }} disabled={loading || !tenantId}>
                {t('login.continue')}
              </button>
            </div>
          </form>
        )}

        {step === 3 && (
          <form onSubmit={submitStep3}>
            <div className="field">
              <label>{t('login.tenant')}</label>
              <div className="login-tenant-chip">{tenantName}</div>
            </div>
            <div className="field">
              <label htmlFor="password">{t('login.password')}</label>
              <input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder={t('login.passwordPlaceholder')}
                required
                autoComplete="current-password"
                autoFocus
                disabled={loading}
              />
            </div>
            {error && <div className="error-banner" role="alert">{error}</div>}
            <div className="field-row">
              <button type="button" className="btn-secondary" onClick={back} disabled={loading}>
                {t('login.back')}
              </button>
              <button type="submit" className="btn-primary" style={{ flex: 1 }} disabled={loading}>
                {loading ? t('login.connecting') : t('login.signIn')}
              </button>
            </div>
          </form>
        )}

        <div className="login-hint">{t('login.devHint')}</div>

        <div className="login-footer">
          <LanguageSelector compact />
          <button
            type="button"
            className="btn-icon"
            onClick={toggleTheme}
            title={t('topbar.toggleTheme')}
            aria-label={t('topbar.toggleTheme')}
          >
            {theme === 'light' ? <IconMoon /> : <IconSun />}
          </button>
        </div>
      </div>
    </div>
  );
}

export default Login;
