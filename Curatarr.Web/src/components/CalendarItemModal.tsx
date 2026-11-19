import { useEffect, useState, type CSSProperties } from "react";
import {
  api,
  type CalendarEvent,
  type MembershipRole,
  type VirtualLibrary,
} from "../api/client";
import { useToast } from "../context/ToastContext";

interface CalendarItemModalProps {
  event: CalendarEvent;
  libraries: (VirtualLibrary & { myRole?: MembershipRole })[];
  isAdmin: boolean;
  onClose: () => void;
  onChanged: () => void;
}

type MarkState = "unmarked" | "pending" | "synced";
type HiddenState = "visible" | "pending" | "synced";

const PENDING_YELLOW = "#d4a017";

// Same wording as PosterGrid.CardActions so the Calendar behaves identically.
function markLabel(state: MarkState): string {
  if (state === "synced") return "In this library — click to unmark";
  if (state === "pending") return "Marked — click Sync to add to the library; click to unmark";
  return "Mark for library";
}

function hideLabel(state: HiddenState, inHiddenView: boolean): string {
  if (state === "pending" && !inHiddenView) return "Hide requested — click Sync to apply; click to cancel";
  if (state === "synced") return "Hidden and synced — click to unhide";
  if (state === "pending") return "Unhide requested — still hidden until Sync; click to cancel";
  return "Visible in this library — click to hide";
}

function stateColor(state: MarkState | HiddenState): string {
  if (state === "synced") return "var(--color-success)";
  if (state === "pending") return PENDING_YELLOW;
  return "var(--text-muted)";
}

const overlayStyle: CSSProperties = {
  position: "fixed",
  inset: 0,
  zIndex: 1000,
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
  padding: "1rem",
  background: "rgba(0,0,0,0.55)",
};

const cardStyle: CSSProperties = {
  width: "min(560px, 100%)",
  maxHeight: "85vh",
  overflowY: "auto",
  background: "var(--surface-card)",
  color: "var(--text-primary)",
  border: "1px solid var(--border)",
  borderRadius: "var(--radius-card)",
  padding: "1.25rem",
  fontFamily: "var(--font-body)",
};

const modalInputStyle: CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: 13,
  minHeight: 44,
  padding: "6px 8px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--border)",
  background: "var(--surface-page)",
  color: "var(--text-primary)",
};

const closeButtonStyle: CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: 18,
  minWidth: 44,
  minHeight: 44,
  border: "1px solid var(--border)",
  borderRadius: "var(--radius)",
  background: "var(--surface-page)",
  color: "var(--text-primary)",
  cursor: "pointer",
  touchAction: "manipulation",
};

export default function CalendarItemModal({
  event,
  libraries,
  isAdmin,
  onClose,
  onChanged,
}: CalendarItemModalProps) {
  const { showToast } = useToast();
  const [selectedLibraryId, setSelectedLibraryId] = useState("");
  const [approvedIds, setApprovedIds] = useState<Set<string>>(new Set());
  const [hiddenIds, setHiddenIds] = useState<Set<string>>(new Set());
  const [loadingState, setLoadingState] = useState(false);
  const [busy, setBusy] = useState(false);

  const writableLibraries = isAdmin
    ? libraries
    : libraries.filter((l) => l.myRole === "Owner" || l.myRole === "Curator");

  const markTarget = event.representativeSourceItemId ?? event.sourceItemIds[0] ?? null;
  const hideTargets = event.sourceItemIds;
  const librarySelected =
    selectedLibraryId !== "" && writableLibraries.some((l) => l.id === selectedLibraryId);
  const canAct =
    librarySelected && !loadingState && !busy && markTarget !== null && hideTargets.length > 0;

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  async function loadState(libraryId: string) {
    setLoadingState(true);
    try {
      const [approved, hidden] = await Promise.all([
        api.getApprovedItemIds(libraryId),
        api.listHiddenItemIds(libraryId),
      ]);
      setApprovedIds(new Set(approved));
      setHiddenIds(new Set(hidden));
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), "error");
      setApprovedIds(new Set());
      setHiddenIds(new Set());
    } finally {
      setLoadingState(false);
    }
  }

  function handleLibraryChange(value: string) {
    setSelectedLibraryId(value);
    if (!value) {
      setApprovedIds(new Set());
      setHiddenIds(new Set());
      return;
    }
    void loadState(value);
  }

  // The per-library id endpoints do not expose timestamps, so presence is
  // treated as pending: the conservative wording that always mentions Sync.
  const markState: MarkState = markTarget && approvedIds.has(markTarget) ? "pending" : "unmarked";
  const anyHidden = hideTargets.some((id) => hiddenIds.has(id));
  const hiddenState: HiddenState = anyHidden ? "pending" : "visible";

  async function refreshAfterChange() {
    if (selectedLibraryId) await loadState(selectedLibraryId);
    onChanged();
  }

  async function handleMarkClick() {
    if (!canAct || !markTarget) return;
    setBusy(true);
    try {
      if (markState !== "unmarked") {
        await api.unapprove(selectedLibraryId, markTarget);
        showToast(`"${event.title}" removed from library`, "info");
      } else if (anyHidden) {
        if (
          !window.confirm(
            "Choosing Library removes this title from Hidden.\n\nUnhiding and the library change still need a Sync to take effect on disk and in Plex. Continue?"
          )
        )
          return;
        await api.approve(selectedLibraryId, markTarget);
        await api.unhideItems(selectedLibraryId, hideTargets);
        showToast(`"${event.title}" added to library`, "success");
      } else {
        await api.approve(selectedLibraryId, markTarget);
        showToast(`"${event.title}" added to library`, "success");
      }
      await refreshAfterChange();
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), "error");
    } finally {
      setBusy(false);
    }
  }

  async function handleHideClick() {
    if (!canAct || hideTargets.length === 0) return;
    setBusy(true);
    const n = hideTargets.length;
    try {
      if (anyHidden) {
        await api.unhideItems(selectedLibraryId, hideTargets);
        showToast(`${n === 1 ? "Unhide" : `${n} unhides`} requested — press Sync to restore`, "info");
      } else if (markState !== "unmarked" && markTarget) {
        if (
          !window.confirm(
            "Choosing Hidden removes this title from the Library.\n\nThe removal still needs a Sync to take effect on disk and in Plex. Continue?"
          )
        )
          return;
        await api.unapprove(selectedLibraryId, markTarget);
        await api.hideItems(selectedLibraryId, hideTargets);
        showToast(`${n === 1 ? "Hide" : `${n} hides`} requested — press Sync to apply`, "info");
      } else {
        await api.hideItems(selectedLibraryId, hideTargets);
        showToast(`${n === 1 ? "Hide" : `${n} hides`} requested — press Sync to apply`, "info");
      }
      await refreshAfterChange();
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), "error");
    } finally {
      setBusy(false);
    }
  }

  const markActionStyle: CSSProperties = {
    fontFamily: "var(--font-body)",
    fontSize: 13,
    minHeight: 44,
    padding: "10px 12px",
    borderRadius: "var(--radius)",
    border: `1px solid ${stateColor(markState)}`,
    background: "var(--surface-page)",
    color: stateColor(markState),
    cursor: canAct ? "pointer" : "not-allowed",
    opacity: canAct ? 1 : 0.5,
    touchAction: "manipulation",
  };

  const hideActionStyle: CSSProperties = {
    ...markActionStyle,
    border: `1px solid ${stateColor(hiddenState)}`,
    color: stateColor(hiddenState),
  };

  return (
    <div
      role="presentation"
      style={overlayStyle}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div role="dialog" aria-modal="true" aria-label={event.title} style={cardStyle}>
        <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 12 }}>
          <h3 style={{ fontFamily: "var(--font-heading)", fontSize: 18, margin: 0 }}>{event.title}</h3>
          <button type="button" aria-label="Close" style={closeButtonStyle} onClick={onClose}>
            ×
          </button>
        </div>

        <div style={{ display: "flex", gap: 12, marginTop: 12, alignItems: "flex-start" }}>
          {event.posterUrl && (
            <img
              src={event.posterUrl}
              alt=""
              style={{ width: 56, height: 84, objectFit: "cover", borderRadius: "var(--radius)" }}
            />
          )}
          <div style={{ minWidth: 0 }}>
            {event.showTitle && (
              <div style={{ fontSize: 13, color: "var(--text-secondary)" }}>{event.showTitle}</div>
            )}
            <div style={{ fontSize: 13, color: "var(--text-muted)", marginTop: 4 }}>{event.date}</div>
            {event.isSeasonPremiere && (
              <div style={{ fontSize: 12, color: "var(--color-primary)", marginTop: 4 }}>
                New Season ({event.seasonNumber ?? "?"})
              </div>
            )}
          </div>
        </div>

        {writableLibraries.length === 0 ? (
          <p style={{ fontSize: 13, color: "var(--text-muted)", marginTop: 16 }}>
            You do not have a library you can change. Ask an administrator for access.
          </p>
        ) : (
          <>
            <label
              htmlFor="calendar-item-library"
              style={{ display: "block", fontSize: 13, color: "var(--text-secondary)", marginTop: 16 }}
            >
              Library
            </label>
            <select
              id="calendar-item-library"
              aria-label="Library"
              style={{ ...modalInputStyle, width: "100%", marginTop: 4 }}
              value={selectedLibraryId}
              onChange={(e) => handleLibraryChange(e.target.value)}
            >
              <option value="">Choose a library…</option>
              {writableLibraries.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                </option>
              ))}
            </select>

            <div style={{ display: "flex", flexWrap: "wrap", gap: 8, marginTop: 14 }}>
              <button
                type="button"
                style={markActionStyle}
                disabled={!canAct}
                onClick={() => void handleMarkClick()}
              >
                {markLabel(markState)}
              </button>
              <button
                type="button"
                style={hideActionStyle}
                disabled={!canAct}
                onClick={() => void handleHideClick()}
              >
                {hideLabel(hiddenState, false)}
              </button>
            </div>
            <p style={{ fontSize: 12, color: "var(--text-muted)", marginTop: 10 }}>
              Changes still need a Sync to take effect on disk and in Plex.
            </p>
          </>
        )}
      </div>
    </div>
  );
}
