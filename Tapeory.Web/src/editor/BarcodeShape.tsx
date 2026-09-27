import { useEffect, useState } from "react";
import type Konva from "konva";
import type { KonvaEventObject } from "konva/lib/Node";
import { Group, Line as KonvaLine, Rect as KonvaRect, Shape, Text as KonvaText } from "react-konva";
import {
  BARCODE_TEXT_SIZE_RATIO,
  barcodeTextHeight,
  encodeBarcode,
  type BarcodeEncoding,
} from "../api/barcodes";
import type { BarcodeObject, BarcodeSymbology, LabelObjectPatch } from "./types";

/** Barcode text is tiny in millimetres; like TextShape, draw it larger and scale it back down so
 * the browser doesn't round its glyph positions. */
const TEXT_SCALE = 16;

/** The server's encoding of a barcode value, or its error; null while it's loading. */
export function useBarcodeEncoding(symbology: BarcodeSymbology, data: string): BarcodeEncoding | null {
  const [result, setResult] = useState<{ key: string; encoding: BarcodeEncoding } | null>(null);
  const key = `${symbology}\u0000${data}`;

  useEffect(() => {
    let cancelled = false;

    encodeBarcode(symbology, data).then((encoding) => {
      if (!cancelled) setResult({ key, encoding });
    });

    return () => {
      cancelled = true;
    };
  }, [symbology, data, key]);

  return result?.key === key ? result.encoding : null;
}

interface BarcodeShapeProps {
  object: BarcodeObject;
  draggable: boolean;
  onSelect: () => void;
  onChange: (changes: LabelObjectPatch) => void;
  registerNode: (node: Konva.Node | null) => void;
}

/** A barcode drawn from the server's module grid, laid out like the print renderer: 1D bars
 * span the height above the optional text line, 2D modules stay square and are centred. */
export function BarcodeShape({ object, draggable, onSelect, onChange, registerNode }: BarcodeShapeProps) {
  const encoding = useBarcodeEncoding(object.symbology, object.data);
  const { width, height } = object;
  const barcode = encoding?.barcode ?? null;

  const textHeight = barcode && !barcode.twoDimensional && object.showText ? barcodeTextHeight(height) : 0;
  const barsHeight = height - textHeight;
  const module = barcode
    ? Math.min(width / barcode.columns, barcode.twoDimensional ? barsHeight / barcode.rows : Infinity)
    : 0;
  const moduleHeight = barcode?.twoDimensional ? module : barsHeight;
  const left = barcode ? (width - module * barcode.columns) / 2 : 0;
  const top = barcode?.twoDimensional ? (barsHeight - module * barcode.rows) / 2 : 0;

  function handleTransformEnd(e: KonvaEventObject<Event>) {
    const node = e.target;
    const scaleX = node.scaleX();
    const scaleY = node.scaleY();

    node.scaleX(1);
    node.scaleY(1);

    onChange({
      x: node.x(),
      y: node.y(),
      rotation: node.rotation(),
      width: Math.max(2, width * scaleX),
      height: Math.max(2, height * scaleY),
    });
  }

  return (
    <Group
      x={object.x}
      y={object.y}
      rotation={object.rotation}
      draggable={draggable}
      onClick={onSelect}
      onTap={onSelect}
      ref={registerNode}
      onDragEnd={(e) => onChange({ x: e.target.x(), y: e.target.y() })}
      onTransformEnd={handleTransformEnd}
    >
      {/* The whole box is clickable and gives the transformer its size. */}
      <KonvaRect width={width} height={height} fill="transparent" />

      {barcode && (
        <Shape
          fill={object.fill}
          listening={false}
          sceneFunc={(context, shape) => {
            context.beginPath();

            for (let row = 0; row < barcode.rows; row++) {
              let column = 0;

              while (column < barcode.columns) {
                if (barcode.modules[row * barcode.columns + column] !== "1") {
                  column++;
                  continue;
                }

                const start = column;
                while (column < barcode.columns && barcode.modules[row * barcode.columns + column] === "1") {
                  column++;
                }

                context.rect(left + start * module, top + row * moduleHeight, (column - start) * module, moduleHeight);
              }
            }

            context.fillShape(shape);
          }}
        />
      )}

      {barcode && textHeight > 0 && (
        <KonvaText
          x={0}
          y={barsHeight + textHeight * 0.1}
          width={width * TEXT_SCALE}
          scaleX={1 / TEXT_SCALE}
          scaleY={1 / TEXT_SCALE}
          text={barcode.text}
          fontSize={textHeight * BARCODE_TEXT_SIZE_RATIO * TEXT_SCALE}
          fontFamily="Arial"
          align="center"
          fill={object.fill}
          listening={false}
        />
      )}

      {encoding?.error && (
        <>
          <KonvaRect width={width} height={height} stroke="#999999" strokeWidth={Math.min(width, height) * 0.03} listening={false} />
          <KonvaLine points={[0, 0, width, height]} stroke="#999999" strokeWidth={Math.min(width, height) * 0.03} listening={false} />
          <KonvaLine points={[width, 0, 0, height]} stroke="#999999" strokeWidth={Math.min(width, height) * 0.03} listening={false} />
        </>
      )}
    </Group>
  );
}
