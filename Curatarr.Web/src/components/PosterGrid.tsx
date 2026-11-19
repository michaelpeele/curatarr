import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from "react";
import * as signalR from "@microsoft/signalr";
import {
  api,
  SourceItem,
  type LibraryEntriesParams,
  type LibraryEntry,
  type LibraryFacets,
  type TitleSearchResult,
} from "../api/client";
import { DetailModal } from "./DetailModal";
import { useToast } from "../context/ToastContext";
import {
  buildShowGroups,
  representativeId,
  type ShowGroup,
} from "../lib/showGroups";
import { useViewport } from "../lib/useViewport";

// TMDB serves any width from the same path. Grid cells are ~150-200px wide, so
// pulling w500 (~80-150 KB) for every card is wasted bandwidth and memory.
function posterThumb(url: string | null, size: "w185" | "w342" = "w185"): string | undefined {
  if (!url) return undefined;
  return url.replace("/t/p/w500/", `/t/p/${size}/`);
}

/**
 * Mounts its children only while they are near the viewport, and unmounts them
 * again once they scroll far away. Sonarr/Radarr use react-lazyload for the
 * same job; doing it by hand avoids another dependency. Without this the grid
 * put tens of thousands of cards (and their posters) in the DOM at once, which
 * made Safari reload the page with a memory-usage warning.
 */
function LazyMount({ children, reserveActions = true }: { children: ReactNode; reserveActions?: boolean }) {
  const ref = useRef<HTMLDivElement | null>(null);
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (typeof IntersectionObserver === "undefined") {
      setMounted(true);
      return;
    }
    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) setMounted(entry.isIntersecting);
      },
      { rootMargin: "1200px 0px" }
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  return (
    // Definite grid-item width keeps the skeleton and the mounted card the
    // same size, so a loaded poster can never push the track wider than its
    // column after a fast multi-page scroll on mobile.
    <div ref={ref} style={{ minWidth: 0, width: "100%", overflow: "hidden" }}>
      {mounted ? (
        children
      ) : (
        // Skeleton mirrors the card's shape -- including the fixed-height
        // action row -- so the grid keeps its height and a long scroll doesn't
        // jump when real cards mount.
        <div
          aria-hidden="true"
          style={{
            display: "flex",
            flexDirection: "column",
            gap: "8px",
            height: "100%",
            border: "2px solid var(--border-strong)",
            borderRadius: "var(--radius-card)",
            overflow: "hidden",
            background: "var(--surface-card)",
          }}
        >
          <div
            style={{
              aspectRatio: "2 / 3",
              background: "var(--surface-raised)",
              opacity: 0.4,
            }}
          />
          <div
            style={{
              padding: "8px 10px",
              display: "flex",
              flexDirection: "column",
              gap: "4px",
              flex: 1,
            }}
          >
            <div
              style={{
                height: "34px",
                borderRadius: "var(--radius)",
                background: "var(--surface-raised)",
                opacity: 0.25,
              }}
            />
            {reserveActions && <div className="card-action-row" style={CARD_ACTION_ROW_STYLE} />}
          </div>
        </div>
      )}
    </div>
  );
}

const PENDING_YELLOW = "#d4a017";

// Client-side mirror of the engine's TitleQueryParser, used to prefill the
// Match modal from a show title / dotted release filename. Kept deliberately
// small and dependency-free; the server owns the canonical implementation.
const RELEASE_TOKENS = new Set([
  "2160p", "1080p", "720p", "480p", "4k", "uhd",
  "bluray", "blu-ray", "bdrip", "brrip", "webrip", "web-dl", "hdtv",
  "x265", "x264", "h265", "h264", "hevc", "avc",
  "aac", "ac3", "dts", "dolby", "truehd",
  "remux", "extended", "unrated", "imax", "hdr", "dv", "hdr10",
]);

function normalizeTitleQuery(input: string): { title: string; year: number | null } {
  const text = (input ?? "").trim();
  if (!text) return { title: "", year: null };

  const canonical = /^(.+?)\s*\((\d{4})\)\s*$/.exec(text);
  if (canonical) return { title: canonical[1].trim(), year: Number(canonical[2]) };

  // Relative paths may be "Folder/Folder.mkv". Take the filename and drop its
  // extension before normalising separators so release tags are not split
  // across a slash or hidden inside ".mkv".
  let cleaned = text.replace(/\\/g, "/").split("/").filter(Boolean).pop() ?? text;
  cleaned = cleaned.replace(/\.[A-Za-z0-9]{2,4}$/, "");
  cleaned = cleaned.replace(/[._+]/g, " ").replace(/\s+/g, " ").trim();
  let year: number | null = null;
  const yearMatch = /\b(?:18[8-9]\d|19\d{2}|20[0-9]{2})\b/.exec(cleaned);
  if (yearMatch) {
    year = Number(yearMatch[0]);
    cleaned = (cleaned.slice(0, yearMatch.index) + " " + cleaned.slice(yearMatch.index + yearMatch[0].length))
      .replace(/\s+/g, " ").trim();
  }
  const withoutGroup = cleaned.replace(/-\s*[A-Z0-9]{3,}\s*$/, "").trim();
  if (withoutGroup) cleaned = withoutGroup;

  const tokens = cleaned.split(/\s+/)
    .map((t) => t.replace(/^-+|-+$/g, ""))
    .filter((t) => t.length > 0 && !RELEASE_TOKENS.has(t.toLowerCase()));
  const title = tokens.join(" ").trim();
  return { title: title || cleaned, year };
}

// Quality is derived from file/folder names only — there is no quality column.
// 4K/2160p/UHD are one tier so the same release never shows "4K / 2160p".
const QUALITY_ORDER = ["4K", "1080p", "720p", "480p"] as const;
const QUALITY_TOKENS: Record<string, (typeof QUALITY_ORDER)[number]> = {
  "4k": "4K",
  uhd: "4K",
  "2160p": "4K",
  "1080p": "1080p",
  "720p": "720p",
  "480p": "480p",
};

function parseQualities(path: string | null | undefined): string[] {
  if (!path) return [];
  // Read the FILE name, not its parent folder: folders often name a release
  // "1080p" while the actual file inside is a different resolution.
  const fileName = path.replace(/\\/g, "/").split("/").pop() ?? "";
  const withoutExtension = fileName.replace(/\.[A-Za-z0-9]{2,4}$/g, "");
  const tokens = withoutExtension.split(/[^A-Za-z0-9]+/).filter(Boolean).map((t) => t.toLowerCase());
  // "4K Remastered" / "4K-RES" are marketing labels on a 1080p or 720p file,
  // and "UHD.BluRay" describes the source/master on many 1080p encodes. When
  // an explicit resolution is present, bare 4k/uhd are not separate tiers.
  const hasExplicitResolution = tokens.some((t) => ["2160p", "1080p", "720p", "480p"].includes(t));
  const found = new Set<string>();
  for (const token of tokens) {
    const quality = QUALITY_TOKENS[token];
    if (!quality) continue;
    if ((token === "4k" || token === "uhd") && hasExplicitResolution) continue;
    found.add(quality);
  }
  return QUALITY_ORDER.filter((q) => found.has(q as (typeof QUALITY_ORDER)[number]));
}

/** Suffix for a single movie file card when the title has multiple copies. */
function qualityLabel(path: string | null | undefined): string {
  const quality = parseQualities(path)[0];
  return quality ? ` — ${quality}` : "";
}

// Duplicate copies often have different metadata titles ("A Charlie Brown
// Thanksgiving" vs the raw 720p folder name). Derive a stable key from the
// release folder, stopping before the year/quality tokens.
function movieCopyKey(item: SourceItem): string {
  const folder = (item.relativePath ?? "").replace(/\\/g, "/").split("/").filter(Boolean)[0] ?? "";
  let cleaned = (folder || item.title || "")
    .replace(/[._+]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
  const cut = cleaned.search(/\b(?:(?:18|19|20)\d{2}|(?:2160|1080|720|480)p|4k|uhd|bluray|blu-ray|x26[45])\b/i);
  if (cut > 0) cleaned = cleaned.slice(0, cut).replace(/[\s(),-]+$/g, "").trim();
  const trailingArticle = /^(.*?)\s+(the|a|an)$/i.exec(cleaned);
  if (trailingArticle) cleaned = `${trailingArticle[2]} ${trailingArticle[1]}`;
  return cleaned.toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
}

/** Suffix for grouped show labels: only shown when more than one quality is present. */
function qualitySuffix(paths: (string | null | undefined)[]): string {
  const distinct = new Set<string>();
  for (const path of paths) for (const q of parseQualities(path)) distinct.add(q);
  const ordered = QUALITY_ORDER.filter((q) => distinct.has(q));
  return ordered.length > 1 ? ` — ${ordered.join(" / ")}` : "";
}

type HiddenState = "visible" | "pending" | "synced";

/**
 * One fixed-height bottom row, shared by the live cards AND the LazyMount /
 * first-page skeletons. Sharing the exact style (30px + 4px top padding +
 * 1px border) is what guarantees a skeleton reserves precisely the space the
 * Mark/Hide icons will occupy, so cards never shift when a poster mounts.
 */
const CARD_ACTION_ROW_STYLE: CSSProperties = {
  display: "flex",
  gap: "6px",
  alignItems: "center",
  height: "30px",
  marginTop: "auto",
  paddingTop: "4px",
  borderTop: "1px solid var(--border)",
};

/** True once a Sync has completed strictly after the state change was made. */
function reconcileStatus(at: string | null | undefined, lastReconciledAt?: string | null): "pending" | "synced" {
  if (!at || !lastReconciledAt) return "pending";
  // Equal timestamps mean the mark/hide was applied by that very Sync, so it
  // stays yellow -- exactly the rule the check icon has always used.
  return new Date(lastReconciledAt) > new Date(at) ? "synced" : "pending";
}

/**
 * Fixed-height action row shared by movie and show cards. Keeping it in one
 * place (and pinned with marginTop:auto) means the Mark/Hide icons sit at the
 * same spot on every poster regardless of how many title lines wrap.
 */
function CardActions({
  markState,
  approving,
  hiddenState,
  hiding,
  inHiddenView,
  onApprove,
  onUnapprove,
  onHide,
  onUnhide,
  onHideAndUnapprove,
  onApproveAndUnhide,
}: {
  markState: MarkState;
  approving: boolean;
  hiddenState: HiddenState;
  hiding: boolean;
  inHiddenView: boolean;
  onApprove: () => void;
  onUnapprove: () => void;
  onHide: () => void;
  onUnhide: () => void;
  onHideAndUnapprove: () => void;
  onApproveAndUnhide: () => void;
}) {
  const hidden = hiddenState !== "visible";
  const markColor =
    markState === "synced"
      ? "var(--color-success)"
      : markState === "pending"
        ? PENDING_YELLOW
        : "var(--text-muted)";
  const markLabel =
    markState === "synced"
      ? "In this library — click to unmark"
      : markState === "pending"
        ? "Marked — click Sync to add to the library; click to unmark"
        : "Mark for library";
  // Same three colours, and the same reconciliation rule, as the check icon:
  // yellow until a Sync finishes after the hide, green afterwards.
  const eyeColor =
    hiddenState === "synced"
      ? "var(--color-success)"
      : hiddenState === "pending"
        ? PENDING_YELLOW
        : "var(--text-muted)";
  const hideRequested = hiddenState === "pending" && !inHiddenView;
  const eyeLabel =
    hideRequested
      ? "Hide requested — click Sync to apply; click to cancel"
      : hiddenState === "synced"
        ? "Hidden and synced — click to unhide"
        : hiddenState === "pending"
          ? "Unhide requested — still hidden until Sync; click to cancel"
          : "Visible in this library — click to hide";

  // Library and Hidden are mutually exclusive. Switching to the opposite state
  // needs an explicit confirmation because it removes the item from the other
  // list, and that removal only takes effect on disk / in Plex after a Sync.
  const handleMarkClick = () => {
    if (markState !== "unmarked") {
      onUnapprove();
      return;
    }
    if (hidden) {
      const ok = window.confirm(
        "Choosing Library removes this title from Hidden.\n\n" +
        "Unhiding and the library change still need a Sync to take effect on disk and in Plex. Continue?"
      );
      if (ok) onApproveAndUnhide();
      return;
    }
    onApprove();
  };

  const handleHideClick = () => {
    if (hidden) {
      onUnhide();
      return;
    }
    if (markState !== "unmarked") {
      const ok = window.confirm(
        "Choosing Hidden removes this title from the Library.\n\n" +
        "The removal still needs a Sync to take effect on disk and in Plex. Continue?"
      );
      if (ok) onHideAndUnapprove();
      return;
    }
    onHide();
  };

  // Each button only dims while ITS OWN request is in flight, so hiding an
  // item doesn't make its Mark icon look disabled (and vice versa).
  const iconButtonStyle = (busy: boolean): CSSProperties => ({
    width: "28px",
    height: "26px",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    borderRadius: "var(--radius)",
    border: "1px solid var(--border)",
    background: "transparent",
    cursor: busy ? "default" : "pointer",
    opacity: busy ? 0.6 : 1,
    padding: 0,
  });

  return (
    <div className="card-action-row" style={CARD_ACTION_ROW_STYLE}>
      <button
        type="button"
        className="card-action-button"
        onClick={handleMarkClick}
        disabled={approving}
        title={markLabel}
        aria-label={markLabel}
        style={iconButtonStyle(approving)}
      >
        <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true">
          <path
            d="M20 6 9 17l-5-5"
            fill="none"
            stroke={markColor}
            strokeWidth="2.8"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      </button>
      <button
        type="button"
        className="card-action-button"
        onClick={handleHideClick}
        disabled={hiding}
        title={eyeLabel}
        aria-label={eyeLabel}
        style={iconButtonStyle(hiding)}
      >
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
          <path
            d="M1.5 12S5 5.5 12 5.5 22.5 12 22.5 12 19 18.5 12 18.5 1.5 12 1.5 12Z"
            fill="none"
            stroke={eyeColor}
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <circle cx="12" cy="12" r="3" fill="none" stroke={eyeColor} strokeWidth="2" />
          {hidden && inHiddenView && (
            <>
              <circle cx="12" cy="12" r="9.5" fill="none" stroke="var(--color-danger)" strokeWidth="1.8" />
              <path
                d="M5.3 5.3 18.7 18.7"
                fill="none"
                stroke="var(--color-danger)"
                strokeWidth="1.8"
                strokeLinecap="round"
              />
            </>
          )}
        </svg>
      </button>
    </div>
  );
}

interface LiveScanEvent {
  id: string;
  title: string;
  posterUrl: string | null;
  year: number | null;
  mediaType: "Movie" | "Show" | "Anime";
  completed: number;
  total: number;
  status: string;
}

interface Props {
  libraryId: string | null; // which library "approve" targets; null = browse-only
  libraryMediaType?: "Movie" | "Show" | "Anime" | null; // when set, only render that media type
  refreshKey?: number; // bump this to force a refetch of source items (e.g. after a scan)
  scanning?: boolean; // when true, subscribes to live scan updates and upserts cards as they arrive
  lastReconciledAt?: string | null; // last successful Sync; distinguishes marked from in-library
  isAdmin?: boolean; // unlocks the Unmatched view and Match action
}

type ViewFilter = "all" | "library" | "unmarked" | "hidden" | "unmatched";

type MatchTarget =
  | { kind: "movie"; item: SourceItem }
  | { kind: "show"; group: ShowGroup };
type MarkState = "unmarked" | "pending" | "synced";
type MarkBulkAction = "none" | "mark" | "unmark";
type HideBulkAction = "none" | "hide" | "unhide";

type SelectedItem =
  | { kind: "movie"; item: SourceItem }
  | { kind: "show"; group: ShowGroup };

type RenderEntry =
  | { kind: "movie"; item: SourceItem; title: string }
  | { kind: "show"; group: ShowGroup; title: string };

const ALL_LETTERS = ["#", ..."ABCDEFGHIJKLMNOPQRSTUVWXYZ".split("")];

// Server-side paging is the default. Set VITE_LIBRARY_PAGING=false to keep the
// previous listSourceItems()-based path side by side for comparison.
const PAGING_ENABLED = import.meta.env.VITE_LIBRARY_PAGING !== "false";
const PAGE_SIZE = 60;
const SEARCH_DEBOUNCE_MS = 250;

// Toolbar affordances (Clear / Hide / Show filters) and the inline status text.
const toolbarButtonStyle: CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "12px",
  padding: "4px 8px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--border)",
  background: "transparent",
  color: "var(--text-secondary)",
  cursor: "pointer",
};

const toolbarStatusStyle: CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "12px",
  color: "var(--text-secondary)",
  whiteSpace: "nowrap",
};

/** Top-right checkbox overlay shown only while multiselect is active. */
function SelectionCheckbox({ selected, onToggle }: { selected: boolean; onToggle: () => void }) {
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation();
        onToggle();
      }}
      aria-pressed={selected}
      aria-label={selected ? "Deselect title" : "Select title"}
      style={{
        position: "absolute",
        top: "6px",
        right: "6px",
        zIndex: 3,
        width: "22px",
        height: "22px",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        borderRadius: "6px",
        border: `1px solid ${selected ? "var(--color-primary, #3b82f6)" : "var(--border-strong)"}`,
        background: selected ? "var(--color-primary, #3b82f6)" : "var(--surface-card)",
        color: selected ? "#fff" : "var(--text-primary)",
        fontFamily: "var(--font-body)",
        fontSize: "13px",
        lineHeight: 1,
        cursor: "pointer",
      }}
    >
      {selected ? "✓" : ""}
    </button>
  );
}

/** Magnifying-glass overlay for opening the Match modal on an unmatched card. */
function MatchIconButton({ onClick, style }: { onClick: () => void; style?: CSSProperties }) {
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation();
        onClick();
      }}
      title="Match"
      aria-label="Match"
      style={{
        position: "absolute",
        right: "6px",
        zIndex: 2,
        width: "22px",
        height: "22px",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        borderRadius: "999px",
        border: "1px solid var(--color-primary, #3b82f6)",
        background: "var(--color-primary, #3b82f6)",
        color: "var(--text-on-accent)",
        cursor: "pointer",
        padding: 0,
        ...style,
      }}
    >
      <svg
        width="13"
        height="13"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <circle cx="11" cy="11" r="7" />
        <line x1="20" y1="20" x2="16.65" y2="16.65" />
      </svg>
    </button>
  );
}

/** Toolbar icon for bulk selection, styled like a stack of checked boxes. */
function MultiselectButton({ active, selectedCount, onToggle }: {
  active: boolean;
  selectedCount: number;
  onToggle: () => void;
}) {
  const label = active
    ? `Exit multiselect${selectedCount ? ` (${selectedCount})` : ""}`
    : "Multiselect";
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-pressed={active}
      aria-label={label}
      title={label}
      style={{
        ...toolbarButtonStyle,
        width: "30px",
        height: "30px",
        padding: 0,
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        color: active ? "var(--text-on-accent)" : "var(--text-secondary)",
        background: active ? "var(--color-primary)" : "transparent",
        borderColor: active ? "var(--color-primary)" : "var(--border)",
      }}
    >
      <svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true">
        <rect x="2.5" y="2.5" width="13" height="13" rx="2" fill="none" stroke="currentColor" strokeWidth="1.8" />
        <path d="m5.8 9 2.2 2.2 4-4" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
        <rect x="8.5" y="8.5" width="13" height="13" rx="2" fill="var(--surface-page)" stroke="currentColor" strokeWidth="1.8" />
        <path d="m11.8 15 2.2 2.2 4-4" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    </button>
  );
}

// TMDB Genre categories with pretty names for filtering
const TMDB_GENRE_FILTERS = [
  { id: 28, name: "Action", icon: "💥" },
  { id: 12, name: "Adventure", icon: "🗺️" },
  { id: 16, name: "Animation", icon: "🎨" },
  { id: 35, name: "Comedy", icon: "😂" },
  { id: 80, name: "Crime", icon: "🕵️" },
  { id: 99, name: "Documentary", icon: "📹" },
  { id: 18, name: "Drama", icon: "🎭" },
  { id: 10751, name: "Family", icon: "👨‍👩‍👧‍👦" },
  { id: 14, name: "Fantasy", icon: "🏰" },
  { id: 36, name: "History", icon: "📜" },
  { id: 27, name: "Horror", icon: "👻" },
  { id: 10402, name: "Music", icon: "🎵" },
  { id: 9648, name: "Mystery", icon: "🔍" },
  { id: 10749, name: "Romance", icon: "💕" },
  { id: 878, name: "Sci-Fi", icon: "🚀" },
  { id: 10770, name: "TV Movie", icon: "📺" },
  { id: 53, name: "Thriller", icon: "😰" },
  { id: 10752, name: "War", icon: "⚔️" },
  { id: 37, name: "Western", icon: "🤠" },
];

// Genre options in the shape SelectFilter expects
const GENRE_FILTER_OPTIONS = TMDB_GENRE_FILTERS.map((g) => ({
  value: g.id,
  label: g.name,
  icon: g.icon,
}));

// Canonical ordering for rating filter options (US certifications). Any
// unexpected value found in the data is appended alphabetically.
const MOVIE_RATING_ORDER = ["G", "PG", "PG-13", "R", "NC-17", "NR", "Unrated"];
const TV_RATING_ORDER = ["TV-Y", "TV-Y7", "TV-Y7-FV", "TV-G", "TV-PG", "TV-14", "TV-MA"];

interface FilterOption<T> {
  value: T;
  label: string;
  icon?: string;
}

const filterButtonStyle = {
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  padding: "6px 12px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--border)",
  background: "var(--surface-card)",
  color: "var(--text-primary)",
  cursor: "pointer",
  display: "flex",
  alignItems: "center",
  gap: "0.5rem",
};

const filterOptionStyle = (active: boolean): CSSProperties => ({
  width: "100%",
  textAlign: "left",
  padding: "8px 12px",
  border: "none",
  background: active ? "var(--surface-raised)" : "transparent",
  color: "var(--text-primary)",
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  cursor: "pointer",
  display: "flex",
  alignItems: "center",
  gap: "0.5rem",
});

/**
 * Dropdown filter used by the toolbar (genre, rating, studio, network).
 * Opening it kicks off the details backfill via onToggle so option lists
 * fill in from TMDB instead of staying empty.
 */
function SelectFilter<T extends string | number>({
  allLabel,
  value,
  options,
  isOpen,
  onToggle,
  onChange,
  backfilling = false,
  emptyHint = "No data yet",
}: {
  allLabel: string;
  value: T | null;
  options: FilterOption<T>[];
  isOpen: boolean;
  onToggle: () => void;
  onChange: (value: T | null) => void;
  backfilling?: boolean;
  emptyHint?: string;
}) {
  const selected = value === null ? null : options.find((o) => o.value === value);
  return (
    <div style={{ position: "relative", display: "inline-block" }}>
      {/* The label always reflects the current selection -- loading state is
          reported separately in the toolbar status text, never here. */}
      <button onClick={onToggle} style={filterButtonStyle}>
        {selected ? (
          <>
            {selected.icon ? `${selected.icon} ` : ""}
            {selected.label}
          </>
        ) : (
          <span>{allLabel}</span>
        )}
        <span style={{ fontSize: "10px" }}>{isOpen ? "▾" : "▸"}</span>
      </button>
      {isOpen && (
        <div
          style={{
            position: "absolute",
            top: "100%",
            left: 0,
            zIndex: 100,
            background: "var(--surface-card)",
            border: "1px solid var(--border)",
            borderRadius: "var(--radius)",
            boxShadow: "0 4px 12px rgba(0,0,0,0.15)",
            minWidth: "200px",
            maxHeight: "300px",
            overflowY: "auto",
            marginTop: "4px",
          }}
        >
          <button onClick={() => onChange(null)} style={filterOptionStyle(value === null)}>
            {allLabel}
          </button>
          {options.map((opt) => (
            <button
              key={opt.value}
              onClick={() => onChange(opt.value)}
              style={filterOptionStyle(value === opt.value)}
            >
              {opt.icon ? `${opt.icon} ` : ""}
              {opt.label}
            </button>
          ))}
          {options.length === 0 && (
            <div style={{ padding: "8px 12px", fontSize: "12px", color: "var(--text-secondary)" }}>
              {backfilling ? "Loading..." : emptyHint}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function labelFor(item: SourceItem): string {
  return (item.mediaType === "Movie" ? item.title : item.showTitle) ?? "Untitled";
}

function letterFor(label: string): string {
  const c = label.trim().charAt(0).toUpperCase();
  return /[A-Z]/.test(c) ? c : "#";
}

function titleForEntry(entry: RenderEntry): string {
  return entry.kind === "movie" ? labelFor(entry.item) : entry.group.title;
}

// The paged endpoint returns pre-grouped cards. Adapt them back into the same
// SourceItem / ShowGroup shapes the cards and detail modal already consume, so
// approve/hide handlers and icon states stay untouched.
function csvFromGenreIds(ids: number[] | null | undefined): string | null {
  return ids && ids.length > 0 ? ids.join(",") : null;
}

function entryToSourceItem(entry: LibraryEntry): SourceItem {
  const isMovie = entry.kind === "movie";
  return {
    id: entry.id ?? entry.representativeId ?? entry.key ?? "",
    mediaType: isMovie ? "Movie" : entry.mediaType,
    title: isMovie ? entry.title : null,
    year: entry.year ?? null,
    showTitle: isMovie ? null : entry.title,
    seasonNumber: isMovie ? null : (entry.latestSeason ?? null),
    episodeNumber: isMovie ? null : (entry.latestEpisode ?? null),
    tmdbId: entry.tmdbId ?? null,
    posterUrl: entry.posterUrl ?? null,
    overview: entry.overview ?? null,
    relativePath: entry.relativePath ?? null,
    genreIds: csvFromGenreIds(entry.genreIds),
    rating: entry.rating ?? null,
    productionCompanies: entry.productionCompanies ?? null,
    networks: entry.networks ?? null,
    director: entry.director ?? null,
    creators: entry.creators ?? null,
    cast: entry.cast ?? null,
    approved: entry.approved,
    approvedAt: entry.approvedAt ?? null,
    hidden: entry.hidden,
    hiddenAt: entry.hiddenAt ?? null,
    unhideRequestedAt: entry.unhideRequestedAt ?? null,
  };
}

function entryToShowGroup(entry: LibraryEntry): ShowGroup {
  const representative = entryToSourceItem(entry);
  return {
    key: entry.key ?? "",
    title: entry.title,
    mediaType: entry.mediaType === "Anime" ? "Anime" : "Show",
    posterUrl: entry.posterUrl ?? null,
    overview: entry.overview ?? null,
    tmdbId: entry.tmdbId ?? null,
    genreIds: csvFromGenreIds(entry.genreIds),
    rating: entry.rating ?? null,
    networks: entry.networks ?? null,
    creators: entry.creators ?? null,
    cast: entry.cast ?? null,
    episodes: [representative],
    episodeIds: entry.episodeIds ?? [],
    episodeCount: entry.episodeCount ?? 0,
    seasonCount: entry.seasonCount ?? 0,
    latestSeason: entry.latestSeason ?? null,
    latestEpisode: entry.latestEpisode ?? null,
    representativeId: entry.representativeId ?? representative.id,
  };
}

function libraryEntryKey(entry: LibraryEntry): string {
  return entry.kind === "movie" ? `movie:${entry.id}` : `show:${entry.key}`;
}

function toRenderEntry(entry: LibraryEntry): RenderEntry {
  return entry.kind === "movie"
    ? { kind: "movie", item: entryToSourceItem(entry), title: entry.title }
    : { kind: "show", group: entryToShowGroup(entry), title: entry.title };
}

/** Stable selection key for a rendered card (mirrors libraryEntryKey). */
function renderEntryKey(entry: RenderEntry): string {
  return entry.kind === "movie" ? `movie:${entry.item.id}` : `show:${entry.group.key}`;
}

/** Initial-load overlay: cover the card area without rendering blank cards. */
function InitialLoadingPlaceholder() {
  return (
    <div
      role="status"
      aria-live="polite"
      style={{
        minHeight: "min(70vh, 640px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "1rem",
        border: "1px solid var(--border)",
        borderRadius: "var(--radius-card)",
        background: "var(--surface-raised)",
      }}
    >
      <div
        style={{
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          gap: "0.75rem",
          minWidth: "min(280px, 100%)",
          padding: "1.5rem 2rem",
          border: "1px solid var(--border-strong)",
          borderRadius: "var(--radius-card)",
          background: "var(--surface-card)",
          boxShadow: "0 12px 32px rgba(0, 0, 0, 0.25)",
        }}
      >
        <span className="loading-indicator">Loading library…</span>
      </div>
    </div>
  );
}

/** Admin-only Match flow for an unmatched movie or show/anime group. */
function MatchModal({
  target,
  onClose,
  onApplied,
}: {
  target: MatchTarget;
  onClose: () => void;
  onApplied: (title: string) => Promise<void>;
}) {
  const { isCompact } = useViewport();
  const isMovie = target.kind === "movie";
  const mediaType = isMovie ? target.item.mediaType : target.group.mediaType;
  const displayTitle = isMovie
    ? target.item.title && target.item.title !== "Untitled"
      ? target.item.title
      : (target.item.relativePath ?? "Untitled")
    : target.group.title;
  const episodeSource = !isMovie
    ? target.group.episodes.find((e) => e.id === target.group.representativeId)?.relativePath
    : undefined;
  const source = isMovie
    ? target.item.title && target.item.title !== "Untitled"
      ? target.item.title
      : (target.item.relativePath ?? displayTitle)
    : displayTitle && displayTitle !== "Untitled"
      ? displayTitle
      : (episodeSource ?? displayTitle);
  const initial = normalizeTitleQuery(source);

  const [query, setQuery] = useState(initial.title);
  const [year, setYear] = useState(initial.year?.toString() ?? "");
  const [results, setResults] = useState<TitleSearchResult[]>([]);
  const [available, setAvailable] = useState(true);
  const [searched, setSearched] = useState(false);
  const [loading, setLoading] = useState(false);
  const [applyingTmdbId, setApplyingTmdbId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const autoSearched = useRef(false);

  const modalInputStyle: CSSProperties = {
    fontFamily: "var(--font-body)", fontSize: "13px", padding: "6px 8px",
    borderRadius: "var(--radius)", border: "1px solid var(--border)",
    background: "var(--surface-page)", color: "var(--text-primary)",
  };
  const modalButtonStyle: CSSProperties = {
    fontFamily: "var(--font-body)", fontSize: "13px", padding: "6px 12px",
    borderRadius: "var(--radius)", border: "1px solid var(--color-primary)",
    background: "var(--color-primary)", color: "var(--text-on-accent)", cursor: "pointer",
  };

  async function runSearch(q: string, y: string) {
    setLoading(true);
    setError(null);
    setSearched(true);
    try {
      const res = await api.searchMetadata(q, y ? Number(y) : null, mediaType);
      setAvailable(res.available);
      setResults(res.results);
    } catch (e) {
      setError((e as Error).message);
      setResults([]);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (autoSearched.current) return;
    autoSearched.current = true;
    if (initial.title) void runSearch(initial.title, initial.year?.toString() ?? "");
    else setSearched(true);
    // Auto-search only once, on open; manual re-searches use the form below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function apply(result: TitleSearchResult) {
    setApplyingTmdbId(result.tmdbId);
    setError(null);
    try {
      await api.matchMetadata({
        ...(isMovie
          ? { sourceItemId: target.item.id }
          : { representativeId: target.group.representativeId }),
        tmdbId: result.tmdbId,
        mediaType,
        title: result.title,
        year: result.year,
        posterUrl: result.posterUrl,
        overview: result.overview,
      });
      await onApplied(result.title);
      onClose();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setApplyingTmdbId(null);
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={`Match ${displayTitle}`}
      onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}
      style={{
        position: "fixed", inset: 0, zIndex: 1000, display: "flex",
        alignItems: "center", justifyContent: "center", padding: isCompact ? 0 : "1rem",
        background: "rgba(0,0,0,0.55)",
      }}
    >
      <div
        style={{
          width: isCompact ? "100%" : "min(560px, 100%)",
          height: isCompact ? "100%" : undefined,
          maxHeight: isCompact ? "none" : "85vh", overflowY: "auto",
          background: "var(--surface-card)", color: "var(--text-primary)",
          border: "1px solid var(--border)", borderRadius: isCompact ? 0 : "var(--radius-card)",
          padding: "1.25rem", fontFamily: "var(--font-body)",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "1rem" }}>
          <h3 style={{ margin: 0, fontFamily: "var(--font-heading)", fontSize: "18px" }}>
            Match “{displayTitle}”
          </h3>
          <button className="modal-button" onClick={onClose} aria-label="Close" style={toolbarButtonStyle}>✕</button>
        </div>
        <p style={{ fontSize: "12px", color: "var(--text-secondary)", margin: "0.5rem 0 1rem" }}>
          {isMovie
            ? "Applied metadata is kept across later scans; detail fields are refreshed by TMDB."
            : `Applies to all ${target.group.episodeCount} episode rows in this title. Metadata is refreshed by TMDB after the match.`}
        </p>

        <form
          onSubmit={(e) => { e.preventDefault(); void runSearch(query, year); }}
          style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}
        >
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Title to search"
            aria-label="Title to search"
            className="modal-input"
            style={{ ...modalInputStyle, flex: "1 1 220px" }}
          />
          <input
            value={year}
            onChange={(e) => setYear(e.target.value)}
            placeholder="Year"
            aria-label="Year"
            inputMode="numeric"
            className="modal-input"
            style={{ ...modalInputStyle, width: "76px" }}
          />
          <button className="modal-button" type="submit" disabled={loading || !query.trim()} style={modalButtonStyle}>
            {loading ? "Searching…" : "Search"}
          </button>
        </form>

        {error && <p style={{ color: "var(--color-danger)", fontSize: "13px", marginTop: "0.75rem" }}>{error}</p>}

        {searched && !loading && !available && (
          <p style={{ fontSize: "13px", color: "var(--text-secondary)", marginTop: "1rem" }}>
            TMDB search is unavailable on this server (no API key configured). You can still correct the title/year above and try again.
          </p>
        )}
        {searched && !loading && available && results.length === 0 && (
          <p style={{ fontSize: "13px", color: "var(--text-secondary)", marginTop: "1rem" }}>
            No TMDB candidates found. Edit the title/year and search again.
          </p>
        )}

        <div style={{ display: "flex", flexDirection: "column", gap: "0.5rem", marginTop: "1rem" }}>
          {results.map((result) => (
            <div
              key={`${result.mediaType}:${result.tmdbId}`}
              style={{ display: "flex", alignItems: "center", gap: "0.75rem", padding: "6px", border: "1px solid var(--border)", borderRadius: "var(--radius)" }}
            >
              {result.posterUrl && (
                <img src={posterThumb(result.posterUrl)} alt="" style={{ width: "36px", height: "54px", objectFit: "cover", borderRadius: "3px" }} />
              )}
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: "13px", fontWeight: 600 }}>{result.title}</div>
                <div style={{ fontSize: "12px", color: "var(--text-secondary)" }}>
                  {result.year ?? "—"} · {result.mediaType}
                </div>
              </div>
              <button
                className="modal-button"
                onClick={() => void apply(result)}
                disabled={applyingTmdbId !== null}
                style={modalButtonStyle}
              >
                {applyingTmdbId === result.tmdbId ? "Applying…" : "Apply"}
              </button>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

export function PosterGrid({ libraryId, libraryMediaType, refreshKey, scanning, lastReconciledAt, isAdmin = false }: Props) {
  const [items, setItems] = useState<SourceItem[]>([]);
  const [approvedIds, setApprovedIds] = useState<Set<string>>(new Set());
  const [hiddenIds, setHiddenIds] = useState<Set<string>>(new Set());
  const [viewFilter, setViewFilter] = useState<ViewFilter>("all");
  const [matchTarget, setMatchTarget] = useState<MatchTarget | null>(null);
  const [hidingKey, setHidingKey] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [approvingKey, setApprovingKey] = useState<string | null>(null);
  const [refreshingKey, setRefreshingKey] = useState<string | null>(null);
  const [selected, setSelected] = useState<SelectedItem | null>(null);
  // Bulk card selection. Keys are renderEntryKey values so movie/show cards
  // cannot collide, and selection is discarded whenever the result set changes.
  const [selectionMode, setSelectionMode] = useState(false);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [markAction, setMarkAction] = useState<MarkBulkAction>("none");
  const [hideAction, setHideAction] = useState<HideBulkAction>("none");
  const [bulkBusy, setBulkBusy] = useState(false);
  const [genreFilter, setGenreFilter] = useState<number | null>(null);
  const [ratingFilter, setRatingFilter] = useState<string | null>(null);
  const [studioFilter, setStudioFilter] = useState<string | null>(null); // movies
  const [networkFilter, setNetworkFilter] = useState<string | null>(null); // shows/anime
  const [searchQuery, setSearchQuery] = useState<string>(""); // free-text search (token-AND)
  const [openFilter, setOpenFilter] = useState<string | null>(null); // which dropdown is open
  const [filtersHidden, setFiltersHidden] = useState(false); // toolbar starts visible on the Library tab
  const { isMobile } = useViewport();
  const [detailsBackfilling, setDetailsBackfilling] = useState(false);
  // Inline status text shown after the filter dropdowns while the details
  // backfill runs ("Loading... 12,500 / 38,212", then "✓ Completed").
  const [detailsStatus, setDetailsStatus] = useState<string | null>(null);
  const detailsStatusTimerRef = useRef<number | null>(null); // auto-hide timer for the completed state
  const detailsRunningRef = useRef(false); // guards against double-starting the backfill loop
  const liveConnectionRef = useRef<signalR.HubConnection | null>(null);
  const { showToast } = useToast();

  // ---- Paged path state (VITE_LIBRARY_PAGING) ---------------------------
  const [pagedEntries, setPagedEntries] = useState<LibraryEntry[]>([]);
  const [pagedTotal, setPagedTotal] = useState(0);
  const [pageOffset, setPageOffset] = useState(0);
  const [hasMore, setHasMore] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [debouncedQuery, setDebouncedQuery] = useState(searchQuery);
  const [facets, setFacets] = useState<LibraryFacets | null>(null);
  const [letterFilter, setLetterFilter] = useState<string | null>(null);
  const requestSeqRef = useRef(0); // drops responses from superseded filters
  const appendInFlightRef = useRef(false);
  const sentinelRef = useRef<HTMLDivElement | null>(null);
  const scanReloadRef = useRef(0);
  const reloadRef = useRef<() => void>(() => {});

  const effectiveSearchQuery = PAGING_ENABLED ? debouncedQuery : searchQuery;

  useEffect(() => {
    if (!PAGING_ENABLED) return;
    const handle = window.setTimeout(() => setDebouncedQuery(searchQuery), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [searchQuery]);

  function setMultiselect(next: boolean) {
    setSelectionMode(next);
    setSelectedKeys(new Set());
    setMarkAction("none");
    setHideAction("none");
  }

  function toggleSelect(key: string) {
    setSelectedKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  const pageParams = useCallback((offset: number): LibraryEntriesParams => ({
    libraryId,
    mediaType: libraryMediaType,
    q: effectiveSearchQuery || null,
    genre: genreFilter,
    rating: ratingFilter,
    studio: studioFilter,
    network: networkFilter,
    letter: letterFilter,
    view: viewFilter === "unmatched" ? "all" : viewFilter,
    unmatched: isAdmin && viewFilter === "unmatched",
    offset,
    limit: PAGE_SIZE,
  }), [libraryId, libraryMediaType, effectiveSearchQuery, genreFilter, ratingFilter, studioFilter, networkFilter, letterFilter, viewFilter, isAdmin]);

  const fetchPage = useCallback(async (offset: number, replace: boolean) => {
    // Only appends are coalesced; a filter change must always supersede an
    // in-flight "load more" request.
    if (!replace && appendInFlightRef.current) return;
    if (replace) appendInFlightRef.current = true;
    const seq = ++requestSeqRef.current;
    try {
      const page = await api.listLibraryEntries(pageParams(offset));
      if (seq !== requestSeqRef.current) return;
      setPagedTotal(page.total);
      setPageOffset(page.offset + page.entries.length);
      setHasMore(page.hasMore);
      setPagedEntries((prev) => {
        if (replace) return page.entries;
        const seen = new Set(prev.map(libraryEntryKey));
        return [...prev, ...page.entries.filter((entry) => !seen.has(libraryEntryKey(entry)))];
      });
      setError(null);
    } catch (e) {
      if (seq === requestSeqRef.current) setError((e as Error).message);
    } finally {
      appendInFlightRef.current = false;
      if (replace) setLoading(false);
      setLoadingMore(false);
    }
  }, [pageParams]);

  const loadFacets = useCallback(async () => {
    try {
      setFacets(await api.listLibraryFacets({
        libraryId,
        mediaType: libraryMediaType,
        view: viewFilter === "unmatched" ? "all" : viewFilter,
        unmatched: isAdmin && viewFilter === "unmatched",
      }));
    } catch {
      setFacets(null);
    }
  }, [libraryId, libraryMediaType, viewFilter, isAdmin]);

  useEffect(() => {
    reloadRef.current = () => {
      void fetchPage(0, true);
      void loadFacets();
    };
  }, [fetchPage, loadFacets]);

  // Any filter change refetches page 0; the callback identity carries all of
  // them, so this effect is the single reset point for offset/entries.
  useEffect(() => {
    if (!PAGING_ENABLED) return;
    setLoading(true);
    setError(null);
    setPagedEntries([]);
    setPagedTotal(0);
    setPageOffset(0);
    setHasMore(false);
    setSelectedKeys(new Set());
    setMarkAction("none");
    setHideAction("none");
    void fetchPage(0, true);
    void loadFacets();
  }, [fetchPage, loadFacets, refreshKey]);

  const loadNextPage = useCallback(() => {
    if (!PAGING_ENABLED || !hasMore || loading || loadingMore) return;
    setLoadingMore(true);
    void fetchPage(pageOffset, false);
  }, [hasMore, loading, loadingMore, pageOffset, fetchPage]);

  useEffect(() => {
    if (!PAGING_ENABLED) return;
    const el = sentinelRef.current;
    if (!el || typeof IntersectionObserver === "undefined") return;
    const observer = new IntersectionObserver(
      (observerEntries) => {
        if (observerEntries.some((entry) => entry.isIntersecting)) loadNextPage();
      },
      { rootMargin: "600px 0px" }
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, [loadNextPage]);

  // Keep the per-item eye icons in sync with the server's group annotations,
  // including optimistic hide/unhide patches.
  useEffect(() => {
    if (!PAGING_ENABLED) return;
    const ids = new Set<string>();
    for (const entry of pagedEntries) {
      if (!entry.hidden) continue;
      const rowIds = entry.kind === "movie" ? [entry.id ?? ""] : (entry.episodeIds ?? []);
      for (const id of rowIds) if (id) ids.add(id);
    }
    setHiddenIds(ids);
  }, [pagedEntries]);

  // Cancel any pending status auto-hide when the grid unmounts.
  useEffect(() => {
    return () => {
      if (detailsStatusTimerRef.current !== null) clearTimeout(detailsStatusTimerRef.current);
    };
  }, []);

  // Every catalog fetch must go through this helper so the per-library
  // hidden flags (and approval timestamps) survive refreshes, not just the
  // initial load. Without the libraryId argument the API omits annotations.
  const applySourceRows = (rows: SourceItem[]) => {
    setItems(rows);
    setHiddenIds(new Set(rows.filter((r) => r.hidden).map((r) => r.id)));
  };

  useEffect(() => {
    if (PAGING_ENABLED) return;
    setLoading(true);
    api
      .listSourceItems(libraryId)
      .then(applySourceRows)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [PAGING_ENABLED, refreshKey, libraryId]);

  // Approval timestamps come from the same annotated grid response, so the
  // check icon can distinguish "just marked" from "already in the library".
  const approvedAtById = useMemo(() => {
    const map = new Map<string, string | null>();
    const rows: SourceItem[] = PAGING_ENABLED
      ? pagedEntries.map((entry) => entryToSourceItem(entry))
      : items;
    for (const item of rows) {
      if (item.approved) map.set(item.id, item.approvedAt ?? null);
    }
    return map;
  }, [PAGING_ENABLED, items, pagedEntries]);

  // Hide timestamps come from the same annotated responses. For paged show
  // groups the server already folds episodeIds into one newest stamp; we still
  // take the max so the legacy (client-grouped) path behaves identically.
  const hiddenAtById = useMemo(() => {
    const map = new Map<string, string>();
    if (PAGING_ENABLED) {
      for (const entry of pagedEntries) {
        const at = entry.hiddenAt;
        if (!at) continue;
        const rowIds = entry.kind === "movie" ? [entry.id ?? ""] : (entry.episodeIds ?? []);
        for (const id of rowIds) if (id) map.set(id, at);
      }
    } else {
      for (const item of items) {
        // A pending hide has hidden=false but still carries a stamp -- that is
        // what turns the eye yellow without removing the card before Sync.
        if (item.hiddenAt) map.set(item.id, item.hiddenAt);
      }
    }
    return map;
  }, [PAGING_ENABLED, items, pagedEntries]);

  const markStateFor = (ids: string[]): MarkState => {
    const id = ids.find((candidate) => approvedIds.has(candidate));
    if (!id) return "unmarked";
    return reconcileStatus(approvedAtById.get(id), lastReconciledAt);
  };

  const hiddenFor = (ids: string[]): boolean =>
    ids.length > 0 && ids.every((id) => hiddenIds.has(id));

  const hiddenAtFor = (ids: string[]): string | null => {
    let newest: Date | null = null;
    let stamp: string | null = null;
    for (const id of ids) {
      const at = hiddenAtById.get(id);
      if (!at) continue;
      const date = new Date(at);
      if (!newest || date > newest) {
        newest = date;
        stamp = at;
      }
    }
    return stamp;
  };

  // Hide is two-phase, like Mark. A stamp on a still-visible card is a hide
  // waiting for Sync (yellow). A hidden card compares its current request stamp
  // to LastReconciledAt: yellow while an unhide is pending, green when the hide
  // itself has been applied.
  const hiddenStateFor = (ids: string[]): HiddenState => {
    const stamp = hiddenAtFor(ids);
    if (!hiddenFor(ids)) return stamp ? "pending" : "visible";
    return reconcileStatus(stamp, lastReconciledAt);
  };

  useEffect(() => {
    if (!libraryId) {
      setApprovedIds(new Set());
      return;
    }
    // Ignore a slow response from a previously selected library.
    let cancelled = false;
    api
      .getApprovedItemIds(libraryId)
      .then((ids) => {
        if (!cancelled) setApprovedIds(new Set(ids));
      })
      .catch(() => {
        if (!cancelled) setApprovedIds(new Set());
      });
    return () => {
      cancelled = true;
    };
  }, [libraryId]);

  // Live grid updates during a scan: as each item resolves server-side,
  // its card appears/updates in the main grid immediately, not just in
  // the small progress strip -- mirrors Sonarr/Radarr's "refresh" behavior.
  // Items stay flat; show groups are derived from them on every render.
  useEffect(() => {
    if (!scanning) {
      liveConnectionRef.current?.stop();
      liveConnectionRef.current = null;
      return;
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/scan")
      .withAutomaticReconnect()
      .build();

    connection.on("ScanProgress", (evt: LiveScanEvent) => {
      if (PAGING_ENABLED) {
        // A scan can emit tens of thousands of events. Refetch page 0 on a
        // completed batch (throttled otherwise) instead of rebuilding an
        // 82k-row client array (research §7.5).
        const now = Date.now();
        const finished = evt.status === "completed" || (evt.total > 0 && evt.completed >= evt.total);
        if (finished || now - scanReloadRef.current > 2000) {
          scanReloadRef.current = now;
          reloadRef.current();
        }
        return;
      }
      setItems((prev) => {
        const idx = prev.findIndex((i) => i.id === evt.id);
        const merged: SourceItem = {
          id: evt.id,
          mediaType: evt.mediaType,
          title: evt.mediaType === "Movie" ? evt.title : (prev[idx]?.title ?? null),
          year: evt.year ?? prev[idx]?.year ?? null,
          showTitle: evt.mediaType === "Movie" ? (prev[idx]?.showTitle ?? null) : evt.title,
          seasonNumber: prev[idx]?.seasonNumber ?? null,
          episodeNumber: prev[idx]?.episodeNumber ?? null,
          tmdbId: prev[idx]?.tmdbId ?? null,
          posterUrl: evt.posterUrl ?? prev[idx]?.posterUrl ?? null,
          overview: prev[idx]?.overview ?? null,
          genreIds: prev[idx]?.genreIds ?? null,
          rating: prev[idx]?.rating ?? null,
          productionCompanies: prev[idx]?.productionCompanies ?? null,
          networks: prev[idx]?.networks ?? null,
          director: prev[idx]?.director ?? null,
          creators: prev[idx]?.creators ?? null,
          cast: prev[idx]?.cast ?? null,
          // A live scan replaces the row but must not drop the per-library
          // flags that drive the mark/hide icons.
          approved: prev[idx]?.approved,
          approvedAt: prev[idx]?.approvedAt,
          hidden: prev[idx]?.hidden,
          hiddenAt: prev[idx]?.hiddenAt ?? null,
          unhideRequestedAt: prev[idx]?.unhideRequestedAt ?? null,
        };
        if (idx === -1) return [merged, ...prev];
        const copy = [...prev];
        copy[idx] = merged;
        return copy;
      });
    });

    connection.start().catch((err) => console.error("SignalR (live grid) connection failed:", err));
    liveConnectionRef.current = connection;

    return () => {
      connection.stop();
    };
  }, [scanning]);

  const entries = useMemo<RenderEntry[]>(() => {
    if (PAGING_ENABLED) {
      // The server already applied library media type, view, q, genre, rating,
      // studio, network and letter. Only the optimistic hidden patch is local.
      return pagedEntries
        .filter((entry) => (viewFilter === "hidden" ? entry.hidden : !entry.hidden))
        .map(toRenderEntry);
    }
    // When a media type is selected, filter out everything that doesn't match.
    // "Movie" library → only actual movies (no season/episode number).
    // "Show"/"Anime" library → only shows/anime (has season and/or episode).
    const filteredItems = libraryMediaType
      ? items.filter((i) => {
          if (libraryMediaType === "Movie") return !i.seasonNumber && !i.episodeNumber;
          return !!(i.seasonNumber || i.episodeNumber);
        })
      : items;
    const movies: RenderEntry[] = filteredItems
      .filter((item) => item.mediaType === "Movie")
      .map((item) => ({ kind: "movie", item, title: labelFor(item) }));
    const showItems = filteredItems.filter((item) => item.mediaType !== "Movie");
    const filteredGroups = libraryMediaType === "Show"
      ? showItems.filter((i) => i.mediaType === "Show")
      : libraryMediaType === "Anime"
        ? showItems.filter((i) => i.mediaType === "Anime")
        : showItems;
    const showGroupsFiltered = buildShowGroups(filteredGroups);
    const shows: RenderEntry[] = showGroupsFiltered.map((group) => ({
      kind: "show",
      group,
      title: group.title,
    }));

    const all = [...movies, ...shows];

    const idsFor = (entry: RenderEntry): string[] =>
      entry.kind === "movie" ? [entry.item.id] : entry.group.episodeIds;
    // A show group is hidden only when EVERY episode is hidden (hiddenFor),
    // and approved only when its representative episode is approved.
    const isHidden = (entry: RenderEntry): boolean => hiddenFor(idsFor(entry));
    const isApproved = (entry: RenderEntry): boolean =>
      entry.kind === "movie"
        ? approvedIds.has(entry.item.id)
        : !!entry.group.representativeId && approvedIds.has(entry.group.representativeId);

    if (viewFilter === "hidden") return all.filter(isHidden);
    // "All" is the whole catalog MINUS hidden items; hidden items are only
    // reachable through the Hidden view.
    if (viewFilter === "all") return all.filter((entry) => !isHidden(entry));
    return all.filter((entry) => !isHidden(entry) &&
      (viewFilter === "library" ? isApproved(entry) : !isApproved(entry)));
  }, [PAGING_ENABLED, pagedEntries, viewFilter, items, libraryMediaType, approvedIds, hiddenIds]);

  // A movie title with multiple loaded files gets one card per file. Each card
  // shows its own quality label; single-copy titles stay unlabelled.
  const movieCopyCount = useMemo(() => {
    const counts = new Map<string, number>();
    for (const entry of entries) {
      if (entry.kind !== "movie") continue;
      const key = movieCopyKey(entry.item);
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
    return counts;
  }, [entries]);

  // Filter options derive from what's actually rendered (entries), so a
  // Movie library never offers TV ratings, a Show library never offers
  // studios, and browse mode offers both sets.
  const ratingOptions = useMemo(() => {
    if (PAGING_ENABLED) {
      const present = new Set(facets?.ratings ?? []);
      const ordered = [...MOVIE_RATING_ORDER, ...TV_RATING_ORDER].filter((r) => present.has(r));
      const extras = [...present].filter((r) => !ordered.includes(r)).sort((a, b) => a.localeCompare(b));
      return [...ordered, ...extras].map((r) => ({ value: r, label: r }));
    }
    const present = new Set<string>();
    for (const entry of entries) {
      const rating = entry.kind === "movie" ? entry.item.rating : entry.group.rating;
      if (rating) present.add(rating);
    }
    const ordered = [...MOVIE_RATING_ORDER, ...TV_RATING_ORDER].filter((r) => present.has(r));
    const extras = [...present]
      .filter((r) => !ordered.includes(r))
      .sort((a, b) => a.localeCompare(b));
    return [...ordered, ...extras].map((r) => ({ value: r, label: r }));
  }, [PAGING_ENABLED, facets, entries]);

  const studioOptions = useMemo(() => {
    if (PAGING_ENABLED) {
      return [...(facets?.studios ?? [])].sort((a, b) => a.localeCompare(b)).map((s) => ({ value: s, label: s }));
    }
    const present = new Set<string>();
    for (const entry of entries) {
      if (entry.kind !== "movie") continue;
      for (const name of (entry.item.productionCompanies ?? "").split("|")) {
        if (name) present.add(name);
      }
    }
    return [...present].sort((a, b) => a.localeCompare(b)).map((s) => ({ value: s, label: s }));
  }, [PAGING_ENABLED, facets, entries]);

  const networkOptions = useMemo(() => {
    if (PAGING_ENABLED) {
      return [...(facets?.networks ?? [])].sort((a, b) => a.localeCompare(b)).map((n) => ({ value: n, label: n }));
    }
    const present = new Set<string>();
    for (const entry of entries) {
      if (entry.kind !== "show") continue;
      for (const name of (entry.group.networks ?? "").split("|")) {
        if (name) present.add(name);
      }
    }
    return [...present].sort((a, b) => a.localeCompare(b)).map((s) => ({ value: s, label: s }));
  }, [PAGING_ENABLED, facets, entries]);

  // Free-text search: lowercase tokens with implicit wildcards (substring
  // match), AND-ed together. Haystacks are title + director + cast for
  // movies and show title + creators + cast for shows -- overview is
  // deliberately excluded so plot summaries don't drown out title matches.
  // The haystack map is only built while a search is active.
  const searchTokens = useMemo(
    () => effectiveSearchQuery.toLowerCase().split(/\s+/).filter(Boolean),
    [effectiveSearchQuery]
  );
  const searchActive = searchTokens.length > 0;

  const searchHaystacks = useMemo(() => {
    if (!searchActive) return null;
    const map = new Map<RenderEntry, string>();
    for (const entry of entries) {
      const parts =
        entry.kind === "movie"
          ? [labelFor(entry.item), entry.item.director ?? "", entry.item.cast ?? ""]
          : [entry.group.title, entry.group.creators ?? "", entry.group.cast ?? ""];
      map.set(entry, parts.join(" ").toLowerCase());
    }
    return map;
  }, [entries, searchActive]);

  // Rating/studio/network options are media-type specific -- switching
  // libraries must not keep a stale selection that matches nothing.
  useEffect(() => {
    setRatingFilter(null);
    setStudioFilter(null);
    setNetworkFilter(null);
    setSearchQuery("");
    setOpenFilter(null);
    setViewFilter("all");
    if (PAGING_ENABLED) setLetterFilter(null);
    setSelectionMode(false);
    setSelectedKeys(new Set());
    setMarkAction("none");
    setHideAction("none");
  }, [libraryMediaType]);

  // Hidden state is per library, so the view filter must not carry over.
  useEffect(() => {
    setViewFilter("all");
    if (PAGING_ENABLED) setLetterFilter(null);
    setSelectionMode(false);
    setSelectedKeys(new Set());
    setMarkAction("none");
    setHideAction("none");
  }, [libraryId]);

  // Preserve optimistic icon changes without moving a card in the paged list.
  function patchPagedEntry(key: string, patch: Partial<LibraryEntry>) {
    if (!PAGING_ENABLED) return;
    setPagedEntries((prev) => prev.map((entry) =>
      libraryEntryKey(entry) === `movie:${key}` || libraryEntryKey(entry) === `show:${key}`
        ? { ...entry, ...patch }
        : entry));
  }

  async function handleApprove(item: SourceItem): Promise<boolean> {
    if (!libraryId) return false;
    setApprovingKey(item.id);
    try {
      await api.approve(libraryId, item.id);
      const ids = await api.getApprovedItemIds(libraryId);
      setApprovedIds(new Set(ids));
      patchPagedEntry(item.id, { approved: true, approvedAt: new Date().toISOString() });
      showToast(`"${item.mediaType === "Movie" ? (item.title ?? "Untitled") : (item.showTitle ?? "Untitled")}" added to library`, "success");
      return true;
    } catch (e) {
      showToast((e as Error).message, "error");
      return false;
    } finally {
      setApprovingKey(null);
    }
  }

  // No per-show endpoint: a single episode row per show group, so the
  // refresh spinner just re-fetches the catalog for feedback.
  async function handleRefreshCard(groupKey: string) {
    setRefreshingKey(groupKey);
    try {
      if (PAGING_ENABLED) {
        await Promise.all([fetchPage(0, true), loadFacets()]);
      } else {
        applySourceRows(await api.listSourceItems(libraryId));
      }
      showToast("Catalog refreshed", "success");
    } catch (e) {
      showToast((e as Error).message, "error");
    } finally {
      setRefreshingKey(null);
    }
  }

  // Called by MatchModal after a successful apply: refetch page/facets so the
  // title leaves the Unmatched view, then report the result once.
  async function handleMatchApplied(title: string) {
    if (PAGING_ENABLED) {
      await Promise.all([fetchPage(0, true), loadFacets()]);
    } else {
      applySourceRows(await api.listSourceItems(libraryId));
    }
    showToast(`Matched “${title}” — metadata will be refreshed from TMDB.`, "success");
  }

  async function handleUnapprove(item: SourceItem): Promise<boolean> {
    if (!libraryId) return false;
    setApprovingKey(item.id);
    try {
      await api.unapprove(libraryId, item.id);
      const ids = await api.getApprovedItemIds(libraryId);
      setApprovedIds(new Set(ids));
      patchPagedEntry(item.id, { approved: false, approvedAt: null });
      showToast(`"${item.mediaType === "Movie" ? (item.title ?? "Untitled") : (item.showTitle ?? "Untitled")}" removed from library`, "info");
      return true;
    } catch (e) {
      showToast((e as Error).message, "error");
      return false;
    } finally {
      setApprovingKey(null);
    }
  }

  async function handleApproveShow(group: ShowGroup): Promise<boolean> {
    if (!libraryId) return false;
    const repId = representativeId(group);
    if (!repId) return false;

    setApprovingKey(group.key);
    try {
      await api.approve(libraryId, repId);
      const ids = await api.getApprovedItemIds(libraryId);
      setApprovedIds(new Set(ids));
      patchPagedEntry(group.key, { approved: true, approvedAt: new Date().toISOString() });
      showToast(
        `"${group.title}" added to library`,
        "success"
      );
      return true;
    } catch (e) {
      showToast((e as Error).message, "error");
      return false;
    } finally {
      setApprovingKey(null);
    }
  }

  async function handleUnapproveShow(group: ShowGroup): Promise<boolean> {
    if (!libraryId) return false;
    const repId = representativeId(group);
    if (!repId) return false;

    setApprovingKey(group.key);
    try {
      await api.unapprove(libraryId, repId);
      const ids = await api.getApprovedItemIds(libraryId);
      setApprovedIds(new Set(ids));
      patchPagedEntry(group.key, { approved: false, approvedAt: null });
      showToast(`"${group.title}" removed from library`, "info");
      return true;
    } catch (e) {
      showToast((e as Error).message, "error");
      return false;
    } finally {
      setApprovingKey(null);
    }
  }

  // Hide/unhide are requests, not immediate state changes (Bug #4). The card
  // keeps its current position; only the eye turns yellow. Sync applies the
  // request (symlink removed/restored) and moves the card on the next refresh.
  async function changeHidden(key: string, ids: string[], hide: boolean): Promise<boolean> {
    if (!libraryId || ids.length === 0) return false;
    const previousHidden = hiddenFor(ids);
    const previousStamp = hiddenAtFor(ids);
    const stamp = new Date().toISOString();

    // A pending hide stays visible (hidden=false); a pending unhide stays in
    // the Hidden view (hidden=true). In both cases HiddenAt becomes the new
    // request stamp so the eye turns yellow until Sync.
    const patchedHidden = hide ? false : true;
    if (PAGING_ENABLED) {
      patchPagedEntry(key, { hidden: patchedHidden, hiddenAt: stamp, unhideRequestedAt: hide ? null : stamp });
    } else {
      setItems((prev) => prev.map((item) => ids.includes(item.id)
        ? {
            ...item,
            hidden: patchedHidden,
            hiddenAt: stamp,
            unhideRequestedAt: hide ? null : stamp,
          }
        : item));
    }
    setHidingKey(key);
    try {
      if (hide) await api.hideItems(libraryId, ids);
      else await api.unhideItems(libraryId, ids);
      showToast(
        hide
          ? `${ids.length === 1 ? "Hide" : `${ids.length} hides`} requested — press Sync to apply`
          : `${ids.length === 1 ? "Unhide" : `${ids.length} unhides`} requested — press Sync to restore`,
        "info"
      );
      return true;
    } catch (e) {
      if (PAGING_ENABLED) {
        patchPagedEntry(key, {
          hidden: previousHidden,
          hiddenAt: previousStamp,
          unhideRequestedAt: null,
        });
      } else {
        setItems((prev) => prev.map((item) => ids.includes(item.id)
          ? { ...item, hidden: previousHidden, hiddenAt: previousStamp, unhideRequestedAt: null }
          : item));
      }
      showToast((e as Error).message, "error");
      return false;
    } finally {
      setHidingKey(null);
    }
  }

  // A confirmed Library ↔ Hidden switch must apply BOTH changes. If the first
  // call fails its helper already toasted the error and we skip the second, so
  // a partial switch is never reported as success.
  async function switchToHidden(unapprove: () => Promise<boolean>, key: string, ids: string[]) {
    if (await unapprove()) await changeHidden(key, ids, true);
  }

  async function switchToLibrary(unhide: () => Promise<boolean>, approve: () => Promise<boolean>) {
    if (await unhide()) await approve();
  }

  const itemCount = PAGING_ENABLED ? pagedEntries.length : items.length;
  const hasActiveFilters = viewFilter !== "all" || genreFilter !== null || ratingFilter !== null ||
    studioFilter !== null || networkFilter !== null || !!letterFilter || !!effectiveSearchQuery.trim();

  if (!PAGING_ENABLED && loading) {
    return (
      <div style={{ display: "flex", justifyContent: "center", padding: "2rem 0" }}>
        <span className="loading-indicator" role="status" aria-live="polite">Loading library…</span>
      </div>
    );
  }
  if (PAGING_ENABLED && loading && itemCount === 0) {
    return <InitialLoadingPlaceholder />;
  }
  if (error) {
    return (
      <p style={{ color: "var(--color-danger)", fontFamily: "var(--font-body)" }}>
        Couldn't load the library: {error}
      </p>
    );
  }
  // Zero-result states are rendered inside the main grid below the toolbar.
  // An early return here would unmount the filter bar, leaving no way to
  // switch back to All or clear a filter after an optimistic hide/unhide.

  const sorted = [...entries].sort((a, b) => titleForEntry(a).localeCompare(titleForEntry(b)));
  const groups = new Map<string, RenderEntry[]>();
  for (const entry of sorted) {
    const letter = letterFor(titleForEntry(entry));
    if (!groups.has(letter)) groups.set(letter, []);
    groups.get(letter)!.push(entry);
  }

  function jumpTo(letter: string) {
    if (PAGING_ENABLED) {
      // Paged grids have no #letter-M anchors; drive the server letter filter
      // instead so the rail still jumps to the right section.
      setLetterFilter((current) => (current === letter ? null : letter));
      return;
    }
    document.getElementById(`letter-${letter}`)?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  // Mirrors the server's DetailsBackfill.Pending predicate: only items with
  // a TmdbId can ever be backfilled -- unmatched files (scene rips, samples)
  // would otherwise count forever and trigger a phantom status on every
  // dropdown open (server reports pending=0 while local total stays >0).
  const needsDetails = (i: SourceItem) =>
    i.tmdbId != null &&
    (!i.genreIds || i.genreIds === "" ||
      i.rating == null ||
      i.cast == null ||
      (i.mediaType === "Movie"
        ? i.productionCompanies == null || i.director == null
        : i.networks == null || i.creators == null));

  // Fills in genre/rating/studio/network data on demand when a filter
  // dropdown is opened. Runs in the foreground for at most ~60s (batches
  // go through nginx, which times out longer requests); whatever is still
  // pending afterwards is picked up by the hourly DetailsBackfillService.
  //
  // Progress is reported via the inline status text after the dropdowns
  // (never the button labels), ending with "✓ Completed" for 10s.
  const showDetailsStatus = (text: string | null, autoHideMs?: number) => {
    if (detailsStatusTimerRef.current !== null) {
      clearTimeout(detailsStatusTimerRef.current);
      detailsStatusTimerRef.current = null;
    }
    setDetailsStatus(text);
    if (text !== null && autoHideMs) {
      detailsStatusTimerRef.current = window.setTimeout(() => {
        setDetailsStatus(null);
        detailsStatusTimerRef.current = null;
      }, autoHideMs);
    }
  };

  // Paged path: the backfill endpoint already reports its own pending count,
  // so no 82k-row listSourceItems() download is needed just to fill dropdowns.
  const ensureDetailsPaged = async () => {
    if (detailsRunningRef.current) return;
    detailsRunningRef.current = true;
    setDetailsBackfilling(true);
    const deadline = Date.now() + 60_000;
    const fmt = (n: number) => n.toLocaleString();
    let outcome: "none" | "done" | "cap" | "stuck" = "none";
    let total = 0;
    let lastCompleted = 0;
    try {
      while (true) {
        const resp = await api.backfillDetails();
        if (total === 0 && resp.pending <= 0) break; // nothing to do, no status
        if (total === 0) total = resp.pending;
        lastCompleted = Math.max(0, total - resp.pending);
        showDetailsStatus(`Loading... ${fmt(lastCompleted)} / ${fmt(total)}`);
        if (resp.pending <= 0) { outcome = "done"; break; }
        if (resp.updated <= 0) { outcome = "stuck"; break; }
        if (Date.now() >= deadline) { outcome = "cap"; break; }
        await new Promise((r) => setTimeout(r, 300));
      }
      if (outcome === "none") {
        // Nothing was backfilled, so the current page is still valid. A
        // refetch here would replace optimistic hide/unhide state with stale
        // server data for the active view (e.g. an empty Hidden view) and
        // could blank the grid while the user is only opening a dropdown.
        await loadFacets();
      } else {
        await Promise.all([fetchPage(0, true), loadFacets()]);
      }
      if (outcome === "done") showDetailsStatus("✓ Completed", 10_000);
      else if (outcome === "cap") showDetailsStatus(`${fmt(lastCompleted)} / ${fmt(total)} — finishing in background`, 10_000);
      else if (outcome === "stuck") showDetailsStatus(`${fmt(lastCompleted)} / ${fmt(total)} — no progress, will retry in background`, 10_000);
    } catch (err) {
      showDetailsStatus(null);
      showToast(`Failed to load filter details: ${(err as Error).message}`, "error");
    } finally {
      detailsRunningRef.current = false;
      setDetailsBackfilling(false);
    }
  };

  const ensureDetailsLegacy = async () => {
    if (detailsRunningRef.current) return;
    const total = items.filter(needsDetails).length;
    if (total === 0) return; // catalog fully backfilled -- no status shown

    detailsRunningRef.current = true;
    setDetailsBackfilling(true);
    const deadline = Date.now() + 60_000;
    const fmt = (n: number) => n.toLocaleString();
    let outcome: "done" | "cap" | "stuck" = "done";
    let lastCompleted = 0;
    try {
      while (true) {
        const resp = await api.backfillDetails();
        lastCompleted = Math.max(0, total - resp.pending);
        showDetailsStatus(`Loading... ${fmt(lastCompleted)} / ${fmt(total)}`);
        if (resp.pending <= 0) {
          outcome = "done";
          break;
        }
        if (resp.updated <= 0) {
          outcome = "stuck"; // no progress (TMDB trouble) -- hourly job retries
          break;
        }
        if (Date.now() >= deadline) {
          outcome = "cap"; // foreground cap -- hourly job finishes the rest
          break;
        }
        await new Promise((r) => setTimeout(r, 300));
        // Refresh mid-run so the open dropdown grows as data lands
        applySourceRows(await api.listSourceItems(libraryId));
      }
      applySourceRows(await api.listSourceItems(libraryId));
      if (outcome === "done") {
        showDetailsStatus("✓ Completed", 10_000);
      } else if (outcome === "cap") {
        showDetailsStatus(`${fmt(lastCompleted)} / ${fmt(total)} — finishing in background`, 10_000);
      } else {
        showDetailsStatus(`${fmt(lastCompleted)} / ${fmt(total)} — no progress, will retry in background`, 10_000);
      }
    } catch (err) {
      showDetailsStatus(null);
      showToast(`Failed to load filter details: ${(err as Error).message}`, "error");
    } finally {
      detailsRunningRef.current = false;
      setDetailsBackfilling(false);
    }
  };

  const ensureDetails = async () => {
    if (PAGING_ENABLED) await ensureDetailsPaged();
    else await ensureDetailsLegacy();
  };

  const toggleFilter = (id: string) => {
    const opening = openFilter !== id;
    setOpenFilter(opening ? id : null);
    if (opening) void ensureDetails(); // non-blocking: dropdown opens immediately
  };

  const anyFilterActive =
    viewFilter !== "all" || genreFilter !== null || ratingFilter !== null ||
    studioFilter !== null || networkFilter !== null || searchActive || !!letterFilter;

  // Shown in the collapsed toolbar so an active-but-hidden filter set is
  // still discoverable.
  const activeFilterCount =
    (viewFilter !== "all" ? 1 : 0) +
    (genreFilter !== null ? 1 : 0) +
    (ratingFilter !== null ? 1 : 0) +
    (studioFilter !== null ? 1 : 0) +
    (networkFilter !== null ? 1 : 0) +
    (searchActive ? 1 : 0) +
    (letterFilter ? 1 : 0);

  const entryMatchesFilters = (entry: RenderEntry): boolean => {
    if (searchActive && searchHaystacks) {
      const hay = searchHaystacks.get(entry) ?? "";
      for (const token of searchTokens) {
        if (!hay.includes(token)) return false;
      }
    }
    if (genreFilter !== null) {
      const genreIdsStr = entry.kind === "movie" ? entry.item.genreIds : entry.group.genreIds;
      if (!genreIdsStr || genreIdsStr === "[]" || genreIdsStr === "") return false;
      const ids = genreIdsStr.split(",").map((id) => parseInt(id, 10)).filter((id) => !isNaN(id));
      if (!ids.includes(genreFilter)) return false;
    }
    if (ratingFilter !== null) {
      const rating = entry.kind === "movie" ? entry.item.rating : entry.group.rating;
      if (rating !== ratingFilter) return false;
    }
    // A studio selection narrows the grid to movies (and vice versa for
    // networks), so browse mode shows "A24 movies" rather than mixing them
    // with every show in the catalog. Both set = OR across the two kinds.
    if (studioFilter !== null) {
      if (entry.kind !== "movie") return false;
      if (!(entry.item.productionCompanies ?? "").split("|").includes(studioFilter)) return false;
    }
    if (networkFilter !== null) {
      if (entry.kind !== "show") return false;
      if (!(entry.group.networks ?? "").split("|").includes(networkFilter)) return false;
    }
    return true;
  };

  const filteredGroups = new Map<string, RenderEntry[]>();
  for (const [letter, groupEntries] of groups.entries()) {
    const filtered = anyFilterActive ? groupEntries.filter(entryMatchesFilters) : groupEntries;
    if (filtered.length > 0) filteredGroups.set(letter, filtered);
  }

  const displayGroups = !PAGING_ENABLED && anyFilterActive ? filteredGroups : groups;
  const visibleEntries = [...displayGroups.values()].flat();

  async function applyBulk() {
    if (!libraryId) return;
    const selected = entries.filter((entry) => selectedKeys.has(renderEntryKey(entry)));
    if (selected.length === 0 || (markAction === "none" && hideAction === "none")) return;

    const markTargets = Array.from(new Set(selected
      .map((entry) => (entry.kind === "movie" ? entry.item.id : entry.group.representativeId))
      .filter((id): id is string => !!id)));
    const hideIds = Array.from(new Set(selected.flatMap((entry) =>
      entry.kind === "movie" ? [entry.item.id] : entry.group.episodeIds
    ).filter((id): id is string => !!id)));

    if (
      (markAction === "mark" && hideAction === "hide") ||
      (markAction === "unmark" && hideAction === "unhide")
    ) {
      const markLabel = markAction === "mark" ? "Mark for library" : "Unmark from library";
      const hideLabel = hideAction === "hide" ? "Hide from library" : "Unhide from library";
      const ok = window.confirm(
        `${markLabel} and ${hideLabel} for ${selected.length} selected ${selected.length === 1 ? "title" : "titles"}?\n\n` +
        "The library change still needs a Sync to take effect on disk and in Plex. Continue?"
      );
      if (!ok) return;
    }

    setBulkBusy(true);
    let markSucceeded = 0;
    let markFailed = 0;
    try {
      if (markAction !== "none") {
        const results = await Promise.allSettled(markTargets.map((id) =>
          markAction === "mark" ? api.approve(libraryId, id) : api.unapprove(libraryId, id)
        ));
        markSucceeded = results.filter((r) => r.status === "fulfilled").length;
        markFailed = results.length - markSucceeded;
      }

      let hideResult: { hidden?: number; removed?: number } | null = null;
      if (hideAction !== "none") {
        hideResult = hideAction === "hide"
          ? await api.hideItems(libraryId, hideIds)
          : await api.unhideItems(libraryId, hideIds);
      }

      if (PAGING_ENABLED) {
        await Promise.all([fetchPage(0, true), loadFacets()]);
      } else {
        applySourceRows(await api.listSourceItems(libraryId));
      }
      const ids = await api.getApprovedItemIds(libraryId);
      setApprovedIds(new Set(ids));

      const parts: string[] = [];
      if (markAction !== "none") {
        parts.push(
          `${markSucceeded} ${markAction === "mark" ? "added to" : "removed from"} library` +
          `${markFailed ? `, ${markFailed} failed` : ""}`
        );
      }
      if (hideAction !== "none") {
        const count = hideAction === "hide"
          ? hideResult?.hidden ?? hideIds.length
          : hideResult?.removed ?? hideIds.length;
        parts.push(
          `${count} ${hideAction === "hide" ? "hidden — press Sync to apply" : "unhidden — press Sync to restore"}`
        );
      }
      showToast(
        `${selected.length} selected: ${parts.join("; ")}.`,
        markFailed > 0 ? "warning" : "success"
      );
      setSelectionMode(false);
      setSelectedKeys(new Set());
      setMarkAction("none");
      setHideAction("none");
    } catch (e) {
      showToast((e as Error).message, "error");
    } finally {
      setBulkBusy(false);
    }
  }

  return (
    <div style={{ display: "flex", gap: "0.5rem", alignItems: "flex-start", marginTop: "-1.5rem" }}>
      <div style={{ flex: 1, minWidth: 0 }}>
        {/* Filter toolbar -- sticky so it stays on screen while browsing,
            collapsed via "Hide filters" / restored via "Show filters". */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "0.75rem",
            // The grid wrapper is pulled up into main's 24px padding band so
            // this sticky bar can reach the viewport top; padding keeps the
            // contents at their original visual position.
            paddingTop: "2.25rem",
            paddingBottom: "0.75rem",
            marginBottom: "0.5rem",
            borderBottom: "1px solid var(--border)",
            flexWrap: "wrap",
            position: "sticky",
            // -1.5rem cancels main's 24px padding so the bar sticks flush to
            // the viewport top instead of leaving a 24px strip where cards
            // would peek through.
            top: "-1.5rem",
            zIndex: 10,
            background: "var(--surface-page)",
          }}
        >
          {filtersHidden ? (
            <>
              <button
                onClick={() => setFiltersHidden(false)}
                aria-expanded={false}
                style={toolbarButtonStyle}
              >
                Show{activeFilterCount > 0 ? ` (${activeFilterCount} active)` : ""}
              </button>
              {detailsStatus && <span style={toolbarStatusStyle}>{detailsStatus}</span>}
            </>
          ) : (
            <>
              <span style={{ fontFamily: "var(--font-body)", fontSize: "13px", color: "var(--text-secondary)" }}>
                Filter:
              </span>
              <SelectFilter<string>
                allLabel={`All ${libraryMediaType === "Movie" ? "Movies" : libraryMediaType === "Show" ? "TV Shows" : libraryMediaType === "Anime" ? "Anime" : "Items"}`}
                value={viewFilter === "all" ? null : viewFilter}
                options={[
                  { value: "library", label: `Library ${libraryMediaType === "Movie" ? "Movies" : libraryMediaType === "Show" ? "TV Shows" : libraryMediaType === "Anime" ? "Anime" : "Items"}` },
                  { value: "unmarked", label: `Unmarked ${libraryMediaType === "Movie" ? "Movies" : libraryMediaType === "Show" ? "TV Shows" : libraryMediaType === "Anime" ? "Anime" : "Items"}` },
                  { value: "hidden", label: `Hidden ${libraryMediaType === "Movie" ? "Movies" : libraryMediaType === "Show" ? "TV Shows" : libraryMediaType === "Anime" ? "Anime" : "Items"}` },
                  ...(isAdmin
                    ? [{ value: "unmatched", label: `Unmatched ${libraryMediaType === "Movie" ? "Movies" : libraryMediaType === "Show" ? "TV Shows" : libraryMediaType === "Anime" ? "Anime" : "Items"}` }]
                    : []),
                ]}
                isOpen={openFilter === "view"}
                onToggle={() => toggleFilter("view")}
                onChange={(v) => { setViewFilter((v as ViewFilter) ?? "all"); setOpenFilter(null); }}
                backfilling={detailsBackfilling}
                emptyHint=""
              />
              <SelectFilter
                allLabel="All Genres"
                value={genreFilter}
                options={GENRE_FILTER_OPTIONS}
                isOpen={openFilter === "genre"}
                onToggle={() => toggleFilter("genre")}
                onChange={(v) => { setGenreFilter(v); setOpenFilter(null); }}
                backfilling={detailsBackfilling}
                emptyHint="Genre data not loaded yet"
              />
              <SelectFilter
                allLabel="All Ratings"
                value={ratingFilter}
                options={ratingOptions}
                isOpen={openFilter === "rating"}
                onToggle={() => toggleFilter("rating")}
                onChange={(v) => { setRatingFilter(v); setOpenFilter(null); }}
                backfilling={detailsBackfilling}
                emptyHint="Rating data not loaded yet"
              />
              {(libraryMediaType === null || libraryMediaType === undefined || libraryMediaType === "Movie") && (
                <SelectFilter
                  allLabel="All Studios"
                  value={studioFilter}
                  options={studioOptions}
                  isOpen={openFilter === "studio"}
                  onToggle={() => toggleFilter("studio")}
                  onChange={(v) => { setStudioFilter(v); setOpenFilter(null); }}
                  backfilling={detailsBackfilling}
                  emptyHint="Studio data not loaded yet"
                />
              )}
              {(libraryMediaType === null || libraryMediaType === undefined || libraryMediaType === "Show" || libraryMediaType === "Anime") && (
                <SelectFilter
                  allLabel="All Networks"
                  value={networkFilter}
                  options={networkOptions}
                  isOpen={openFilter === "network"}
                  onToggle={() => toggleFilter("network")}
                  onChange={(v) => { setNetworkFilter(v); setOpenFilter(null); }}
                  backfilling={detailsBackfilling}
                  emptyHint="Network data not loaded yet"
                />
              )}
              <input
                type="search"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                onFocus={() => void ensureDetails()} // cast/crew may still be loading
                placeholder="Search titles, cast, crew…"
                aria-label="Search titles, cast, crew"
                style={{
                  fontFamily: "var(--font-body)",
                  fontSize: "13px",
                  padding: "6px 10px",
                  borderRadius: "var(--radius)",
                  border: "1px solid var(--border)",
                  background: "var(--surface-card)",
                  color: "var(--text-primary)",
                  minWidth: "180px",
                  flex: "1 1 200px",
                  maxWidth: "320px",
                }}
              />
              {detailsStatus && (
                <span style={toolbarStatusStyle}>
                  {detailsStatus}
                </span>
              )}
              {PAGING_ENABLED && letterFilter && (
                <button onClick={() => setLetterFilter(null)} style={toolbarButtonStyle}>
                  Letter: {letterFilter} ✕
                </button>
              )}
              {anyFilterActive && (
                <button
                  onClick={() => {
                    setViewFilter("all");
                    setGenreFilter(null);
                    setRatingFilter(null);
                    setStudioFilter(null);
                    setNetworkFilter(null);
                    setSearchQuery("");
                    setLetterFilter(null);
                    setOpenFilter(null);
                  }}
                  style={toolbarButtonStyle}
                >
                  Clear filters
                </button>
              )}
              <button
                onClick={() => {
                  setFiltersHidden(true);
                  setOpenFilter(null);
                  setMultiselect(false);
                }}
                aria-expanded={true}
                style={{ ...toolbarButtonStyle, marginLeft: "auto" }}
              >
                Hide
              </button>
              {libraryId && (
                <MultiselectButton
                  active={selectionMode}
                  selectedCount={selectedKeys.size}
                  onToggle={() => setMultiselect(!selectionMode)}
                />
              )}
            </>
          )}
        </div>

        {selectionMode && libraryId && (
          <div
            style={{
              display: "flex",
              flexWrap: "wrap",
              alignItems: "center",
              gap: "8px",
              margin: "0 0 1rem",
              padding: "8px 10px",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--surface-card)",
              fontFamily: "var(--font-body)",
              fontSize: "12px",
              color: "var(--text-secondary)",
            }}
          >
            <strong style={{ color: "var(--text-primary)", marginRight: "4px" }}>
              {selectedKeys.size} selected
            </strong>
            <button
              type="button"
              onClick={() => setSelectedKeys(new Set(visibleEntries.map(renderEntryKey)))}
              disabled={bulkBusy}
              style={toolbarButtonStyle}
            >
              Select all
            </button>
            <button
              type="button"
              onClick={() => setSelectedKeys(new Set())}
              disabled={bulkBusy || selectedKeys.size === 0}
              style={toolbarButtonStyle}
            >
              Clear
            </button>
            <label style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}>
              Mark for library
              <select
                value={markAction}
                onChange={(e) => setMarkAction(e.target.value as MarkBulkAction)}
                disabled={bulkBusy}
                style={{ ...toolbarButtonStyle, background: "var(--surface-card)" }}
              >
                <option value="none">No change</option>
                <option value="mark">Mark</option>
                <option value="unmark">Unmark</option>
              </select>
            </label>
            <label style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}>
              Hide from Library
              <select
                value={hideAction}
                onChange={(e) => setHideAction(e.target.value as HideBulkAction)}
                disabled={bulkBusy}
                style={{ ...toolbarButtonStyle, background: "var(--surface-card)" }}
              >
                <option value="none">No change</option>
                <option value="hide">Hide</option>
                <option value="unhide">Unhide</option>
              </select>
            </label>
            <button
              type="button"
              onClick={() => setMultiselect(false)}
              disabled={bulkBusy}
              style={toolbarButtonStyle}
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={() => void applyBulk()}
              disabled={bulkBusy || selectedKeys.size === 0 || (markAction === "none" && hideAction === "none")}
              style={{
                ...toolbarButtonStyle,
                borderColor: "var(--color-primary, #3b82f6)",
                color: "var(--color-primary, #3b82f6)",
                fontWeight: 600,
                cursor:
                  bulkBusy || selectedKeys.size === 0 || (markAction === "none" && hideAction === "none")
                    ? "not-allowed"
                    : "pointer",
                opacity:
                  bulkBusy || selectedKeys.size === 0 || (markAction === "none" && hideAction === "none")
                    ? 0.5
                    : 1,
              }}
            >
              {bulkBusy ? "Applying…" : "Apply"}
            </button>
          </div>
        )}

        {displayGroups.size === 0 && !searchActive && (
          <p style={{ color: "var(--text-secondary)", fontFamily: "var(--font-body)", padding: "1rem 0" }}>
            {PAGING_ENABLED && hasActiveFilters
              ? `No titles match${effectiveSearchQuery.trim() ? ` “${effectiveSearchQuery.trim()}”` : ""} these filters.`
              : "No scanned items yet. Register a source root and run a scan."}
          </p>
        )}

        {ALL_LETTERS.filter((l) => displayGroups.has(l)).map((letter) => (
          <div key={letter} id={`letter-${letter}`} style={{ marginBottom: "1.5rem" }}>
            <h3
              style={{
                fontFamily: "var(--font-heading)",
                fontSize: "15px",
                color: "var(--text-secondary)",
                borderBottom: "1px solid var(--border)",
                paddingBottom: "4px",
                margin: "0 0 0.75rem",
              }}
            >
              {letter}
            </h3>
            <div
              style={{
                display: "grid",
                // minmax(0, 1fr) is critical: plain 1fr == minmax(auto, 1fr),
                // which lets a loaded poster's intrinsic width blow out the
                // rightmost track on mobile after a fast multi-page scroll.
                gridTemplateColumns: isMobile ? "repeat(3, minmax(0, 1fr))" : "repeat(auto-fill, minmax(150px, 1fr))",
                gap: "1rem",
              }}
            >
              {displayGroups.get(letter)!.map((entry) =>
                entry.kind === "movie" ? (
                  <LazyMount key={entry.item.id} reserveActions={!!libraryId}>
                    <PosterCard
                      item={entry.item}
                      canApprove={!!libraryId}
                      approving={approvingKey === entry.item.id}
                      onApprove={() => handleApprove(entry.item)}
                      onUnapprove={() => handleUnapprove(entry.item)}
                      markState={markStateFor([entry.item.id])}
                      hiddenState={hiddenStateFor([entry.item.id])}
                      hiding={hidingKey === entry.item.id}
                      inHiddenView={viewFilter === "hidden"}
                      onHide={() => changeHidden(entry.item.id, [entry.item.id], true)}
                      onUnhide={() => changeHidden(entry.item.id, [entry.item.id], false)}
                      onHideAndUnapprove={() => {
                        void switchToHidden(() => handleUnapprove(entry.item), entry.item.id, [entry.item.id]);
                      }}
                      onApproveAndUnhide={() => {
                        void switchToLibrary(
                          () => changeHidden(entry.item.id, [entry.item.id], false),
                          () => handleApprove(entry.item)
                        );
                      }}
                      onOpenDetails={() => setSelected({ kind: "movie", item: entry.item })}
                      showMatch={isAdmin && viewFilter === "unmatched"}
                      onMatch={() => setMatchTarget({ kind: "movie", item: entry.item })}
                      selectionMode={selectionMode}
                      selected={selectedKeys.has(renderEntryKey(entry))}
                      onToggleSelect={() => toggleSelect(renderEntryKey(entry))}
                      qualitySuffixText={
                        (movieCopyCount.get(movieCopyKey(entry.item)) ?? 0) > 1
                          ? qualityLabel(entry.item.relativePath)
                          : ""
                      }
                    />
                  </LazyMount>
                ) : (
                  <LazyMount key={entry.group.key} reserveActions={!!libraryId}>
                    <ShowCard
                      group={entry.group}
                      canApprove={!!libraryId}
                      approving={approvingKey === entry.group.key}
                      refreshing={refreshingKey === entry.group.key}
                      onApprove={() => handleApproveShow(entry.group)}
                      onUnapprove={() => handleUnapproveShow(entry.group)}
                      markState={markStateFor([entry.group.representativeId, ...entry.group.episodeIds])}
                      hiddenState={hiddenStateFor(entry.group.episodeIds)}
                      hiding={hidingKey === entry.group.key}
                      inHiddenView={viewFilter === "hidden"}
                      onHide={() => changeHidden(entry.group.key, entry.group.episodeIds, true)}
                      onUnhide={() => changeHidden(entry.group.key, entry.group.episodeIds, false)}
                      onHideAndUnapprove={() => {
                        void switchToHidden(
                          () => handleUnapproveShow(entry.group),
                          entry.group.key,
                          entry.group.episodeIds
                        );
                      }}
                      onApproveAndUnhide={() => {
                        void switchToLibrary(
                          () => changeHidden(entry.group.key, entry.group.episodeIds, false),
                          () => handleApproveShow(entry.group)
                        );
                      }}
                      onRefresh={() => handleRefreshCard(entry.group.key)}
                      showMatch={isAdmin && viewFilter === "unmatched"}
                      onMatch={() => setMatchTarget({ kind: "show", group: entry.group })}
                      onOpenDetails={() => setSelected({ kind: "show", group: entry.group })}
                      selectionMode={selectionMode}
                      selected={selectedKeys.has(renderEntryKey(entry))}
                      onToggleSelect={() => toggleSelect(renderEntryKey(entry))}
                    />
                  </LazyMount>
                )
              )}
            </div>
          </div>
        ))}

        {searchActive && displayGroups.size === 0 && (
          <p style={{ color: "var(--text-secondary)", fontFamily: "var(--font-body)", padding: "1rem 0" }}>
            No titles match “{effectiveSearchQuery.trim()}”. Try fewer or different words.
          </p>
        )}

        {PAGING_ENABLED && (
          <div ref={sentinelRef} style={{ minHeight: "36px", padding: "0.75rem 0", textAlign: "center" }}>
            {loadingMore ? (
              <span className="loading-indicator" role="status" aria-live="polite">Loading more…</span>
            ) : hasMore ? (
              <span style={toolbarStatusStyle}>Scroll for more ({pagedTotal} total)</span>
            ) : null}
          </div>
        )}

      </div>

      <div
        style={{
          position: "sticky",
          top: 0,
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          fontSize: "10px",
          fontFamily: "var(--font-body)",
          flexShrink: 0,
          width: "18px",
          gap: "1px",
        }}
      >
        {ALL_LETTERS.map((letter) => (
          <button
            key={letter}
            onClick={() => jumpTo(letter)}
            disabled={!PAGING_ENABLED && !displayGroups.has(letter)}
            style={{
              border: "none",
              background: "none",
              padding: "1px 0",
              cursor: PAGING_ENABLED || displayGroups.has(letter) ? "pointer" : "default",
              color: displayGroups.has(letter) || letterFilter === letter ? "var(--color-primary)" : "var(--text-muted)",
              opacity: displayGroups.has(letter) || letterFilter === letter ? 1 : 0.4,
              fontWeight: displayGroups.has(letter) || letterFilter === letter ? 600 : 400,
            }}
          >
            {letter}
          </button>
        ))}
      </div>

      {selected?.kind === "movie" && (
        <DetailModal item={selected.item} onClose={() => setSelected(null)} />
      )}
      {selected?.kind === "show" && selected.group.episodes.length > 0 && (
        <DetailModal
          item={
            selected.group.episodes.find(
              (episode) => episode.id === selected.group.representativeId
            ) ?? selected.group.episodes[0]
          }
          summary={selected.group}
          onClose={() => setSelected(null)}
        />
      )}
      {matchTarget && (
        <MatchModal
          target={matchTarget}
          onClose={() => setMatchTarget(null)}
          onApplied={handleMatchApplied}
        />
      )}
    </div>
  );
}

function PosterCard({
  item,
  canApprove,
  approving,
  onApprove,
  onUnapprove,
  markState,
  hiddenState,
  hiding,
  inHiddenView,
  onHide,
  onUnhide,
  onHideAndUnapprove,
  onApproveAndUnhide,
  onOpenDetails,
  showMatch,
  onMatch,
  selectionMode,
  selected,
  onToggleSelect,
  qualitySuffixText,
}: {
  item: SourceItem;
  canApprove: boolean;
  approving: boolean;
  onApprove: () => void;
  onUnapprove: () => void;
  markState: MarkState;
  hiddenState: HiddenState;
  hiding: boolean;
  inHiddenView: boolean;
  onHide: () => void;
  onUnhide: () => void;
  onHideAndUnapprove: () => void;
  onApproveAndUnhide: () => void;
  onOpenDetails: () => void;
  showMatch: boolean;
  onMatch: () => void;
  selectionMode: boolean;
  selected: boolean;
  onToggleSelect: () => void;
  qualitySuffixText: string;
}) {
  const label =
    (item.mediaType === "Movie"
      ? `${item.title ?? "Untitled"}${item.year ? ` (${item.year})` : ""}`
      : `${item.showTitle ?? "Untitled"} S${String(item.seasonNumber ?? 0).padStart(2, "0")}E${String(
          item.episodeNumber ?? 0
        ).padStart(2, "0")}`) + qualitySuffixText;

  return (
    <div
      style={{
        border: selected
          ? "2px solid var(--color-primary, #3b82f6)"
          : `2px solid ${markState === "synced" ? "var(--color-success)" : markState === "pending" ? PENDING_YELLOW : "var(--border-strong)"}`,
        borderRadius: "var(--radius-card)",
        overflow: "hidden",
        background: "var(--surface-card)",
        display: "flex",
        flexDirection: "column",
        position: "relative",
        minWidth: 0,
        width: "100%",
        height: "100%",
        boxShadow: selected ? "0 0 0 2px var(--color-primary, #3b82f6)" : undefined,
      }}
    >
      {selectionMode && <SelectionCheckbox selected={selected} onToggle={onToggleSelect} />}
      {showMatch && (
        <MatchIconButton
          onClick={onMatch}
          style={{ top: selectionMode ? "34px" : "6px" }}
        />
      )}
      <div
        onClick={selectionMode ? onToggleSelect : onOpenDetails}
        style={{
          aspectRatio: "2 / 3",
          background: item.posterUrl ? `var(--surface-raised)` : "var(--sage-100, #E4ECDA)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          cursor: "pointer",
        }}
      >
        {item.posterUrl ? (
          <img
            src={posterThumb(item.posterUrl)}
            alt={label}
            loading="lazy"
            decoding="async"
            draggable={false}
            style={{ width: "100%", height: "100%", objectFit: "cover" }}
          />
        ) : (
          <span style={{ fontSize: "12px", color: "var(--text-muted)", fontFamily: "var(--font-body)" }}>
            No poster
          </span>
        )}
      </div>
      <div style={{ padding: "8px 10px", display: "flex", flexDirection: "column", gap: "4px", flex: 1 }}>
        <p
          style={{
            fontFamily: "var(--font-heading)",
            fontSize: "14px",
            fontWeight: 600,
            margin: 0,
            color: "var(--text-primary)",
            lineHeight: 1.3,
          }}
        >
          {label}
        </p>
        {canApprove && (
          <CardActions
            markState={markState}
            approving={approving}
            hiddenState={hiddenState}
            hiding={hiding}
            inHiddenView={inHiddenView}
            onApprove={onApprove}
            onUnapprove={onUnapprove}
            onHide={onHide}
            onUnhide={onUnhide}
            onHideAndUnapprove={onHideAndUnapprove}
            onApproveAndUnhide={onApproveAndUnhide}
          />
        )}
      </div>
    </div>
  );
}

function ShowCard({
  group,
  canApprove,
  approving,
  refreshing,
  onApprove,
  onUnapprove,
  markState,
  hiddenState,
  hiding,
  inHiddenView,
  onHide,
  onUnhide,
  onHideAndUnapprove,
  onApproveAndUnhide,
  onRefresh,
  showMatch,
  onMatch,
  onOpenDetails,
  selectionMode,
  selected,
  onToggleSelect,
}: {
  group: ShowGroup;
  canApprove: boolean;
  approving: boolean;
  refreshing: boolean;
  onApprove: () => void;
  onUnapprove: () => void;
  markState: MarkState;
  hiddenState: HiddenState;
  hiding: boolean;
  inHiddenView: boolean;
  onHide: () => void;
  onUnhide: () => void;
  onHideAndUnapprove: () => void;
  onApproveAndUnhide: () => void;
  onRefresh: () => void;
  showMatch: boolean;
  onMatch: () => void;
  onOpenDetails: () => void;
  selectionMode: boolean;
  selected: boolean;
  onToggleSelect: () => void;
}) {
  const latest =
    group.latestSeason != null && group.latestEpisode != null
      ? `S${String(group.latestSeason).padStart(2, "0")}E${String(group.latestEpisode).padStart(2, "0")}`
      : null;

  const metaStyle: React.CSSProperties = {
    fontFamily: "var(--font-body)",
    fontSize: "11px",
    margin: 0,
    color: "var(--text-secondary)",
    lineHeight: 1.35,
  };

  return (
    <div
      style={{
        border: selected
          ? "2px solid var(--color-primary, #3b82f6)"
          : `2px solid ${markState === "synced" ? "var(--color-success)" : markState === "pending" ? PENDING_YELLOW : "var(--border-strong)"}`,
        borderRadius: "var(--radius-card)",
        overflow: "hidden",
        background: "var(--surface-card)",
        display: "flex",
        flexDirection: "column",
        position: "relative",
        minWidth: 0,
        width: "100%",
        height: "100%",
        boxShadow: selected ? "0 0 0 2px var(--color-primary, #3b82f6)" : undefined,
      }}
    >
      {selectionMode && <SelectionCheckbox selected={selected} onToggle={onToggleSelect} />}
      <button
        onClick={(e) => {
          e.stopPropagation();
          onRefresh();
        }}
        disabled={refreshing}
        title="Refresh catalog"
        aria-label="Refresh catalog"
        style={{
          position: "absolute",
          top: "6px",
          left: "6px",
          zIndex: 2,
          width: "22px",
          height: "22px",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          borderRadius: "999px",
          border: "1px solid var(--border-strong)",
          background: "var(--surface-card)",
          color: "var(--text-primary)",
          fontFamily: "var(--font-body)",
          fontSize: "13px",
          lineHeight: 1,
          cursor: refreshing ? "default" : "pointer",
          opacity: refreshing ? 0.6 : 1,
        }}
      >
        {refreshing ? "…" : "↻"}
      </button>

      {showMatch && (
        <MatchIconButton
          onClick={onMatch}
          style={{ top: selectionMode ? "34px" : "6px" }}
        />
      )}

      <div
        onClick={selectionMode ? onToggleSelect : onOpenDetails}
        style={{
          aspectRatio: "2 / 3",
          background: group.posterUrl ? `var(--surface-raised)` : "var(--sage-100, #E4ECDA)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          cursor: "pointer",
        }}
      >
        {group.posterUrl ? (
          <img
            src={posterThumb(group.posterUrl)}
            alt={group.title}
            loading="lazy"
            decoding="async"
            draggable={false}
            style={{ width: "100%", height: "100%", objectFit: "cover" }}
          />
        ) : (
          <span style={{ fontSize: "12px", color: "var(--text-muted)", fontFamily: "var(--font-body)" }}>
            No poster
          </span>
        )}
      </div>
      <div style={{ padding: "8px 10px", display: "flex", flexDirection: "column", gap: "4px", flex: 1 }}>
        <p
          style={{
            fontFamily: "var(--font-heading)",
            fontSize: "14px",
            fontWeight: 600,
            margin: 0,
            color: "var(--text-primary)",
            lineHeight: 1.3,
          }}
        >
          {group.title}{qualitySuffix(group.episodes.map((episode) => episode.relativePath))}
        </p>
        <p style={metaStyle}>
          {group.episodeCount} {group.episodeCount === 1 ? "episode" : "episodes"}
          {group.seasonCount > 0
            ? ` · ${group.seasonCount} ${group.seasonCount === 1 ? "season" : "seasons"}`
            : ""}
        </p>
        {latest && <p style={metaStyle}>Latest: {latest}</p>}
        {canApprove && (
          <CardActions
            markState={markState}
            approving={approving}
            hiddenState={hiddenState}
            hiding={hiding}
            inHiddenView={inHiddenView}
            onApprove={onApprove}
            onUnapprove={onUnapprove}
            onHide={onHide}
            onUnhide={onUnhide}
            onHideAndUnapprove={onHideAndUnapprove}
            onApproveAndUnhide={onApproveAndUnhide}
          />
        )}
      </div>
    </div>
  );
}
