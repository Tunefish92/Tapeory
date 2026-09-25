import { describe, it } from "vitest";

/**
 * jsdom does not implement HTMLCanvasElement's 2D rendering context, so Konva's <Stage> cannot
 * mount in this test environment: `getContext("2d")` returns null and Konva throws.
 *
 * This is fixable by adding the `canvas` npm package as a dev dependency (it provides a native
 * canvas backend jsdom can delegate to), but that package needs its install script (which
 * fetches a native binary) approved — this project's npm setup gates install scripts behind
 * explicit approval, and this session didn't have a reason urgent enough to grant it. To enable
 * canvas-level tests later: `npm install --save-dev canvas`, approve its install script, then
 * replace this file with real LabelCanvas/ObjectShape render tests.
 *
 * Until then, canvas rendering is covered by manual verification only; the editor's actual logic
 * (document mutations, undo/redo, field extraction) is unit tested in document.test.ts and
 * history.test.ts, and every non-canvas UI piece (Toolbar, PropertiesPanel) has full RTL coverage.
 */
describe.skip("LabelCanvas (requires the canvas package for jsdom 2D context support)", () => {
  it("renders objects and responds to selection/drag/resize — verify manually in a browser", () => {});
});
