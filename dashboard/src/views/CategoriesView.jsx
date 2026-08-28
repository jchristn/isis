import { useParams, Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import CategoriesPanel from '../components/CategoriesPanel';

/** Scope drill-in: manage the categories of a single scope (tenant from the session). */
function CategoriesView() {
  const { t } = useTranslation();
  const { scopeId } = useParams();
  const { tenantId } = useAuth();

  return (
    <>
      <PageHeader
        title={t('categories.title')}
        subtitle={t('categories.subtitle')}
        breadcrumbs={
          <>
            <Link to="/dashboard/scopes">{t('scopes.title')}</Link> /{' '}
            <Link to={`/dashboard/scopes/${scopeId}`}>{scopeId}</Link> / {t('categories.title')}
          </>
        }
      />
      <CategoriesPanel tenantId={tenantId} scopeId={scopeId} />
    </>
  );
}

export default CategoriesView;
