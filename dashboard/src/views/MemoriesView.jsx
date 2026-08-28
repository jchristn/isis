import { useParams, Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import MemoriesPanel from '../components/MemoriesPanel';

/** Scope drill-in: manage the memories of a single scope (tenant from the session). */
function MemoriesView() {
  const { t } = useTranslation();
  const { scopeId } = useParams();
  const { tenantId } = useAuth();

  return (
    <>
      <PageHeader
        title={t('memories.title')}
        subtitle={t('memories.subtitle')}
        breadcrumbs={
          <>
            <Link to="/dashboard/scopes">{t('scopes.title')}</Link> /{' '}
            <Link to={`/dashboard/scopes/${scopeId}`}>{scopeId}</Link> / {t('memories.title')}
          </>
        }
      />
      <MemoriesPanel tenantId={tenantId} scopeId={scopeId} />
    </>
  );
}

export default MemoriesView;
