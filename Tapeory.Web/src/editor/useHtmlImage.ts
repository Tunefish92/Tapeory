import { useEffect, useState } from "react";

/** Loads a URL into an HTMLImageElement for use with react-konva's <Image>, which needs an
 * already-loaded image element rather than a URL string. */
export function useHtmlImage(url: string | null): HTMLImageElement | null {
  const [image, setImage] = useState<HTMLImageElement | null>(null);

  useEffect(() => {
    if (!url) {
      setImage(null);
      return;
    }

    const img = new window.Image();
    img.crossOrigin = "anonymous";

    const handleLoad = () => setImage(img);
    img.addEventListener("load", handleLoad);
    img.src = url;

    return () => {
      img.removeEventListener("load", handleLoad);
    };
  }, [url]);

  return image;
}
