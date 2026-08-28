import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import CopyableId from '../components/CopyableId';
import StatusBadge from '../components/StatusBadge';
import { EmptyState } from '../components/States';

/**
 * Surfaces the RecallDB collections that back this tenant's scopes (the scope →
 * collection bindings Isis owns), with a read-only table and an empty state.
 */
function CollectionsView() {
  const { t } = useTranslation();
  const { apiClient, tenantId } = useAuth();
  const [bindings, setBindings] = useState([]);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await apiClient.listScopes(tenantId, { maxResults: 1000 });
      setBindings((res.items || []).filter((s) => s.storeProvider === 'RecallDb'));
    } catch {
      setBindings([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, tenantId]);

  useEffect(() => {
    load();
  }, [load]);

  const columns = [
    {
      key: 'recallCollectionId',
      label: t('collections.title'),
      pinned: true,
      cellClass: 'cell-id',
      render: (s) => (s.recallCollectionId ? <CopyableId value={s.recallCollectionId} /> : <StatusBadge tone="warning">unbound</StatusBadge>)
    },
    { key: 'name', label: t('scopes.title'), pinned: true, render: (s) => s.name || s.id },
    {
      key: 'dimensionality',
      label: t('collections.dimension'),
      numeric: true,
      render: (s) => s.dimensionality ?? '—'
    },
    {
      key: 'scopeId',
      label: 'Scope ID',
      cellClass: 'cell-id',
      render: (s) => <CopyableId value={s.id || s.Id} />
    }
  ];

  return (
    <>
      <PageHeader title={t('collections.title')} subtitle={t('collections.subtitle')} />

      {bindings.length > 0 && !loading ? (
        <DataTable
          tableId="collections"
          columns={columns}
          data={bindings}
          loading={loading}
          onRefresh={load}
          emptyMessage={t('collections.empty')}
        />
      ) : (
        <EmptyState title={t('collections.title')} message={t('collections.empty')} />
      )}
    </>
  );
}

export default CollectionsView;
