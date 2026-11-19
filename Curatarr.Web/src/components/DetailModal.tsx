import { useEffect, useState } from "react";
import { api, SourceItem, TitleDetails } from "../api/client";
import type { ShowGroup } from "../lib/showGroups";
import { useViewport } from "../lib/useViewport";

export function DetailModal({
  item,
  summary,
  onClose,
}: {
  item: SourceItem;
  summary?: ShowGroup;
  onClose: () => void;
}) {
  const { isCompact } = useViewport();
  const [details, setDetails] = useState<TitleDetails | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setLoading(true);
    api
      .getItemDetails(item.id)
      .then(setDetails)
      .catch(() => setDetails({ available: false }))
      .finally(() => setLoading(false));
  }, [item.id]);

  const label = summary
    ? summary.title
    : item.mediaType === "Movie"
      ? `${item.title ?? "Untitled"}${item.year ? ` (${item.year})` : ""}`
      : item.showTitle ?? "Untitled";

  const episodeLabel = summary
    ? `${summary.episodeCount} ${summary.episodeCount === 1 ? "episode" : "episodes"}` +
      (summary.seasonCount > 0
        ? ` · ${summary.seasonCount} ${summary.seasonCount === 1 ? "season" : "seasons"}`
        : "") +
      (summary.latestSeason != null && summary.latestEpisode != null
        ? ` · Latest S${String(summary.latestSeason).padStart(2, "0")}E${String(
            summary.latestEpisode
          ).padStart(2, "0")}`
        : "")
    : null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={label}
      onClick={onClose}
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.5)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 100,
        padding: isCompact ? 0 : "1.5rem",
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          background: "var(--surface-card)",
          borderRadius: isCompact ? 0 : "var(--radius-card)",
          border: "2px solid var(--border-strong)",
          maxWidth: isCompact ? "none" : "480px",
          width: "100%",
          height: isCompact ? "100%" : undefined,
          maxHeight: isCompact ? "none" : "85vh",
          overflowY: "auto",
          display: "flex",
          fontFamily: "var(--font-body)",
        }}
      >
        {(details?.posterUrl ?? item.posterUrl) && (
          <img
            src={details?.posterUrl ?? item.posterUrl ?? undefined}
            alt={label}
            style={{ width: "140px", height: "210px", objectFit: "cover", flexShrink: 0 }}
          />
        )}
        <div style={{ padding: "1rem", flex: 1, minWidth: 0 }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
            <h2
              style={{
                fontFamily: "var(--font-heading)",
                fontSize: "18px",
                fontWeight: 600,
                margin: 0,
                color: "var(--text-primary)",
              }}
            >
              {label}
            </h2>
            <button
              className="modal-button"
              onClick={onClose}
              aria-label="Close"
              style={{ border: "none", background: "none", cursor: "pointer", color: "var(--text-secondary)", fontSize: "16px" }}
            >
              ✕
            </button>
          </div>

          {episodeLabel && (
            <p
              style={{
                fontSize: "12px",
                color: "var(--text-secondary)",
                margin: "6px 0 0",
              }}
            >
              {episodeLabel}
            </p>
          )}

          {loading && <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>Loading details...</p>}

          {!loading && details?.available === false && (
            <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>
              {summary?.overview ?? item.overview ?? "No additional details available for this title."}
            </p>
          )}

          {!loading && details?.available && (
            <>
              {details.overview && (
                <p style={{ fontSize: "13px", color: "var(--text-primary)", lineHeight: 1.5, margin: "8px 0" }}>
                  {details.overview}
                </p>
              )}
              {details.director && (
                <p style={{ fontSize: "12px", color: "var(--text-secondary)", margin: "4px 0" }}>
                  <strong style={{ color: "var(--text-primary)" }}>Director:</strong> {details.director}
                </p>
              )}
              {details.creators && details.creators.length > 0 && (
                <p style={{ fontSize: "12px", color: "var(--text-secondary)", margin: "4px 0" }}>
                  <strong style={{ color: "var(--text-primary)" }}>Created by:</strong> {details.creators.join(", ")}
                </p>
              )}
              {details.cast && details.cast.length > 0 && (
                <p style={{ fontSize: "12px", color: "var(--text-secondary)", margin: "4px 0" }}>
                  <strong style={{ color: "var(--text-primary)" }}>Cast:</strong> {details.cast.join(", ")}
                </p>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
