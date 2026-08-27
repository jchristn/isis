import { useTranslation } from 'react-i18next';

/**
 * Generic filter row. Renders labeled fields from a declarative spec and exposes
 * Apply / Clear controls. Fields: { name, label, type, options?, placeholder? }.
 */
function FilterBar({ fields = [], values = {}, onChange, onApply, onClear, extra = null }) {
  const { t } = useTranslation();

  return (
    <div className="filter-bar">
      {fields.map((field) => (
        <div className="filter-field" key={field.name}>
          <label htmlFor={`filter-${field.name}`}>{field.label}</label>
          {field.type === 'select' ? (
            <select
              id={`filter-${field.name}`}
              value={values[field.name] ?? ''}
              onChange={(e) => onChange(field.name, e.target.value)}
            >
              {(field.options || []).map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          ) : (
            <input
              id={`filter-${field.name}`}
              type={field.type || 'text'}
              placeholder={field.placeholder}
              value={values[field.name] ?? ''}
              onChange={(e) => onChange(field.name, e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && onApply?.()}
            />
          )}
        </div>
      ))}
      {extra}
      <div className="filter-field" style={{ minWidth: 'auto', flexDirection: 'row', gap: '0.5rem', alignItems: 'flex-end' }}>
        {onApply && (
          <button className="btn-primary btn-sm" onClick={onApply}>
            {t('common.apply')}
          </button>
        )}
        {onClear && (
          <button className="btn-secondary btn-sm" onClick={onClear}>
            {t('common.clear')}
          </button>
        )}
      </div>
    </div>
  );
}

export default FilterBar;
