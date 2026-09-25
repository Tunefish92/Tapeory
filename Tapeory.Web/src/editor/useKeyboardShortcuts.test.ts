import { renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useKeyboardShortcuts } from "./useKeyboardShortcuts";

function fireKey(key: string, options: Partial<KeyboardEventInit> = {}, target: EventTarget = window) {
  const event = new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true, ...options });
  target.dispatchEvent(event);
  return event;
}

function setupHandlers() {
  return {
    onUndo: vi.fn(),
    onRedo: vi.fn(),
    onDelete: vi.fn(),
    onDuplicate: vi.fn(),
    onDeselect: vi.fn(),
    onNudge: vi.fn(),
  };
}

describe("useKeyboardShortcuts", () => {
  it("calls onUndo for Ctrl+Z", () => {
    const handlers = setupHandlers();
    renderHook(() => useKeyboardShortcuts({ ...handlers, enabled: true }));

    fireKey("z", { ctrlKey: true });

    expect(handlers.onUndo).toHaveBeenCalledOnce();
    expect(handlers.onRedo).not.toHaveBeenCalled();
  });

  it("calls onRedo for Ctrl+Shift+Z", () => {
    const handlers = setupHandlers();
    renderHook(() => useKeyboardShortcuts({ ...handlers, enabled: true }));

    fireKey("z", { ctrlKey: true, shiftKey: true });

    expect(handlers.onRedo).toHaveBeenCalledOnce();
    expect(handlers.onUndo).not.toHaveBeenCalled();
  });

  it("calls onRedo for Ctrl+Y", () => {
    const handlers = setupHandlers();
    renderHook(() => useKeyboardShortcuts({ ...handlers, enabled: true }));

    fireKey("y", { ctrlKey: true });

    expect(handlers.onRedo).toHaveBeenCalledOnce();
  });

  it("calls onDuplicate for Ctrl+D", () => {
    const handlers = setupHandlers();
    renderHook(() => useKeyboardShortcuts({ ...handlers, enabled: true }));

    fireKey("d", { ctrlKey: true });

    expect(handlers.onDuplicate).toHaveBeenCalledOnce();
  });

  it("calls onDelete for Delete and Backspace", () => {
    const handlers = setupHandlers();
    renderHook(() => useKeyboardShortcuts({ ...handlers, enabled: true }));

    fireKey("Delete");
    fireKey("Backspace");

    expect(handlers.onDelete).toHaveBeenCalledTimes(2);
  });

  it("calls onDeselect for Escape", () => {
    const handlers = setupHandlers();
    renderHook(() => useKeyboardShortcuts({ ...handlers, enabled: true }));

    fireKey("Escape");

    expect(handlers.onDeselect).toHaveBeenCalledOnce();
  });

  it("nudges by 1mm on arrow keys, and by 5mm with Shift", () => {
    const handlers = setupHandlers();
    renderHook(() => useKeyboardShortcuts({ ...handlers, enabled: true }));

    fireKey("ArrowUp");
    fireKey("ArrowDown");
    fireKey("ArrowLeft");
    fireKey("ArrowRight", { shiftKey: true });

    expect(handlers.onNudge).toHaveBeenNthCalledWith(1, 0, -1);
    expect(handlers.onNudge).toHaveBeenNthCalledWith(2, 0, 1);
    expect(handlers.onNudge).toHaveBeenNthCalledWith(3, -1, 0);
    expect(handlers.onNudge).toHaveBeenNthCalledWith(4, 5, 0);
  });

  it("ignores keystrokes while typing in an input, textarea, or select", () => {
    const handlers = setupHandlers();
    renderHook(() => useKeyboardShortcuts({ ...handlers, enabled: true }));

    const input = document.createElement("input");
    document.body.appendChild(input);

    fireKey("Delete", {}, input);

    expect(handlers.onDelete).not.toHaveBeenCalled();
    document.body.removeChild(input);
  });

  it("does nothing when disabled", () => {
    const handlers = setupHandlers();
    renderHook(() => useKeyboardShortcuts({ ...handlers, enabled: false }));

    fireKey("Delete");
    fireKey("z", { ctrlKey: true });

    expect(handlers.onDelete).not.toHaveBeenCalled();
    expect(handlers.onUndo).not.toHaveBeenCalled();
  });

  it("removes its listener on unmount", () => {
    const handlers = setupHandlers();
    const { unmount } = renderHook(() => useKeyboardShortcuts({ ...handlers, enabled: true }));

    unmount();
    fireKey("Delete");

    expect(handlers.onDelete).not.toHaveBeenCalled();
  });
});
