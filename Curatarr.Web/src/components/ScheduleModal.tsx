import { useEffect, useRef, useState } from "react";
import { api, ScheduleStatus } from "../api/client";

/**
 * Schedule editor (Sprint 3 Slice 3). Admin-only modal opened from the
 * bottom bar's Schedule button.
 *
 * Flow: the first page offers exactly two choices — Daily or Seasonal.
 * Picking one reveals its editor:
 *
 * - Daily: one row per weekday with a checkbox, a start time picker and an
 *   end time picker. Picking a start fills the start box; picking an end
 *   completes the row and ticks the checkbox. A checked day with blank times
 *   means all day (ScheduleEngine: end == start means all day).
 * - Seasonal: from/to date rows (month/day only — repeats yearly, can wrap
 *   across year-end). Picking both dates ticks the row's checkbox. Rows that
 *   are unchecked or missing a date are discarded on save.
 *
 * Existing rules are hydrated into the editors. Save diffs form vs current:
 * changed rows update, new rows create, removed rows delete, and rules of the
 * other mode are removed.
 */
const DAY_NAMES = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

const inputStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  padding: "5px 8px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--border)",
  background: "var(--surface-card)",
  color: "var(--text-primary)",
};
const buttonStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  padding: "6px 12px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--color-primary)",
  background: "var(--color-primary)",
  color: "var(--text-on-accent)",
  cursor: "pointer",
};
const secondaryButtonStyle: React.CSSProperties = {
  ...buttonStyle,
  border: "1px solid var(--border)",
  background: "var(--surface-card)",
  color: "var(--text-primary)",
};
const rowStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: "0.6rem",
  padding: "0.35rem 0",
};
/** Read-only summary box that fills in as the picks are completed. */
const readBoxStyle: React.CSSProperties = {
  ...inputStyle,
  width: "8.5rem",
  color: "var(--text-secondary)",
  whiteSpace: "nowrap",
  overflow: "hidden",
  textOverflow: "ellipsis",
};

interface DailyRow {
  day: number; // 0=Sun .. 6=Sat
  checked: boolean;
  start: string; // "HH:mm" or ""
  end: string;
  /** original rule id when hydrated (null = new row) */
  id: string | null;
}

interface SeasonRow {
  id: string | null;
  checked: boolean;
  startMonth: number | null;
  startDay: number | null;
  endMonth: number | null;
  endDay: number | null;
}

const EMPTY_SEASON_ROW: SeasonRow = {
  id: null,
  checked: false,
  startMonth: null,
  startDay: null,
  endMonth: null,
  endDay: null,
};

type Page = "choose" | "daily" | "season";

const pad2 = (n: number) => String(n).padStart(2, "0");

function isLeapYear(year: number): boolean {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
}

/**
 * month/day -> value for <input type="date">. Only month/day is stored (ranges
 * repeat every year), so the year is just a display placeholder: use the
 * current year, or the next leap year when the date is Feb 29.
 */
function toDateValue(month: number | null, day: number | null): string {
  if (month == null || day == null) return "";
  let year = new Date().getFullYear();
  if (month === 2 && day === 29) {
    while (!isLeapYear(year)) year += 1;
  }
  return `${year}-${pad2(month)}-${pad2(day)}`;
}

/** <input type="date"> value -> month/day (year intentionally ignored). */
function fromDateValue(value: string): { month: number; day: number } | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!m) return null;
  const month = Number(m[2]);
  const day = Number(m[3]);
  if (month < 1 || month > 12 || day < 1 || day > 31) return null;
  return { month, day };
}

export function ScheduleModal(props: {
  libraryId: string;
  libraryName: string;
  onClose: () => void;
}) {
  const [page, setPage] = useState<Page>("choose");
  const [loaded, setLoaded] = useState(false);
  const [dailyRows, setDailyRows] = useState<DailyRow[]>(
    DAY_NAMES.map((_, i) => ({ day: i, checked: false, start: "", end: "", id: null }))
  );
  const [seasonRows, setSeasonRows] = useState<SeasonRow[]>([{ ...EMPTY_SEASON_ROW }]);
  const [timeZone, setTimeZone] = useState("");
  const [statusText, setStatusText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  // Load existing rules + status + timezone on mount.
  useEffect(() => {
    let cancelled = false;
    api
      .listSchedules(props.libraryId)
      .then((schedules) => {
        if (cancelled) return;
        const daily: DailyRow[] = DAY_NAMES.map((_, i) => ({
          day: i,
          checked: false,
          start: "",
          end: "",
          id: null,
        }));
        const seasons: SeasonRow[] = [];
        for (const s of schedules) {
          if (!s.enabled) continue;
          const hasSeason = s.startMonth != null || s.endMonth != null;
          if (hasSeason && s.daysOfWeek.length === 0) {
            seasons.push({
              id: s.id,
              checked: true,
              startMonth: s.startMonth,
              startDay: s.startDay,
              endMonth: s.endMonth,
              endDay: s.endDay,
            });
          } else if (s.daysOfWeek.length > 0) {
            for (const d of s.daysOfWeek) {
              if (d < 0 || d > 6) continue;
              daily[d] = { day: d, checked: true, start: s.startTime, end: s.endTime, id: s.id };
            }
          } else {
            // day-less, season-less rule: treat as daily-all.
            for (let d = 0; d < 7; d++) {
              daily[d] = { day: d, checked: true, start: s.startTime, end: s.endTime, id: s.id };
            }
          }
        }
        setDailyRows(daily);
        setSeasonRows(seasons.length > 0 ? seasons : [{ ...EMPTY_SEASON_ROW }]);
        setLoaded(true);
      })
      .catch((e) => !cancelled && setError((e as Error).message));

    api
      .getScheduleStatus(props.libraryId)
      .then((st) => {
        if (!cancelled) setStatusText(formatStatus(st));
      })
      .catch(() => {});

    api
      .getScheduleSettings()
      .then((s) => !cancelled && setTimeZone(s.timeZone || "server local time"))
      .catch(() => {});

    return () => {
      cancelled = true;
    };
  }, [props.libraryId]);

  // Live status refresh while the modal is open (flips change on the minute).
  useEffect(() => {
    const t = setInterval(() => {
      api
        .getScheduleStatus(props.libraryId)
        .then((st) => setStatusText(formatStatus(st)))
        .catch(() => {});
    }, 60_000);
    return () => clearInterval(t);
  }, [props.libraryId]);

  /** Patch a daily row; completing a time ticks the checkbox. */
  function setDaily(day: number, patch: Partial<DailyRow>) {
    setDailyRows((rows) =>
      rows.map((r) => {
        if (r.day !== day) return r;
        const next = { ...r, ...patch };
        if (patch.start !== undefined || patch.end !== undefined) {
          if (next.start && next.end) next.checked = true;
          else if (!next.start && !next.end) next.checked = false;
        }
        if (patch.checked === false) {
          next.start = "";
          next.end = "";
        }
        return next;
      })
    );
  }

  function addSeasonRow() {
    setSeasonRows((rows) => [...rows, { ...EMPTY_SEASON_ROW }]);
  }

  /** Patch a season row; completing both dates ticks the checkbox. */
  function patchSeason(index: number, patch: Partial<SeasonRow>) {
    setSeasonRows((rows) =>
      rows.map((r, i) => {
        if (i !== index) return r;
        const next = { ...r, ...patch };
        if (patch.checked === false) {
          next.startMonth = null;
          next.startDay = null;
          next.endMonth = null;
          next.endDay = null;
        }
        if (next.startMonth != null && next.startDay != null && next.endMonth != null && next.endDay != null) {
          next.checked = true;
        }
        return next;
      })
    );
  }

  function removeSeasonRow(index: number) {
    setSeasonRows((rows) => (rows.length <= 1 ? rows : rows.filter((_, i) => i !== index)));
  }

  async function handleSave() {
    if (page === "choose") return;
    setSaving(true);
    setError(null);
    try {
      const mode = page;
      const existing = await api.listSchedules(props.libraryId);

      // --- Daily diff ---
      const wantedDaily = mode === "daily" ? dailyRows.filter((r) => r.checked) : [];
      for (const row of wantedDaily) {
        const body = {
          name: `${DAY_NAMES[row.day]} window`,
          enabled: true,
          daysOfWeek: [row.day],
          startTime: row.start || "00:00",
          endTime: row.end || "00:00",
        };
        if (row.id) {
          const cur = existing.find((s) => s.id === row.id);
          if (
            cur &&
            (cur.startTime !== body.startTime || cur.endTime !== body.endTime || !cur.enabled)
          ) {
            await api.updateSchedule(props.libraryId, row.id, body);
          }
        } else {
          await api.createSchedule(props.libraryId, body);
        }
      }
      // Delete daily rules that were hydrated but are now unchecked/switched away.
      for (const s of existing) {
        const isDaily = s.daysOfWeek.length > 0 && s.startMonth == null;
        if (!isDaily || !s.enabled) continue;
        if (mode !== "daily" || !wantedDaily.some((r) => r.id === s.id)) {
          await api.deleteSchedule(props.libraryId, s.id);
        }
      }

      // --- Season diff (only checked rows with both dates; incomplete discarded) ---
      const wantedSeason =
        mode === "season"
          ? seasonRows.filter(
              (r) =>
                r.checked &&
                r.startMonth != null &&
                r.startDay != null &&
                r.endMonth != null &&
                r.endDay != null
            )
          : [];
      for (const row of wantedSeason) {
        const body = {
          name: `Season ${row.startMonth}/${row.startDay}–${row.endMonth}/${row.endDay}`,
          enabled: true,
          daysOfWeek: [] as number[],
          startTime: "00:00",
          endTime: "00:00",
          startMonth: row.startMonth,
          startDay: row.startDay,
          endMonth: row.endMonth,
          endDay: row.endDay,
        };
        if (row.id) {
          await api.updateSchedule(props.libraryId, row.id, body);
        } else {
          await api.createSchedule(props.libraryId, body);
        }
      }
      // Delete season rules no longer in the form (or mode switched to daily).
      for (const s of existing) {
        const isSeason = s.daysOfWeek.length === 0 && s.startMonth != null;
        if (!isSeason || !s.enabled) continue;
        if (mode !== "season" || !wantedSeason.some((r) => r.id === s.id)) {
          await api.deleteSchedule(props.libraryId, s.id);
        }
      }

      const st = await api.getScheduleStatus(props.libraryId);
      setStatusText(formatStatus(st));
      props.onClose();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.5)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={props.onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          background: "var(--surface-page)",
          borderRadius: "var(--radius)",
          padding: "1.25rem 1.5rem",
          width: "min(520px, calc(100vw - 2rem))",
          maxHeight: "85vh",
          overflowY: "auto",
          boxShadow: "0 8px 30px rgba(0,0,0,0.35)",
          fontFamily: "var(--font-body)",
          fontSize: "13px",
        }}
      >
        <div style={{ display: "flex", justifyContent: "space-between", marginBottom: "0.75rem" }}>
          <strong style={{ fontSize: "15px", color: "var(--text-primary)" }}>
            Schedule — {props.libraryName}
          </strong>
          <button
            onClick={props.onClose}
            style={{ border: "none", background: "none", cursor: "pointer", fontSize: "16px" }}
          >
            ✕
          </button>
        </div>

        {!loaded && <p style={{ color: "var(--text-secondary)" }}>Loading schedule…</p>}

        {/* First page: exactly two choices. */}
        {loaded && page === "choose" && (
          <div style={{ display: "flex", flexDirection: "column", gap: "0.6rem", padding: "0.25rem 0" }}>
            <button
              onClick={() => setPage("daily")}
              style={{ ...buttonStyle, padding: "10px 14px", fontSize: "14px" }}
            >
              Daily
            </button>
            <button
              onClick={() => setPage("season")}
              style={{ ...buttonStyle, padding: "10px 14px", fontSize: "14px" }}
            >
              Seasonal
            </button>
          </div>
        )}

        {loaded && page === "daily" && (
          <div>
            {dailyRows.map((row) => (
              <div key={row.day} style={rowStyle}>
                <input
                  type="checkbox"
                  checked={row.checked}
                  onChange={(e) => setDaily(row.day, { checked: e.target.checked })}
                />
                <span style={{ width: "2.6rem", color: "var(--text-primary)" }}>
                  {DAY_NAMES[row.day]}
                </span>
                <TimeRangeField
                  start={row.start}
                  end={row.end}
                  onChange={(patch) => setDaily(row.day, patch)}
                />
              </div>
            ))}
            <button
              onClick={() => setPage("choose")}
              style={{ ...secondaryButtonStyle, marginTop: "0.75rem" }}
            >
              Back
            </button>
          </div>
        )}

        {loaded && page === "season" && (
          <div>
            {seasonRows.map((row, i) => (
              <div key={i} style={rowStyle}>
                <input
                  type="checkbox"
                  checked={row.checked}
                  onChange={(e) => patchSeason(i, { checked: e.target.checked })}
                />
                <span style={{ color: "var(--text-secondary)", fontSize: "12px" }}>from</span>
                <input
                  type="date"
                  value={toDateValue(row.startMonth, row.startDay)}
                  onChange={(e) => {
                    const v = fromDateValue(e.target.value);
                    patchSeason(i, {
                      startMonth: v ? v.month : null,
                      startDay: v ? v.day : null,
                    });
                  }}
                  style={{ ...inputStyle, width: "9.6rem" }}
                  title="Pick the first day of the range"
                />
                <span style={{ color: "var(--text-secondary)", fontSize: "12px" }}>to</span>
                <input
                  type="date"
                  value={toDateValue(row.endMonth, row.endDay)}
                  onChange={(e) => {
                    const v = fromDateValue(e.target.value);
                    patchSeason(i, { endMonth: v ? v.month : null, endDay: v ? v.day : null });
                  }}
                  style={{ ...inputStyle, width: "9.6rem" }}
                  title="Pick the last day of the range"
                />
                <span style={readBoxStyle} title="Completed range">
                  {row.startMonth != null && row.startDay != null && row.endMonth != null && row.endDay != null
                    ? `${row.startMonth}/${row.startDay} – ${row.endMonth}/${row.endDay}`
                    : "pick dates"}
                </span>
                <button
                  onClick={() => removeSeasonRow(i)}
                  disabled={seasonRows.length === 1}
                  style={{
                    ...secondaryButtonStyle,
                    padding: "2px 8px",
                    opacity: seasonRows.length === 1 ? 0.4 : 1,
                  }}
                  title="Remove this row"
                >
                  −
                </button>
                <button
                  onClick={addSeasonRow}
                  style={{ ...secondaryButtonStyle, padding: "2px 8px" }}
                  title="Add another season row"
                >
                  +
                </button>
              </div>
            ))}
            <p style={{ color: "var(--text-secondary)", fontSize: "12px", marginTop: "0.5rem" }}>
              Repeat every year — only the month and day are saved, so the year in the date picker
              is ignored.
            </p>
            <button
              onClick={() => setPage("choose")}
              style={{ ...secondaryButtonStyle, marginTop: "0.75rem" }}
            >
              Back
            </button>
          </div>
        )}

        {loaded && page !== "choose" && (
          <p style={{ color: "var(--text-secondary)", fontSize: "12px", marginTop: "0.5rem" }}>
            Times in {timeZone}. Overnight windows (end before start) cross midnight. A checked
            day with blank times means all day.
          </p>
        )}

        {statusText && (
          <p style={{ color: "var(--text-secondary)", fontSize: "13px", marginBottom: "0.5rem" }}>
            {statusText}
          </p>
        )}
        {error && <p style={{ color: "var(--color-danger)", fontSize: "13px" }}>{error}</p>}

        <div style={{ display: "flex", gap: "0.5rem", justifyContent: "flex-end", marginTop: "0.75rem" }}>
          <button onClick={props.onClose} style={secondaryButtonStyle}>
            {page === "choose" ? "Close" : "Cancel"}
          </button>
          {page !== "choose" && (
            <button onClick={handleSave} disabled={saving} style={buttonStyle}>
              {saving ? "Saving…" : "Save schedule"}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

/** Shared library-page / modal status line. */
export function formatStatus(st: ScheduleStatus): string {
  if (st.scheduleCount === 0) return "";
  if (st.alwaysOn) return "Always on";
  const when = st.nextTransitionAt
    ? new Date(st.nextTransitionAt).toLocaleString([], {
        weekday: "short",
        month: "short",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit",
        timeZoneName: "short",
      })
    : null;
  if (!when) return st.isActive ? "Showing now (no upcoming transition)" : "Not showing";
  return st.isActive ? `Stops showing ${when}` : `Shows next ${when}`;
}

/** "18:30" -> "6:30 PM" (display only). */
function fmtTime(value: string): string {
  const m = /^(\d{1,2}):(\d{2})$/.exec(value);
  if (!m) return value;
  const hours = Number(m[1]);
  const suffix = hours < 12 ? "AM" : "PM";
  const h12 = hours % 12 === 0 ? 12 : hours % 12;
  return `${h12}:${m[2]} ${suffix}`;
}

const HOURS = [12, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
const MINUTES = Array.from({ length: 60 }, (_, i) => i);

interface TimeParts {
  hour: number;
  minute: number;
  pm: boolean;
}

function parseTime(value: string): TimeParts | null {
  const m = /^(\d{1,2}):(\d{2})$/.exec(value);
  if (!m) return null;
  const hours = Number(m[1]);
  return { hour: hours % 12 === 0 ? 12 : hours % 12, minute: Number(m[2]), pm: hours >= 12 };
}

function toTimeValue(parts: TimeParts): string {
  const hours = (parts.hour % 12) + (parts.pm ? 12 : 0);
  return `${String(hours).padStart(2, "0")}:${String(parts.minute).padStart(2, "0")}`;
}

function addHours(parts: TimeParts, hours: number): TimeParts {
  const total = ((parts.hour % 12) + (parts.pm ? 12 : 0) + hours + 24) % 24;
  return { hour: total % 12 === 0 ? 12 : total % 12, minute: parts.minute, pm: total >= 12 };
}

/**
 * One text box per weekday that shows the chosen window and opens an in-app
 * time picker (hour / minute / AM-PM). Safari does not render a picker for
 * <input type="time">, so the picker is built here instead of relying on the
 * native control. First pick sets the start time, the next pick sets the end
 * time, after which the row auto-checks itself.
 */
function TimeRangeField(props: {
  start: string;
  end: string;
  onChange: (patch: { start?: string; end?: string }) => void;
}) {
  const [open, setOpen] = useState<null | "start" | "end">(null);
  const [draft, setDraft] = useState<TimeParts>({ hour: 6, minute: 0, pm: false });
  const boxRef = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(null);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  const openFor = (which: "start" | "end") => {
    const current = which === "start" ? props.start : props.end;
    const other = which === "start" ? props.end : props.start;
    const parsed =
      parseTime(current) ??
      parseTime(other) ??
      (which === "start" ? { hour: 9, minute: 0, pm: false } : { hour: 5, minute: 0, pm: true });
    setDraft(parsed);
    setOpen(which);
  };

  const commit = () => {
    const value = toTimeValue(draft);
    if (open === "start") {
      props.onChange({ start: value });
      // Follow straight into the end picker so a window takes two clicks.
      if (!props.end) {
        setDraft(addHours(draft, 2));
        setOpen("end");
        return;
      }
    } else if (open === "end") {
      props.onChange({ end: value });
    }
    setOpen(null);
  };

  const label =
    props.start && props.end
      ? `${fmtTime(props.start)} – ${fmtTime(props.end)}`
      : props.start
        ? `${fmtTime(props.start)} – …`
        : "pick a time";

  return (
    <span ref={boxRef} style={{ position: "relative", display: "inline-flex" }}>
      <button
        type="button"
        onClick={() => openFor(!props.start ? "start" : !props.end ? "end" : "start")}
        title="Click to pick a time"
        style={{ ...readBoxStyle, width: "11.5rem", textAlign: "left", cursor: "pointer" }}
      >
        {label}
      </button>
      {open && (
        <div
          style={{
            position: "absolute",
            top: "calc(100% + 4px)",
            left: 0,
            zIndex: 3,
            display: "flex",
            alignItems: "center",
            gap: "0.3rem",
            padding: "0.45rem",
            borderRadius: "var(--radius)",
            border: "1px solid var(--border)",
            background: "var(--surface-page)",
            boxShadow: "0 6px 18px rgba(0,0,0,0.3)",
          }}
        >
          <select
            aria-label="Hour"
            value={draft.hour}
            onChange={(e) => setDraft({ ...draft, hour: Number(e.target.value) })}
            style={{ ...inputStyle, width: "4rem", padding: "3px 4px" }}
          >
            {HOURS.map((h) => (
              <option key={h} value={h}>
                {h}
              </option>
            ))}
          </select>
          <span style={{ color: "var(--text-secondary)" }}>:</span>
          <select
            aria-label="Minute"
            value={draft.minute}
            onChange={(e) => setDraft({ ...draft, minute: Number(e.target.value) })}
            style={{ ...inputStyle, width: "4rem", padding: "3px 4px" }}
          >
            {MINUTES.map((m) => (
              <option key={m} value={m}>
                {String(m).padStart(2, "0")}
              </option>
            ))}
          </select>
          <button
            type="button"
            onClick={() => setDraft({ ...draft, pm: !draft.pm })}
            style={{ ...secondaryButtonStyle, padding: "3px 8px" }}
          >
            {draft.pm ? "PM" : "AM"}
          </button>
          <button type="button" onClick={commit} style={{ ...buttonStyle, padding: "3px 10px" }}>
            {open === "start" && !props.end ? "Next" : "Done"}
          </button>
        </div>
      )}
    </span>
  );
}
