import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { api, type LogConfig, type LogEntry } from "../api/client";

const LEVELS = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];
const RETENTIONS = [7, 14, 21, 30, 60, 90];
const UI_CAP = 2000;

/** Newest-first? No — logs stream oldest → newest, so we append and keep the last UI_CAP. */
function formatTime(ts: string): string {
  const d = new Date(ts);
  return Number.isNaN(d.getTime()) ? ts : d.toLocaleTimeString([], { hour12: false });
}

const LEVEL_COLOR: Record<string, string> = {
  Trace: "var(--text-secondary)",
  Debug: "var(--text-secondary)",
  Information: "var(--color-info, #3b82f6)",
  Warning: "#d4a017",
  Error: "var(--color-danger, #dc2626)",
  Critical: "var(--color-danger, #dc2626)",
};

export function LogsPage() {
  const [entries, setEntries] = useState<LogEntry[]>([]);
  const [config, setConfig] = useState<LogConfig | null>(null);
  const [draftLevel, setDraftLevel] = useState("Information");
  const [draftRetention, setDraftRetention] = useState(7);
  const [monitor, setMonitor] = useState(true);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saveStatus, setSaveStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const listRef = useRef<HTMLDivElement | null>(null);
  const seenRef = useRef<Set<number>>(new Set());

  const appendEntries = (incoming: LogEntry[]) => {
    if (incoming.length === 0) return;
    const fresh = incoming.filter((e) => !seenRef.current.has(e.seq));
    if (fresh.length === 0) return;
    for (const e of fresh) seenRef.current.add(e.seq);
    setEntries((prev) => {
      const next = [...prev, ...fresh];
      return next.length > UI_CAP ? next.slice(next.length - UI_CAP) : next;
    });
  };

  const refresh = async () => {
    setLoading(true);
    setError(null);
    try {
      const page = await api.listLogs(0, 1000);
      seenRef.current = new Set(page.entries.map((e) => e.seq));
      setEntries(page.entries);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  };

  const loadConfig = async () => {
    try {
      const cfg = await api.getLogConfig();
      setConfig(cfg);
      setDraftLevel(cfg.level);
      setDraftRetention(cfg.retentionDays);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  useEffect(() => {
    refresh();
    loadConfig();
  }, []);

  useEffect(() => {
    if (!monitor) {
      connectionRef.current?.stop().catch(() => undefined);
      connectionRef.current = null;
      return;
    }
    let cancelled = false;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/logs")
      .withAutomaticReconnect()
      .build();
    connection.on("LogEntry", (entry: LogEntry) => appendEntries([entry]));
    connection.start()
      .then(() => { if (!cancelled) connectionRef.current = connection; })
      .catch(() => undefined);
    return () => {
      cancelled = true;
      connection.stop().catch(() => undefined);
      if (connectionRef.current === connection) connectionRef.current = null;
    };
  }, [monitor]);

  useEffect(() => {
    if (monitor && listRef.current) {
      listRef.current.scrollTop = listRef.current.scrollHeight;
    }
  }, [entries, monitor]);

  const save = async () => {
    setSaving(true);
    setSaveStatus(null);
    setError(null);
    try {
      let cfg: LogConfig;
      if (draftLevel !== config?.level) {
        cfg = await api.setLogLevel(draftLevel);
      } else {
        cfg = await api.getLogConfig();
      }
      if (draftRetention !== config?.retentionDays) {
        cfg = await api.setLogConfig({
          ...cfg,
          retentionDays: draftRetention,
        });
      }
      setConfig(cfg);
      setSaveStatus("Saved");
    } catch (e) {
      setError((e as Error).message);
      setSaveStatus("Save failed");
    } finally {
      setSaving(false);
    }
  };

  const toggleExpanded = (seq: number) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(seq)) next.delete(seq);
      else next.add(seq);
      return next;
    });
  };

  const controlStyle = {
    padding: "6px 10px",
    borderRadius: "var(--radius)",
    border: "1px solid var(--border)",
    background: "var(--surface-raised)",
    color: "var(--text-primary)",
    fontSize: "13px",
  };

  return (
    <div style={{ fontFamily: "var(--font-body)", color: "var(--text-primary)" }}>
      <div style={{ display: "flex", alignItems: "center", gap: "12px", flexWrap: "wrap" }}>
        <h2 style={{ fontFamily: "var(--font-heading)", fontSize: "20px", margin: 0 }}>Logs</h2>
        <span style={{
          fontSize: "11px",
          padding: "2px 8px",
          borderRadius: "999px",
          background: monitor ? "rgba(34,197,94,0.15)" : "var(--surface-raised)",
          color: monitor ? "var(--color-success, #16a34a)" : "var(--text-secondary)",
        }}>
          {monitor ? "Live" : "Paused"}
        </span>
        <button style={controlStyle} onClick={refresh} disabled={loading}>Refresh</button>
        <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px" }}>
          <input type="checkbox" checked={monitor} onChange={(e) => setMonitor(e.target.checked)} />
          Monitor live
        </label>
        <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px" }}>
          Level
          <select style={controlStyle} value={draftLevel} onChange={(e) => setDraftLevel(e.target.value)}>
            {LEVELS.map((l) => <option key={l} value={l}>{l}</option>)}
          </select>
        </label>
        <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px" }}>
          Retention
          <select style={controlStyle} value={draftRetention} onChange={(e) => setDraftRetention(Number(e.target.value))}>
            {RETENTIONS.map((r) => <option key={r} value={r}>{r} days</option>)}
          </select>
        </label>
        <button style={controlStyle} onClick={save} disabled={saving}>Save</button>
        {saveStatus && <span style={{ fontSize: "12px", color: "var(--text-secondary)" }}>{saveStatus}</span>}
      </div>

      {draftLevel === "Debug" && (
        <div style={{
          marginTop: "10px",
          padding: "8px 12px",
          borderRadius: "var(--radius)",
          border: "1px solid #d4a017",
          background: "rgba(212,160,23,0.1)",
          color: "#d4a017",
          fontSize: "13px",
        }}>
          Debug produces a large volume of logs and may affect performance — troubleshooting only.
        </div>
      )}
      {error && <p style={{ color: "var(--color-danger, #dc2626)", fontSize: "13px" }}>{error}</p>}

      <div
        ref={listRef}
        style={{
          marginTop: "12px",
          maxHeight: "60vh",
          overflowY: "auto",
          border: "1px solid var(--border)",
          borderRadius: "var(--radius)",
          background: "var(--surface-card)",
          fontFamily: "ui-monospace, SFMono-Regular, Menlo, monospace",
          fontSize: "12px",
        }}
      >
        {loading && entries.length === 0 && (
          <div style={{ padding: "12px", color: "var(--text-secondary)" }}>Loading logs…</div>
        )}
        {!loading && entries.length === 0 && (
          <div style={{ padding: "12px", color: "var(--text-secondary)" }}>No log entries yet.</div>
        )}
        {entries.map((entry) => (
          <div key={entry.seq} style={{ borderBottom: "1px solid var(--border)" }}>
            <div
              onClick={() => entry.exception && toggleExpanded(entry.seq)}
              style={{
                display: "grid",
                gridTemplateColumns: "92px 76px 180px 1fr",
                gap: "8px",
                padding: "5px 10px",
                cursor: entry.exception ? "pointer" : "default",
              }}
            >
              <span style={{ color: "var(--text-secondary)" }}>{formatTime(entry.timestamp)}</span>
              <span style={{ color: LEVEL_COLOR[entry.level] ?? "var(--text-primary)", fontWeight: 600 }}>
                {entry.level}
              </span>
              <span style={{ color: "var(--text-secondary)", overflow: "hidden", textOverflow: "ellipsis" }}>
                {entry.sourceContext || "—"}
              </span>
              <span style={{ wordBreak: "break-word" }}>{entry.message}</span>
            </div>
            {expanded.has(entry.seq) && entry.exception && (
              <pre style={{
                margin: 0,
                padding: "8px 10px 10px 186px",
                whiteSpace: "pre-wrap",
                color: "var(--color-danger, #dc2626)",
                background: "var(--surface-raised)",
              }}>
                {entry.exception}
              </pre>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
