import { useEffect, useMemo, useState, type CSSProperties } from "react";
import {
  api,
  type CalendarEvent,
  type CalendarEventsResponse,
  type MediaKind,
  type MembershipRole,
  type VirtualLibrary,
} from "../api/client";
import CalendarItemModal from "./CalendarItemModal";

type MediaFilter = MediaKind | "All";

interface CalendarPageProps {
  libraries: (VirtualLibrary & { myRole?: MembershipRole })[];
  selectedLibraryId: string | null;
  onSelectLibrary: (id: string | null) => void;
  libraryMediaType: MediaKind | null;
  refreshKey: number;
  isAdmin: boolean;
}

const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

const controlStyle: CSSProperties = {
  minHeight: "44px",
  padding: "8px 10px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--border)",
  background: "var(--surface-card)",
  color: "var(--text-primary)",
  fontFamily: "var(--font-body)",
  fontSize: "14px",
};

const iconButtonStyle: CSSProperties = {
  ...controlStyle,
  width: "44px",
  padding: 0,
  display: "inline-flex",
  alignItems: "center",
  justifyContent: "center",
  cursor: "pointer",
  fontSize: "20px",
};

function parseMonth(value: string): { year: number; month: number } | null {
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  return month >= 1 && month <= 12 ? { year, month } : null;
}

function shiftMonth(value: string, delta: number): string {
  const parsed = parseMonth(value);
  if (!parsed) return value;
  const shifted = new Date(Date.UTC(parsed.year, parsed.month - 1 + delta, 1));
  return `${shifted.getUTCFullYear()}-${String(shifted.getUTCMonth() + 1).padStart(2, "0")}`;
}

function monthLabel(value: string): string {
  const parsed = parseMonth(value);
  if (!parsed) return value;
  return `${MONTHS[parsed.month - 1]} ${parsed.year}`;
}

function dayKey(month: string, day: number): string {
  return `${month}-${String(day).padStart(2, "0")}`;
}

/** Calendar with All-view item actions. The shared BottomBar still supplies Scan/Sync. */
export function CalendarPage({
  libraries,
  selectedLibraryId,
  onSelectLibrary,
  libraryMediaType,
  refreshKey,
  isAdmin,
}: CalendarPageProps) {
  const [month, setMonth] = useState<string | null>(null);
  const [libraryFilter, setLibraryFilter] = useState(() => selectedLibraryId ?? "");
  const [mediaTypeFilter, setMediaTypeFilter] = useState<MediaFilter>(() => libraryMediaType ?? "All");
  const [data, setData] = useState<CalendarEventsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedEvent, setSelectedEvent] = useState<CalendarEvent | null>(null);
  const [calendarRefreshKey, setCalendarRefreshKey] = useState(0);

  // Follow the shared library selector so Calendar and the global Sync toolbar
  // always describe the same library.
  useEffect(() => setLibraryFilter(selectedLibraryId ?? ""), [selectedLibraryId]);
  useEffect(() => setMediaTypeFilter(libraryMediaType ?? "All"), [libraryMediaType]);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    api.listCalendarEvents({
      month: month ?? undefined,
      libraryId: libraryFilter || undefined,
      mediaType: mediaTypeFilter === "All" ? undefined : mediaTypeFilter,
    })
      .then((result) => {
        if (cancelled) return;
        setData(result);
        // Adopt the server's current month so "today" follows the configured
        // timezone rather than the browser's clock.
        if (month === null) setMonth(result.month);
      })
      .catch((e) => {
        if (!cancelled) setError((e as Error).message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => { cancelled = true; };
  }, [month, libraryFilter, mediaTypeFilter, refreshKey, calendarRefreshKey]);

  const displayMonth = month ?? data?.month ?? "";
  const parsed = parseMonth(displayMonth);

  const eventsByDate = useMemo(() => {
    const grouped = new Map<string, CalendarEvent[]>();
    for (const event of data?.events ?? []) {
      const list = grouped.get(event.date) ?? [];
      list.push(event);
      grouped.set(event.date, list);
    }
    return grouped;
  }, [data?.events]);

  const gridInfo = useMemo(() => {
    if (!parsed) return { leading: 0, days: 0 };
    return {
      leading: new Date(Date.UTC(parsed.year, parsed.month - 1, 1)).getUTCDay(),
      days: new Date(Date.UTC(parsed.year, parsed.month, 0)).getUTCDate(),
    };
  }, [parsed?.year, parsed?.month]);

  function handleLibraryChange(value: string) {
    setLibraryFilter(value);
    const library = libraries.find((l) => l.id === value);
    setMediaTypeFilter(library ? library.mediaType : "All");
    onSelectLibrary(value || null);
  }

  function handleMonthPick(value: string) {
    const next = value.slice(0, 7);
    if (/^\d{4}-\d{2}$/.test(next)) setMonth(next);
  }

  return (
    <div style={{ maxWidth: "1180px", fontFamily: "var(--font-body)" }}>
      <h2 style={{ fontFamily: "var(--font-heading)", fontSize: "20px", color: "var(--text-primary)", margin: "0 0 4px" }}>
        Calendar
      </h2>
      <p style={{ fontSize: "13px", color: "var(--text-secondary)", margin: "0 0 4px" }}>
        Movie release dates and the next air date for each show, from cached metadata.
      </p>
      <p style={{ fontSize: "12px", color: "var(--text-muted)", margin: "0 0 1rem" }}>
        Sync applies to all libraries.
      </p>

      <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: "8px", marginBottom: "1rem" }}>
        <button
          type="button"
          aria-label="Previous month"
          onClick={() => parsed && setMonth(shiftMonth(displayMonth, -1))}
          disabled={!parsed}
          style={iconButtonStyle}
        >
          ‹
        </button>

        {/* Transparent native input over the label: tapping the Month YYYY
            heading opens the date picker on iOS Safari too. */}
        <label
          style={{
            ...controlStyle,
            position: "relative",
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            minWidth: "168px",
            cursor: "pointer",
            fontFamily: "var(--font-heading)",
            fontWeight: 600,
          }}
        >
          <span>{parsed ? monthLabel(displayMonth) : "Loading…"}</span>
          <input
            type="date"
            aria-label="Jump to month"
            value={parsed ? dayKey(displayMonth, 1) : ""}
            onChange={(e) => handleMonthPick(e.target.value)}
            style={{
              position: "absolute",
              inset: 0,
              width: "100%",
              height: "100%",
              opacity: 0,
              cursor: "pointer",
            }}
          />
        </label>

        <button
          type="button"
          aria-label="Next month"
          onClick={() => parsed && setMonth(shiftMonth(displayMonth, 1))}
          disabled={!parsed}
          style={iconButtonStyle}
        >
          ›
        </button>

        <div style={{ display: "flex", flexWrap: "wrap", gap: "8px", marginLeft: "auto" }}>
          <select
            aria-label="Library"
            value={libraryFilter}
            onChange={(e) => handleLibraryChange(e.target.value)}
            style={{ ...controlStyle, maxWidth: "260px" }}
          >
            <option value="">All libraries</option>
            {libraries.map((library) => (
              <option key={library.id} value={library.id}>{library.name}</option>
            ))}
          </select>

          <select
            aria-label="Media type"
            value={mediaTypeFilter}
            onChange={(e) => setMediaTypeFilter(e.target.value as MediaFilter)}
            disabled={libraryFilter !== ""}
            title={libraryFilter ? "Locked to this library's media type" : undefined}
            style={{
              ...controlStyle,
              maxWidth: "180px",
              ...(libraryFilter ? { opacity: 0.75, cursor: "not-allowed" } : {}),
            }}
          >
            <option value="All">All types</option>
            <option value="Movie">Movie</option>
            <option value="Show">Show</option>
            <option value="Anime">Anime</option>
          </select>
        </div>
      </div>

      {data && (
        <p style={{ fontSize: "12px", color: "var(--text-muted)", margin: "0 0 0.75rem" }}>
          Dates use server timezone {data.timeZone}; today is {data.today}.
        </p>
      )}
      {error && <p style={{ color: "var(--color-danger)", fontSize: "13px" }}>{error}</p>}

      {!parsed ? (
        <p style={{ color: "var(--text-muted)", fontSize: "13px" }}>Loading calendar…</p>
      ) : (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(7, minmax(0, 1fr))",
            gap: "6px",
            padding: "6px",
            border: "1px solid var(--border)",
            borderRadius: "var(--radius)",
            background: "var(--surface-card)",
            opacity: loading ? 0.65 : 1,
          }}
        >
          {WEEKDAYS.map((day) => (
            <div
              key={day}
              style={{
                padding: "4px 2px",
                textAlign: "center",
                fontSize: "11px",
                fontWeight: 600,
                color: "var(--text-secondary)",
              }}
            >
              {day}
            </div>
          ))}

          {Array.from({ length: gridInfo.leading }, (_, index) => (
            <div key={`blank-${index}`} aria-hidden="true" />
          ))}

          {Array.from({ length: gridInfo.days }, (_, index) => {
            const day = index + 1;
            const key = dayKey(displayMonth, day);
            const dayEvents = eventsByDate.get(key) ?? [];
            return (
              <div
                key={key}
                style={{
                  minHeight: "clamp(84px, 12vw, 130px)",
                  padding: "5px",
                  border: "1px solid var(--border)",
                  borderRadius: "calc(var(--radius) - 2px)",
                  background: "var(--surface-page)",
                  overflow: "hidden",
                }}
              >
                <div style={{ fontSize: "12px", fontWeight: 600 }}>
                  <span style={{ color: key === data?.today ? "var(--color-primary)" : "var(--text-secondary)" }}>
                    {day}
                  </span>
                </div>

                {dayEvents.map((event) => {
                  // Only the All-libraries view can act on an item; concrete
                  // library views stay read-only as in slice 1.
                  const clickable = libraryFilter === "";
                  const cardKey = `${event.id}-${event.date}`;
                  const cardStyle: CSSProperties = {
                    display: "flex",
                    gap: "5px",
                    alignItems: "flex-start",
                    marginTop: "4px",
                    padding: "4px",
                    border: "1px solid var(--border)",
                    borderRadius: "6px",
                    background: "var(--surface-raised)",
                    minWidth: 0,
                    ...(clickable
                      ? {
                          width: "100%",
                          textAlign: "left" as const,
                          fontFamily: "var(--font-body)",
                          color: "var(--text-primary)",
                          cursor: "pointer",
                          touchAction: "manipulation" as const,
                        }
                      : {}),
                  };
                  const contents = (
                    <>
                      {event.posterUrl && (
                        <img
                          src={event.posterUrl}
                          alt=""
                          style={{
                            width: "clamp(18px, 4vw, 28px)",
                            height: "clamp(27px, 6vw, 42px)",
                            objectFit: "cover",
                            borderRadius: "3px",
                            flexShrink: 0,
                          }}
                        />
                      )}
                      <div style={{ minWidth: 0 }}>
                        <div
                          style={{
                            fontSize: "11px",
                            lineHeight: 1.2,
                            color: "var(--text-primary)",
                            overflow: "hidden",
                            textOverflow: "ellipsis",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {event.title}
                        </div>
                        {event.isSeasonPremiere && (
                          <div style={{ fontSize: "10px", color: "var(--color-primary)", whiteSpace: "nowrap" }}>
                            New Season ({event.seasonNumber ?? "?"})
                          </div>
                        )}
                      </div>
                    </>
                  );
                  return clickable ? (
                    <button
                      type="button"
                      key={cardKey}
                      title={event.title}
                      aria-label={`${event.title} on ${event.date}`}
                      style={cardStyle}
                      onClick={() => setSelectedEvent(event)}
                    >
                      {contents}
                    </button>
                  ) : (
                    <div key={cardKey} title={event.title} style={cardStyle}>
                      {contents}
                    </div>
                  );
                })}
              </div>
            );
          })}
        </div>
      )}

      {!loading && data && data.events.length === 0 && (
        <p style={{ color: "var(--text-muted)", fontSize: "13px", marginTop: "0.75rem" }}>
          No dated items in this month for the current filters.
        </p>
      )}

      {selectedEvent && (
        <CalendarItemModal
          event={selectedEvent}
          libraries={libraries}
          isAdmin={isAdmin}
          onClose={() => setSelectedEvent(null)}
          onChanged={() => setCalendarRefreshKey((v) => v + 1)}
        />
      )}
    </div>
  );
}
