import { useState, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { formatNumber } from '../i18n/formatters';

/**
 * Hand-rolled stacked SVG bar chart (no charting library).
 *
 * Renders `success` stacked on `failure` per bucket, a Y axis with ~3 ticks,
 * distributed X labels, and a portal-rendered hover tooltip. Buckets:
 *   { label, success, failure?, total?, tooltip?: [{k, v}] }
 */
function ActivityChart({ buckets = [], height = 220, onBucketClick = null, emptyLabel }) {
  const { t, i18n } = useTranslation();
  const [hover, setHover] = useState(null);

  const width = 720;
  const padding = { top: 16, right: 12, bottom: 28, left: 44 };
  const chartW = width - padding.left - padding.right;
  const chartH = height - padding.top - padding.bottom;

  const maxVal = Math.max(
    1,
    ...buckets.map((b) => (b.total != null ? b.total : (b.success || 0) + (b.failure || 0)))
  );

  const barGap = 2;
  const barW = buckets.length > 0 ? Math.max(1, chartW / buckets.length - barGap) : 0;

  const yTicks = [0, 0.5, 1].map((f) => ({ f, value: Math.round(maxVal * f) }));
  const labelStride = Math.max(1, Math.ceil(buckets.length / 8));

  const handleMove = useCallback((e, bucket) => {
    setHover({ x: e.clientX, y: e.clientY, bucket });
  }, []);

  if (!buckets.length) {
    return (
      <div className="chart-frame">
        <div className="state-block" style={{ padding: 'var(--spacing-lg)' }}>
          <div className="state-desc">{emptyLabel || t('states.emptyTitle')}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="chart-frame">
      <svg
        width="100%"
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        preserveAspectRatio="xMidYMid meet"
      >
        {/* Y axis grid + ticks */}
        {yTicks.map((tick) => {
          const y = padding.top + chartH - tick.f * chartH;
          return (
            <g key={tick.f}>
              <line
                x1={padding.left}
                x2={width - padding.right}
                y1={y}
                y2={y}
                stroke="var(--color-border)"
                strokeWidth="1"
              />
              <text x={padding.left - 8} y={y + 4} textAnchor="end" fontSize="10" fill="var(--color-text-muted)">
                {formatNumber(tick.value, i18n.language)}
              </text>
            </g>
          );
        })}

        {/* Bars */}
        {buckets.map((b, i) => {
          const success = b.success || 0;
          const failure = b.failure || 0;
          const total = b.total != null ? b.total : success + failure;
          const x = padding.left + i * (chartW / buckets.length) + barGap / 2;
          const totalH = (total / maxVal) * chartH;
          const failH = (failure / maxVal) * chartH;
          const succH = totalH - failH;
          const yFail = padding.top + chartH - failH;
          const ySucc = yFail - succH;
          return (
            <g
              key={i}
              style={{ cursor: onBucketClick ? 'pointer' : 'default' }}
              onMouseMove={(e) => handleMove(e, b)}
              onMouseLeave={() => setHover(null)}
              onClick={() => onBucketClick?.(b)}
            >
              {/* invisible hit area */}
              <rect x={x} y={padding.top} width={barW} height={chartH} fill="transparent" />
              {failH > 0 && (
                <rect x={x} y={yFail} width={barW} height={failH} fill="var(--color-danger)" rx="1" />
              )}
              <rect x={x} y={ySucc} width={barW} height={Math.max(0, succH)} fill="var(--color-primary)" rx="1" />
            </g>
          );
        })}

        {/* X axis labels */}
        {buckets.map((b, i) =>
          i % labelStride === 0 ? (
            <text
              key={`lbl-${i}`}
              x={padding.left + i * (chartW / buckets.length) + barW / 2}
              y={height - 10}
              textAnchor="middle"
              fontSize="10"
              fill="var(--color-text-muted)"
            >
              {b.label}
            </text>
          ) : null
        )}
      </svg>

      {hover &&
        createPortal(
          <div className="chart-tooltip" style={{ top: hover.y + 12, left: hover.x + 12 }}>
            <div style={{ fontWeight: 600, marginBottom: 4 }}>{hover.bucket.label}</div>
            {(hover.bucket.tooltip || [
              { k: t('common.total') || 'Total', v: hover.bucket.total ?? (hover.bucket.success || 0) + (hover.bucket.failure || 0) }
            ]).map((row) => (
              <div className="tt-row" key={row.k}>
                <span style={{ color: 'var(--color-text-muted)' }}>{row.k}</span>
                <span>{typeof row.v === 'number' ? formatNumber(row.v, i18n.language) : row.v}</span>
              </div>
            ))}
          </div>,
          document.body
        )}
    </div>
  );
}

export default ActivityChart;
