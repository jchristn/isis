import { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { IconX } from './Icons';

/**
 * Accessible modal. Locks body scroll, closes on ESC and backdrop click, and
 * keeps header + footer outside the scrollable body.
 */
function Modal({ isOpen, onClose, title, subtitle, children, footer, size = 'medium' }) {
  const panelRef = useRef(null);

  useEffect(() => {
    if (!isOpen) return undefined;
    const onKey = (e) => {
      if (e.key === 'Escape') onClose?.();
    };
    document.addEventListener('keydown', onKey);
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    // Move focus into the panel.
    setTimeout(() => panelRef.current?.focus(), 0);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = prevOverflow;
    };
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const sizeClass = size === 'wide' ? 'wide' : size === 'small' ? 'small' : '';

  return createPortal(
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose?.()}>
      <div
        className={`modal-panel ${sizeClass}`}
        role="dialog"
        aria-modal="true"
        aria-label={typeof title === 'string' ? title : undefined}
        tabIndex={-1}
        ref={panelRef}
      >
        <div className="modal-header">
          <div>
            {title && <h2>{title}</h2>}
            {subtitle && <div className="page-subtitle">{subtitle}</div>}
          </div>
          <button className="btn-icon" onClick={onClose} aria-label="Close">
            <IconX />
          </button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-footer">{footer}</div>}
      </div>
    </div>,
    document.body
  );
}

export default Modal;
