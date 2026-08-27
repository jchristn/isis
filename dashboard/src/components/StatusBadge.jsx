/** Small status pill. Color is always paired with a text label. */
function StatusBadge({ tone = 'neutral', children, title }) {
  return (
    <span className={`badge badge-${tone}`} title={title}>
      {children}
    </span>
  );
}

/** Convenience: boolean active/inactive. */
export function ActiveBadge({ active }) {
  return <StatusBadge tone={active ? 'success' : 'neutral'}>{active ? 'Active' : 'Inactive'}</StatusBadge>;
}

export default StatusBadge;
