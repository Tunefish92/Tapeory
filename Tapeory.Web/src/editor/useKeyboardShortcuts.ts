import { useEffect } from "react";

interface KeyboardShortcutHandlers {
  onUndo: () => void;
  onRedo: () => void;
  onDelete: () => void;
  onDuplicate: () => void;
  onDeselect: () => void;
  onNudge: (dxMm: number, dyMm: number) => void;
  enabled: boolean;
}

const EDITABLE_TAGS = new Set(["INPUT", "TEXTAREA", "SELECT"]);

export function useKeyboardShortcuts({
  onUndo,
  onRedo,
  onDelete,
  onDuplicate,
  onDeselect,
  onNudge,
  enabled,
}: KeyboardShortcutHandlers): void {
  useEffect(() => {
    if (!enabled) {
      return;
    }

    function handleKeyDown(e: KeyboardEvent) {
      const target = e.target as HTMLElement | null;

      if (target && EDITABLE_TAGS.has(target.tagName)) {
        return;
      }

      const meta = e.ctrlKey || e.metaKey;

      if (meta && e.key.toLowerCase() === "z" && e.shiftKey) {
        e.preventDefault();
        onRedo();
        return;
      }

      if (meta && e.key.toLowerCase() === "z") {
        e.preventDefault();
        onUndo();
        return;
      }

      if (meta && e.key.toLowerCase() === "y") {
        e.preventDefault();
        onRedo();
        return;
      }

      if (meta && e.key.toLowerCase() === "d") {
        e.preventDefault();
        onDuplicate();
        return;
      }

      if (e.key === "Delete" || e.key === "Backspace") {
        e.preventDefault();
        onDelete();
        return;
      }

      if (e.key === "Escape") {
        onDeselect();
        return;
      }

      const step = e.shiftKey ? 5 : 1;

      switch (e.key) {
        case "ArrowUp":
          e.preventDefault();
          onNudge(0, -step);
          break;
        case "ArrowDown":
          e.preventDefault();
          onNudge(0, step);
          break;
        case "ArrowLeft":
          e.preventDefault();
          onNudge(-step, 0);
          break;
        case "ArrowRight":
          e.preventDefault();
          onNudge(step, 0);
          break;
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [enabled, onUndo, onRedo, onDelete, onDuplicate, onDeselect, onNudge]);
}
