import { useEffect, useState } from "react";

export interface Viewport {
  /** Phone-sized layouts (< 640px), matching the existing PosterGrid/App boundary. */
  isMobile: boolean;
  /** Tablet-sized layouts (640px–1023px). */
  isTablet: boolean;
  /** Phone or tablet — used for touch-sized controls and full-screen modals. */
  isCompact: boolean;
}

function readViewport(): Viewport {
  const width = typeof window === "undefined" ? 1024 : window.innerWidth;
  return {
    isMobile: width < 640,
    isTablet: width >= 640 && width < 1024,
    isCompact: width < 1024,
  };
}

/** One shared resize listener instead of four independent isMobile listeners. */
export function useViewport(): Viewport {
  const [viewport, setViewport] = useState<Viewport>(readViewport);

  useEffect(() => {
    const update = () =>
      setViewport((current) => {
        const next = readViewport();
        return current.isMobile === next.isMobile && current.isTablet === next.isTablet
          ? current
          : next;
      });
    update();
    window.addEventListener("resize", update);
    return () => window.removeEventListener("resize", update);
  }, []);

  return viewport;
}
