/** Route header: optional breadcrumbs, title, subtitle, and action slot. */
function PageHeader({ title, subtitle, actions, breadcrumbs }) {
  return (
    <div className="section">
      {breadcrumbs && <div className="breadcrumbs">{breadcrumbs}</div>}
      <div className="page-header">
        <div>
          <h1>{title}</h1>
          {subtitle && <p className="page-subtitle">{subtitle}</p>}
        </div>
        {actions && <div className="page-actions">{actions}</div>}
      </div>
    </div>
  );
}

export default PageHeader;
