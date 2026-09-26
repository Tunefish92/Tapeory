import { useEffect, useMemo, useState } from "react";
import { ensureFontLoaded } from "../api/fonts";
import type { LabelDocument } from "./types";

/**
 * Loads every font the document's text uses from the server and returns a counter that
 * increases whenever one finishes loading — so text can be re-measured with the real font.
 */
export function useDocumentFonts(document: LabelDocument): number {
  const [version, setVersion] = useState(0);

  const keys = useMemo(() => {
    const set = new Set<string>();
    for (const object of document.objects) {
      if ((object.type === "text" || object.type === "dynamicField") && object.fontFamily) {
        set.add(`${object.fontWeight === "bold" ? "bold" : "normal"}|${object.fontFamily}`);
      }
    }
    return [...set].sort().join("\n");
  }, [document.objects]);

  useEffect(() => {
    let cancelled = false;

    for (const key of keys ? keys.split("\n") : []) {
      const [weight, family] = key.split(/\|(.*)/s);
      void ensureFontLoaded(family, weight === "bold").then((ok) => {
        if (ok && !cancelled) setVersion((v) => v + 1);
      });
    }

    return () => {
      cancelled = true;
    };
  }, [keys]);

  return version;
}
