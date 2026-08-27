import { useState, useCallback } from 'react';

/** Persist a piece of state to localStorage (JSON-serialized). */
export function useLocalStorage(key, initialValue) {
  const [value, setValue] = useState(() => {
    try {
      const stored = localStorage.getItem(key);
      return stored !== null ? JSON.parse(stored) : initialValue;
    } catch {
      return initialValue;
    }
  });

  const setStored = useCallback(
    (next) => {
      setValue((prev) => {
        const resolved = typeof next === 'function' ? next(prev) : next;
        try {
          localStorage.setItem(key, JSON.stringify(resolved));
        } catch {
          // ignore quota / serialization errors
        }
        return resolved;
      });
    },
    [key]
  );

  return [value, setStored];
}

export default useLocalStorage;
