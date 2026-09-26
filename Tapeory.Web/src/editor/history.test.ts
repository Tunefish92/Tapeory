import { describe, expect, it } from "vitest";
import { historyReducer, type HistoryState } from "./history";

function initial<T>(present: T): HistoryState<T> {
  return { past: [], present, future: [] };
}

describe("historyReducer", () => {
  it("set pushes the current present onto past and clears future", () => {
    const state = { past: ["a"], present: "b", future: ["stale-redo"] };

    const result = historyReducer(state, { type: "set", value: "c" });

    expect(result).toEqual({ past: ["a", "b"], present: "c", future: [] });
  });

  it("set is a no-op when the value is reference-equal to the current present", () => {
    const value = { x: 1 };
    const state = initial(value);

    const result = historyReducer(state, { type: "set", value });

    expect(result).toBe(state);
  });

  it("undo moves the last past entry into present and pushes present onto future", () => {
    const state = { past: ["a", "b"], present: "c", future: [] };

    const result = historyReducer(state, { type: "undo" });

    expect(result).toEqual({ past: ["a"], present: "b", future: ["c"] });
  });

  it("undo is a no-op when there is no past", () => {
    const state = initial("only");

    const result = historyReducer(state, { type: "undo" });

    expect(result).toBe(state);
  });

  it("redo moves the first future entry into present and pushes present onto past", () => {
    const state = { past: ["a"], present: "b", future: ["c", "d"] };

    const result = historyReducer(state, { type: "redo" });

    expect(result).toEqual({ past: ["a", "b"], present: "c", future: ["d"] });
  });

  it("redo is a no-op when there is no future", () => {
    const state = initial("only");

    const result = historyReducer(state, { type: "redo" });

    expect(result).toBe(state);
  });

  it("reset replaces present and clears both past and future", () => {
    const state = { past: ["a"], present: "b", future: ["c"] };

    const result = historyReducer(state, { type: "reset", value: "fresh" });

    expect(result).toEqual({ past: [], present: "fresh", future: [] });
  });

  it("undo followed by redo returns to the same present", () => {
    let state = initial("start");
    state = historyReducer(state, { type: "set", value: "middle" });
    state = historyReducer(state, { type: "set", value: "end" });

    const afterUndo = historyReducer(state, { type: "undo" });
    const afterRedo = historyReducer(afterUndo, { type: "redo" });

    expect(afterRedo).toEqual(state);
  });

  it("a new set after undo discards the stale redo branch", () => {
    let state = initial("start");
    state = historyReducer(state, { type: "set", value: "middle" });
    state = historyReducer(state, { type: "set", value: "end" });
    state = historyReducer(state, { type: "undo" }); // present = "middle", future = ["end"]

    const result = historyReducer(state, { type: "set", value: "branch" });

    expect(result).toEqual({ past: ["start", "middle"], present: "branch", future: [] });
  });

  it("caps history length so it does not grow unbounded", () => {
    let state = initial(0);

    for (let i = 1; i <= 150; i += 1) {
      state = historyReducer(state, { type: "set", value: i });
    }

    expect(state.past.length).toBeLessThanOrEqual(100);
    expect(state.present).toBe(150);
  });
});
