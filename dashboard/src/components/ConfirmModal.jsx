import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';

/** Custom confirmation dialog (never uses window.confirm). */
function ConfirmModal({ isOpen, onClose, onConfirm, title, message, confirmLabel, danger = true }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);

  const handleConfirm = async () => {
    setBusy(true);
    try {
      await onConfirm();
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={busy ? undefined : onClose}
      title={title || t('confirm.deleteTitle')}
      size="small"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button className={danger ? 'btn-danger' : 'btn-primary'} onClick={handleConfirm} disabled={busy}>
            {busy ? t('confirm.deleting') : confirmLabel || t('common.delete')}
          </button>
        </>
      }
    >
      <p style={{ fontSize: 'var(--font-size-sm)' }}>{message}</p>
    </Modal>
  );
}

export default ConfirmModal;
