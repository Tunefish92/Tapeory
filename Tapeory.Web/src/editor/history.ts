import { useCallback, useMemo, useReducer } from "react";

export interface HistoryState<T> {
  past: T[];
  present: T;
  future: T[];
}

export type HistoryAction<T> =
  | { type: "set"; value: T }
  | { type: "reset"; value: T }
  | { type: "undo" }
  | { type: "redo" };

const MAX_HISTORY = 100;

export function historyReducer<T>(state: HistoryState<T>, action: HistoryAction<T>): HistoryState<T> {
  switch (action.type) {
    case "set": {
      if (action.value === state.present) {
        return state;
      }

      const past = [...state.past, state.present].slice(-MAX_HISTORY);
      return { past, present: action.value, future: [] };
    }
    case "reset":
      return { past: [], present: action.value, future: [] };
    case "undo": {
      if (state.past.length === 0) {
        return state;
      }

      const previous = state.past[state.past.length - 1];
      return {
        past: state.past.slice(0, -1),
        present: previous,
        future: [state.present, ...state.future],
      };
    }
    case "redo": {
      if (state.future.length === 0) {
        return state;
      }

      const [next, ...rest] = state.future;
      return {
        past: [...state.past, state.present],
        present: next,
        future: rest,
      };
    }
    default:
      return state;
  }
}

export interface UseHistoryResult<T> {
  value: T;
  set: (value: T) => void;
  reset: (value: T) => void;
  undo: () => void;
  redo: () => void;
  canUndo: boolean;
  canRedo: boolean;
}

export function useHistory<T>(initial: T): UseHistoryResult<T> {
  const [state, dispatch] = useReducer(historyReducer<T>, {
    past: [],
    present: initial,
    future: [],
  });

  const set = useCallback((value: T) => dispatch({ type: "set", value }), []);
  const reset = useCallback((value: T) => dispatch({ type: "reset", value }), []);
  const undo = useCallback(() => dispatch({ type: "undo" }), []);
  const redo = useCallback(() => dispatch({ type: "redo" }), []);

  return useMemo(
    () => ({
      value: state.present,
      set,
      reset,
      undo,
      redo,
      canUndo: state.past.length > 0,
      canRedo: state.future.length > 0,
    }),
    [state, set, reset, undo, redo],
  );
}
