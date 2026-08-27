import { useApp } from '../context/AppContext';
import { IconX } from './Icons';

/** Global toast stack rendered once near the app root. */
function ToastStack() {
  const { toasts, removeToast } = useApp();
  if (!toasts.length) return null;
  return (
    <div className="toast-stack" role="status" aria-live="polite">
      {toasts.map((toast) => (
        <div key={toast.id} className={`toast ${toast.type}`}>
          <span>{toast.message}</span>
          <button className="btn-icon" onClick={() => removeToast(toast.id)} aria-label="Dismiss">
            <IconX width="14" height="14" />
          </button>
        </div>
      ))}
    </div>
  );
}

export default ToastStack;
