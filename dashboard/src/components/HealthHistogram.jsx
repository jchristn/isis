/**
 * Compact hand-rolled health histogram. Accepts an array of samples where each
 * item is a boolean or an object with `isHealthy`. Renders green/red bars.
 */
function HealthHistogram({ history = [], width = 90, height = 18, bars = 24 }) {
  const samples = history.slice(-bars);
  if (!samples.length) {
    return <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>—</span>;
  }
  const barW = width / bars;
  return (
    <svg className="health-histogram" width={width} height={height} role="img" aria-label="Health history">
      {samples.map((s, i) => {
        const healthy = typeof s === 'boolean' ? s : s?.isHealthy ?? s?.IsHealthy;
        return (
          <rect
            key={i}
            x={i * barW}
            y={0}
            width={Math.max(1, barW - 1)}
            height={height}
            fill={healthy ? 'var(--color-success)' : 'var(--color-danger)'}
            opacity={healthy ? 0.85 : 0.9}
          />
        );
      })}
    </svg>
  );
}

export default HealthHistogram;
