/** Single KPI tile. Clickable when a resource is navigable. */
function KpiCard({ label, value, sub, onClick, tone }) {
  return (
    <div
      className={`kpi-card${onClick ? ' clickable' : ''}`}
      onClick={onClick}
      role={onClick ? 'button' : undefined}
      tabIndex={onClick ? 0 : undefined}
      onKeyDown={onClick ? (e) => (e.key === 'Enter' || e.key === ' ') && onClick() : undefined}
    >
      <span className="kpi-label">{label}</span>
      <span className="kpi-value" style={tone ? { color: `var(--color-${tone})` } : undefined}>
        {value}
      </span>
      {sub && <span className="kpi-sub">{sub}</span>}
    </div>
  );
}

export default KpiCard;
