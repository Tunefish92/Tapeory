/** Base rendering scale: how many screen pixels represent one label millimeter at 100% zoom. */
export const PIXELS_PER_MM = 4;

/** Font sizes are authored in points (the unit users expect) and converted to the document's mm
 * coordinate space so they scale consistently with everything else on the canvas. */
export const PT_TO_MM = 0.352778;

export const MIN_ZOOM = 0.5;
export const MAX_ZOOM = 4;
export const DEFAULT_ZOOM = 2;
export const ZOOM_STEP = 0.25;
