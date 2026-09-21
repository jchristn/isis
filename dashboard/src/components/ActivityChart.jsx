import { useState, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { formatNumber } from '../i18n/formatters';

/**
 * Hand-rolled stacked SVG bar chart (no charting library).
 *
 * Two modes:
 *  - Legacy (default): renders `success` stacked on `failure` per bucket. Buckets:
 *      { label, success, failure?, total?, tooltip?: [{k, v}] }
 *  - Multi-series: pass `series` = [{ key, label, color }]. Each bucket carries a numeric value per
 *      series key and is drawn as a stacked bar in series order, with a legend. Buckets:
 *      { label, [key]: number, tooltip?: [{k, v}] }
 *
 * Both modes share the Y axis (~3 ticks), distributed X labels, and a portal-rendered hover tooltip.
 */
function ActivityChart({ buckets = [], series = null, height = 220, onBucketClick = null, emptyLabel }) {
  const { t, i18n } = useTranslation();
  const [hover, setHover] = useState(null);

  const width = 720;
  const padding = { top: 16, right: 12, bottom: 28, left: 44 };
  const chartW = width - padding.left - padding.right;
  const chartH = height - padding.top - padding.bottom;

  const multi = Array.isArray(series) && series.length > 0;

  // Default (legacy) series: failure at the bottom, success on top — preserves the original look.
  const activeSeries = multi
    ? series
    : [
        { key: 'failure', label: t('requestHistory.failed'), color: 'var(--color-danger)' },
        { key: 'success', label: t('requestHistory.success'), color: 'var(--color-primary)' }
      ];

  const bucketTotal = (b) => {
    if (!multi && b.total != null) return b.total;
    return activeSeries.reduce((sum, s) => sum + (b[s.key] || 0), 0);
  };

  const maxVal = Math.max(1, ...buckets.map(bucketTotal));

  const barGap = 2;
  const barW = buckets.length > 0 ? Math.max(1, chartW / buckets.length - barGap) : 0;

  const yTicks = [0, 0.5, 1].map((f) => ({ f, value: Math.round(maxVal * f) }));
  const labelStride = Math.max(1, Math.ceil(buckets.length / 8));

  const handleMove = useCallback((e, bucket) => {
    setHover({ x: e.clientX, y: e.clientY, bucket });
  }, []);

  const tooltipRows = (b) => {
    if (b.tooltip) return b.tooltip;
    if (multi) return activeSeries.map((s) => ({ k: s.label, v: b[s.key] || 0 }));
    return [{ k: t('common.total') || 'Total', v: bucketTotal(b) }];
  };

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
      {multi && (
        <div className="chart-legend" style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--spacing-md)', marginBottom: 'var(--spacing-sm)' }}>
          {activeSeries.map((s) => (
            <span key={s.key} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem', fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
              <span style={{ width: 10, height: 10, borderRadius: 2, background: s.color, display: 'inline-block' }} />
              {s.label}
            </span>
          ))}
        </div>
      )}
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
              <text x={padding.left - 8} y={y + 4} textAnchor="end" fontSize="6" fill="var(--color-text-muted)">
                {formatNumber(tick.value, i18n.language)}
              </text>
            </g>
          );
        })}

        {/* Bars — stacked segments from the bottom up in series order */}
        {buckets.map((b, i) => {
          const x = padding.left + i * (chartW / buckets.length) + barGap / 2;
          let yCursor = padding.top + chartH;
          const segments = [];
          activeSeries.forEach((s) => {
            const value = b[s.key] || 0;
            if (value <= 0) return;
            const segH = (value / maxVal) * chartH;
            yCursor -= segH;
            segments.push(<rect key={s.key} x={x} y={yCursor} width={barW} height={Math.max(0, segH)} fill={s.color} rx="1" />);
          });
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
              {segments}
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
              fontSize="6"
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
            {tooltipRows(hover.bucket).map((row) => (
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
