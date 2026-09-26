import { useMemo } from "react";
import type Konva from "konva";
import type { KonvaEventObject } from "konva/lib/Node";
import { Image as KonvaImage, Line as KonvaLine, Rect as KonvaRect, Text as KonvaText } from "react-konva";
import { PT_TO_MM } from "./constants";
import { createCanvasMeasure, fitText, LINE_HEIGHT } from "./fitText";
import type { DynamicFieldObject, LabelObject, LabelObjectPatch, TextObject } from "./types";
import { useHtmlImage } from "./useHtmlImage";

interface ObjectShapeProps {
  object: LabelObject;
  /** In preview mode, dynamic fields render their sample/default value instead of the
   * {{fieldName}} placeholder. */
  previewMode: boolean;
  onSelect: () => void;
  onChange: (changes: LabelObjectPatch) => void;
  registerNode: (node: Konva.Node | null) => void;
  /** Bumped when a font finishes loading, so text is re-measured with the real font. */
  fontsVersion?: number;
}

function handleDragEnd(e: KonvaEventObject<DragEvent>, onChange: (changes: LabelObjectPatch) => void) {
  onChange({ x: e.target.x(), y: e.target.y() });
}

/** Shared transform-end handler for box-shaped objects (rect, image): Konva's Transformer
 * resizes by scaling the node, so we bake that scale into width/height and reset scale to 1 to
 * avoid it compounding on the next resize. */
function handleBoxTransformEnd(
  e: KonvaEventObject<Event>,
  onChange: (changes: LabelObjectPatch) => void,
) {
  const node = e.target;
  const scaleX = node.scaleX();
  const scaleY = node.scaleY();

  node.scaleX(1);
  node.scaleY(1);

  onChange({
    x: node.x(),
    y: node.y(),
    rotation: node.rotation(),
    width: Math.max(2, node.width() * scaleX),
    height: Math.max(2, node.height() * scaleY),
  });
}

/**
 * Text is laid out in millimetres, so its font size is only a few units, and the browser would
 * measure and position glyphs for that tiny size — the layer's zoom then magnifies the rounding
 * into uneven letter spacing. Text nodes are therefore drawn this many times larger and scaled
 * back down by the same factor.
 */
const TEXT_SCALE = 16;

/** Like handleBoxTransformEnd, for text nodes at their base scale of 1 / TEXT_SCALE. */
function handleTextTransformEnd(
  e: KonvaEventObject<Event>,
  onChange: (changes: LabelObjectPatch) => void,
) {
  const node = e.target;
  const scaleX = node.scaleX();
  const scaleY = node.scaleY();

  node.scaleX(1 / TEXT_SCALE);
  node.scaleY(1 / TEXT_SCALE);

  onChange({
    x: node.x(),
    y: node.y(),
    rotation: node.rotation(),
    width: Math.max(2, node.width() * scaleX),
    height: Math.max(2, node.height() * scaleY),
  });
}

function handleLineTransformEnd(
  e: KonvaEventObject<Event>,
  points: [number, number, number, number],
  onChange: (changes: LabelObjectPatch) => void,
) {
  const node = e.target;
  const scaleX = node.scaleX();
  const scaleY = node.scaleY();

  node.scaleX(1);
  node.scaleY(1);

  onChange({
    x: node.x(),
    y: node.y(),
    rotation: node.rotation(),
    points: [points[0] * scaleX, points[1] * scaleY, points[2] * scaleX, points[3] * scaleY],
  });
}

type CommonShapeProps = {
  x: number;
  y: number;
  rotation: number;
  draggable: boolean;
  onClick: () => void;
  onTap: () => void;
  ref: (node: Konva.Node | null) => void;
  onDragEnd: (e: KonvaEventObject<DragEvent>) => void;
};

/** Text and dynamic fields: laid out with the same fitting rules the print renderer uses. */
function TextShape({
  object,
  text,
  common,
  onChange,
  fontsVersion,
}: {
  object: TextObject | DynamicFieldObject;
  text: string;
  common: CommonShapeProps;
  onChange: (changes: LabelObjectPatch) => void;
  fontsVersion: number;
}) {
  const bold = object.fontWeight === "bold";
  const fit = object.fit ?? "none";

  const fitted = useMemo(
    () =>
      fitText(text, object.width, object.height, object.fontSize * PT_TO_MM, fit, createCanvasMeasure(object.fontFamily, bold)),
    // fontsVersion: re-measure once the real font has loaded.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [text, object.width, object.height, object.fontSize, object.fontFamily, bold, fit, fontsVersion],
  );

  return (
    <KonvaText
      // Remount when fonts load: Konva caches its own glyph measurements per node.
      key={fontsVersion}
      {...common}
      text={fitted.lines.join("\n")}
      width={object.width * TEXT_SCALE}
      height={object.height * TEXT_SCALE}
      scaleX={1 / TEXT_SCALE}
      scaleY={1 / TEXT_SCALE}
      fontSize={fitted.fontSize * TEXT_SCALE}
      lineHeight={LINE_HEIGHT}
      // Line breaks come from fitText (or the typed text), exactly as the renderer does it.
      wrap="none"
      fontFamily={object.fontFamily}
      fontStyle={bold ? "bold" : "normal"}
      align={object.align}
      fill={object.fill}
      onTransformEnd={(e) => handleTextTransformEnd(e, onChange)}
    />
  );
}

export function ObjectShape({
  object,
  previewMode,
  onSelect,
  onChange,
  registerNode,
  fontsVersion = 0,
}: ObjectShapeProps) {
  const imageUrl = object.type === "image" ? object.url : null;
  const image = useHtmlImage(imageUrl);

  if (object.hidden) {
    return null;
  }

  const common: CommonShapeProps = {
    x: object.x,
    y: object.y,
    rotation: object.rotation,
    // Locking prevents dragging (and, via LabelCanvas's showTransformer check, resizing) but
    // must not block pointer events entirely — otherwise a locked object could never be
    // clicked again to select and unlock it.
    draggable: !object.locked,
    onClick: onSelect,
    onTap: onSelect,
    ref: registerNode,
    onDragEnd: (e: KonvaEventObject<DragEvent>) => handleDragEnd(e, onChange),
  };

  switch (object.type) {
    case "text":
      return <TextShape object={object} text={object.text} common={common} onChange={onChange} fontsVersion={fontsVersion} />;

    case "dynamicField": {
      const displayText = previewMode
        ? object.defaultValue || `[${object.fieldName}]`
        : `{{${object.fieldName}}}`;

      return <TextShape object={object} text={displayText} common={common} onChange={onChange} fontsVersion={fontsVersion} />;
    }

    case "rect":
      return (
        <KonvaRect
          {...common}
          width={object.width}
          height={object.height}
          fill={object.fill}
          stroke={object.stroke}
          strokeWidth={object.strokeWidth}
          cornerRadius={object.cornerRadius}
          onTransformEnd={(e) => handleBoxTransformEnd(e, onChange)}
        />
      );

    case "line":
      return (
        <KonvaLine
          {...common}
          points={object.points}
          stroke={object.stroke}
          strokeWidth={object.strokeWidth}
          onTransformEnd={(e) => handleLineTransformEnd(e, object.points, onChange)}
        />
      );

    case "image":
      if (!image) {
        return null;
      }

      return (
        <KonvaImage
          {...common}
          image={image}
          width={object.width}
          height={object.height}
          onTransformEnd={(e) => handleBoxTransformEnd(e, onChange)}
        />
      );

    default:
      return null;
  }
}
