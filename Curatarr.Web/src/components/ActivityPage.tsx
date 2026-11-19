import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import {
  api,
  type ActivityCategory,
  type ActivityEvent,
  type ScanStatus,
} from "../api/client";

const CATEGORIES: (ActivityCategory | "All")[] = [
  "All",
  "Scheduling",
  "User",
  "Plex",
  "Scan",
  "Source",
  "Path",
];

const PAGE_SIZE = 50;

function formatTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime())
    ? iso
    : d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
}

function formatDuration(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  const m = Math.floor(s / 60);
  return `${m}:${String(s % 60).padStart(2, "0")}`;
}

const CATEGORY_ICON: Record<ActivityCategory, string> = {
  Scheduling: "⏰",
  User: "👤",
  Plex: "▶️",
  Scan: "🔍",
  Source: "📁",
  Path: "🔗",
};

export function ActivityPage() {
  const [items, setItems] = useState<ActivityEvent[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [category, setCategory] = useState<ActivityCategory | "All">("All");
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [live, setLive] = useState(false);
  const [scanStatus, setScanStatus] = useState<ScanStatus | null>(null);
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const seenIds = useRef<Set<string>>(new Set());

  const load = async (reset: boolean) => {
    setError(null);
    if (reset) setLoading(true);
    else setLoadingMore(true);
    try {
      const page = await api.listActivity({
        limit: PAGE_SIZE,
        category: category === "All" ? undefined : category,
        before: reset ? undefined : (nextCursor ?? undefined),
      });
      if (reset) {
        seenIds.current = new Set(page.items.map((i) => i.id));
        setItems(page.items);
      } else {
        const fresh = page.items.filter((i) => !seenIds.current.has(i.id));
        fresh.forEach((i) => seenIds.current.add(i.id));
        setItems((prev) => [...prev, ...fresh]);
      }
      setNextCursor(page.nextCursor);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
      setLoadingMore(false);
    }
  };

  // Reset and reload whenever the category filter changes.
  useEffect(() => {
    void load(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [category]);

  // Poll scan status while a scan is running (or until we learn it isn't).
  useEffect(() => {
    let cancelled = false;
    const tick = async () => {
      try {
        const status = await api.getScanStatus();
        if (!cancelled) setScanStatus(status);
      } catch {
        // Admin-only page; a transient failure just hides the card.
      }
    };
    void tick();
    const timer = window.setInterval(tick, 2000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, []);

  // Live activity stream. REST reload is the fallback when the hub drops.
  useEffect(() => {
    let connection: signalR.HubConnection | null = null;
    let cancelled = false;
    const start = async () => {
      connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/activity")
        .withAutomaticReconnect()
        .build();
      connection.on("ActivityCreated", (created: ActivityEvent | ActivityEvent[]) => {
        if (cancelled) return;
        const batch = Array.isArray(created) ? created : [created];
        const fresh = batch.filter((i) => {
          if (seenIds.current.has(i.id)) return false;
          seenIds.current.add(i.id);
          return true;
        });
        if (fresh.length === 0) return;
        if (category !== "All" && fresh.some((i) => i.category !== category)) {
          // A filtered view can't show these; a full reload keeps it consistent.
          void load(true);
          return;
        }
        setItems((prev) => [...fresh, ...prev]);
      });
      connection.onreconnected(() => void load(true));
      try {
        await connection.start();
        if (!cancelled) setLive(true);
      } catch {
        if (!cancelled) setLive(false);
      }
    };
    void start();
    return () => {
      cancelled = true;
      setLive(false);
      void connection?.stop();
      connectionRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [category]);

  const scan = scanStatus?.running ? scanStatus : null;
  const remaining = scan && scan.etaSeconds != null && scan.etaSeconds >= 0
    ? formatDuration(scan.etaSeconds)
    : null;

  return (
    <div style={{ fontFamily: "var(--font-body)", color: "var(--text-primary)" }}>
      <div style={{ display: "flex", alignItems: "center", gap: "10px", flexWrap: "wrap" }}>
        <h2 style={{ fontFamily: "var(--font-heading)", fontSize: "20px", margin: 0 }}>Activity</h2>
        <span
          style={{
            fontSize: "11px",
            padding: "2px 8px",
            borderRadius: "999px",
            border: "1px solid var(--border)",
            color: live ? "var(--color-success)" : "var(--text-secondary)",
          }}
        >
          {live ? "● Live" : "○ Reconnecting / offline"}
        </span>
        <div style={{ marginLeft: "auto", display: "flex", gap: "8px" }}>
          <select
            aria-label="Activity category"
            value={category}
            onChange={(e) => setCategory(e.target.value as ActivityCategory | "All")}
            style={{
              fontSize: "12px",
              padding: "4px 8px",
              borderRadius: "var(--radius)",
              border: "1px solid var(--border)",
              background: "var(--surface-card)",
              color: "var(--text-primary)",
            }}
          >
            {CATEGORIES.map((c) => (
              <option key={c} value={c}>{c === "All" ? "All activity" : c}</option>
            ))}
          </select>
          <button type="button" onClick={() => void load(true)} disabled={loading}
            style={{ fontSize: "12px", padding: "4px 10px" }}>
            Refresh
          </button>
        </div>
      </div>

      {scan && (
        <div
          style={{
            marginTop: "14px",
            padding: "12px 14px",
            borderRadius: "var(--radius)",
            border: "1px solid var(--border)",
            background: "var(--surface-card)",
            position: "sticky",
            top: 0,
            zIndex: 2,
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: "8px", fontWeight: 600 }}>
            🔍 Scan in progress
            {scan.paused && <span style={{ color: "var(--color-warning)", fontSize: "12px" }}>(paused)</span>}
          </div>
          <div style={{ marginTop: "6px", fontSize: "12px", color: "var(--text-secondary)" }}>
            {scan.completed} / {scan.total} items · elapsed {formatDuration(scan.elapsedSeconds)}
            {remaining != null ? ` · about ${remaining} remaining` : ""}
          </div>
          {scan.total > 0 && (
            <div style={{ marginTop: "8px", height: "6px", borderRadius: "999px", background: "var(--surface-raised)", overflow: "hidden" }}>
              <div style={{
                height: "100%",
                width: `${Math.min(100, (scan.completed / scan.total) * 100)}%`,
                background: "var(--color-success)",
              }} />
            </div>
          )}
        </div>
      )}

      {error && <p style={{ color: "var(--color-error)", fontSize: "13px", marginTop: "12px" }}>{error}</p>}

      <div style={{ marginTop: "14px", display: "flex", flexDirection: "column", gap: "2px" }}>
        {loading ? (
          <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>Loading activity…</p>
        ) : items.length === 0 ? (
          <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>No activity recorded yet.</p>
        ) : items.map((item) => (
          <div
            key={item.id}
            style={{
              display: "grid",
              gridTemplateColumns: "80px 26px 110px 1fr",
              gap: "8px",
              alignItems: "start",
              padding: "8px 6px",
              borderBottom: "1px solid var(--border)",
              fontSize: "12px",
            }}
          >
            <span style={{ color: "var(--text-secondary)", fontVariantNumeric: "tabular-nums" }}>
              {formatTime(item.occurredAt)}
            </span>
            <span title={item.category}>{CATEGORY_ICON[item.category] ?? "•"}</span>
            <span style={{ fontWeight: 600, wordBreak: "break-word" }}>{item.type}</span>
            <span style={{ color: "var(--text-secondary)", wordBreak: "break-word" }}>
              <strong style={{ color: "var(--text-primary)" }}>{item.actor}</strong> {item.detail}
            </span>
          </div>
        ))}
      </div>

      {nextCursor && !loading && (
        <div style={{ marginTop: "12px", textAlign: "center" }}>
          <button type="button" onClick={() => void load(false)} disabled={loadingMore}>
            {loadingMore ? "Loading…" : "Load more"}
          </button>
        </div>
      )}
    </div>
  );
}
