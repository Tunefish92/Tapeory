import { useEffect, useRef } from "react";
import type Konva from "konva";
import type { KonvaEventObject } from "konva/lib/Node";
import { Layer, Rect, Stage, Transformer } from "react-konva";
import { PIXELS_PER_MM } from "./constants";
import { ObjectShape } from "./ObjectShape";
import { useDocumentFonts } from "./useDocumentFonts";
import type { LabelDocument, LabelObjectPatch } from "./types";

interface LabelCanvasProps {
  document: LabelDocument;
  selectedId: string | null;
  previewMode: boolean;
  zoom: number;
  onSelect: (id: string | null) => void;
  onChange: (id: string, changes: LabelObjectPatch) => void;
}

export function LabelCanvas({
  document: labelDocument,
  selectedId,
  previewMode,
  zoom,
  onSelect,
  onChange,
}: LabelCanvasProps) {
  const shapeRefs = useRef(new Map<string, Konva.Node>());
  const transformerRef = useRef<Konva.Transformer>(null);
  const fontsVersion = useDocumentFonts(labelDocument);

  useEffect(() => {
    const transformer = transformerRef.current;

    if (!transformer) {
      return;
    }

    const node = selectedId ? shapeRefs.current.get(selectedId) : undefined;
    transformer.nodes(node ? [node] : []);
    transformer.getLayer()?.batchDraw();
  }, [selectedId, labelDocument.objects, fontsVersion]);

  const scale = PIXELS_PER_MM * zoom;
  const selectedObject = labelDocument.objects.find((object) => object.id === selectedId);
  const showTransformer = !previewMode && !!selectedObject && !selectedObject.locked;

  const handleStagePointerDown = (e: KonvaEventObject<MouseEvent | TouchEvent>) => {
    if (e.target === e.target.getStage()) {
      onSelect(null);
    }
  };

  return (
    <Stage
      width={labelDocument.widthMm * scale}
      height={labelDocument.heightMm * scale}
      onMouseDown={handleStagePointerDown}
      onTouchStart={handleStagePointerDown}
    >
      <Layer scaleX={scale} scaleY={scale}>
        <Rect
          x={0}
          y={0}
          width={labelDocument.widthMm}
          height={labelDocument.heightMm}
          fill="#ffffff"
          stroke="#d0d0d0"
          strokeWidth={0.25}
          listening={false}
        />
        {labelDocument.objects.map((object) => (
          <ObjectShape
            key={object.id}
            object={object}
            previewMode={previewMode}
            fontsVersion={fontsVersion}
            onSelect={() => onSelect(object.id)}
            onChange={(changes) => onChange(object.id, changes)}
            registerNode={(node) => {
              if (node) {
                shapeRefs.current.set(object.id, node);
              } else {
                shapeRefs.current.delete(object.id);
              }
            }}
          />
        ))}
        {showTransformer && (
          <Transformer
            ref={transformerRef}
            rotateEnabled
            flipEnabled={false}
            boundBoxFunc={(oldBox, newBox) =>
              newBox.width < 2 || newBox.height < 2 ? oldBox : newBox
            }
          />
        )}
      </Layer>
    </Stage>
  );
}
