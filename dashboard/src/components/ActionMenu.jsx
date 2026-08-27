import { useState, useRef, useLayoutEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { IconDots } from './Icons';

/**
 * Per-row action menu. Portals to the document body so it is never clipped by
 * table scroll/overflow containers.
 */
function ActionMenu({ actions = [] }) {
  const [open, setOpen] = useState(false);
  const [coords, setCoords] = useState({ top: 0, left: 0 });
  const triggerRef = useRef(null);
  const menuRef = useRef(null);

  const position = useCallback(() => {
    const rect = triggerRef.current?.getBoundingClientRect();
    if (!rect) return;
    const menuWidth = 170;
    let left = rect.right - menuWidth;
    if (left < 8) left = 8;
    setCoords({ top: rect.bottom + 4, left });
  }, []);

  useLayoutEffect(() => {
    if (!open) return undefined;
    position();
    const onScroll = () => setOpen(false);
    const onClick = (e) => {
      if (menuRef.current && !menuRef.current.contains(e.target) && !triggerRef.current.contains(e.target)) {
        setOpen(false);
      }
    };
    window.addEventListener('scroll', onScroll, true);
    window.addEventListener('resize', onScroll);
    document.addEventListener('mousedown', onClick);
    return () => {
      window.removeEventListener('scroll', onScroll, true);
      window.removeEventListener('resize', onScroll);
      document.removeEventListener('mousedown', onClick);
    };
  }, [open, position]);

  const visible = actions.filter(Boolean);
  if (!visible.length) return null;

  return (
    <span className="action-menu-wrap" data-row-click-ignore="true">
      <button
        ref={triggerRef}
        className="btn-icon action-menu-trigger"
        aria-label="Row actions"
        onClick={(e) => {
          e.stopPropagation();
          setOpen((v) => !v);
        }}
      >
        <IconDots />
      </button>
      {open &&
        createPortal(
          <div
            ref={menuRef}
            className="action-menu-portal"
            style={{ top: coords.top, left: coords.left }}
            onClick={(e) => e.stopPropagation()}
          >
            {visible.map((action, i) =>
              action.divider ? (
                <div key={`div-${i}`} className="action-menu-divider" />
              ) : (
                <button
                  key={action.label}
                  className={`action-menu-item${action.danger ? ' danger' : ''}`}
                  disabled={action.disabled}
                  onClick={() => {
                    setOpen(false);
                    action.onClick?.();
                  }}
                >
                  {action.label}
                </button>
              )
            )}
          </div>,
          document.body
        )}
    </span>
  );
}

export default ActionMenu;
