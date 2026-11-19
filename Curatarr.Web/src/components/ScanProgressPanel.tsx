import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { api } from "../api/client";

interface ScanProgressEvent {
  title: string;
  posterUrl: string | null;
  completed: number;
  total: number;
  status: "Found" | "PartialMatch" | "Skipped";
}

interface Props {
  active: boolean; // scan is currently running
  onDone?: () => void;
}

const MAX_RECENT = 12;

export function ScanProgressPanel({ active }: Props) {
  const [latest, setLatest] = useState<ScanProgressEvent | null>(null);
  const [recent, setRecent] = useState<ScanProgressEvent[]>([]);
  const [paused, setPaused] = useState(false);
  const [pauseBusy, setPauseBusy] = useState(false);
  const [stopBusy, setStopBusy] = useState(false);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (!active) {
      connectionRef.current?.stop();
      connectionRef.current = null;
      setPaused(false);
      return;
    }

    setLatest(null);
    setRecent([]);
    setPaused(false);

    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/scan")
      .withAutomaticReconnect()
      .build();

    connection.on("ScanProgress", (evt: ScanProgressEvent) => {
      setLatest(evt);
      setRecent((prev) => [evt, ...prev].slice(0, MAX_RECENT));
    });

    connection.start().catch((err) => {
      console.error("SignalR connection failed:", err);
    });

    connectionRef.current = connection;

    return () => {
      connection.stop();
    };
  }, [active]);

  async function togglePause() {
    setPauseBusy(true);
    try {
      if (paused) {
        await api.resumeScan();
        setPaused(false);
      } else {
        await api.pauseScan();
        setPaused(true);
      }
    } catch (e) {
      console.error("Pause/resume failed:", e);
    } finally {
      setPauseBusy(false);
    }
  }

  async function handleStop() {
    setStopBusy(true);
    try {
      await api.stopScan();
    } catch (e) {
      console.error("Stop failed:", e);
    } finally {
      setStopBusy(false);
    }
  }

  if (!active) return null;

  const percent = latest && latest.total > 0 ? Math.round((latest.completed / latest.total) * 100) : 0;

  return (
    <div
      style={{
        margin: "1rem 1.5rem 0",
        padding: "0.75rem 1rem",
        borderRadius: "var(--radius)",
        border: "1px solid var(--border)",
        background: "var(--surface-raised)",
        fontFamily: "var(--font-body)",
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
        <strong style={{ color: "var(--text-primary)", fontSize: "13px" }}>
          {paused ? "Paused" : "Scanning..."}
        </strong>
        <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
          {latest && (
            <span style={{ fontSize: "12px", color: "var(--text-secondary)" }}>
              {latest.completed} / {latest.total}
            </span>
          )}
          <button
            onClick={togglePause}
            disabled={pauseBusy || stopBusy}
            style={{
              fontSize: "11px",
              padding: "3px 8px",
              borderRadius: "var(--radius)",
              border: "1px solid var(--color-primary)",
              background: "transparent",
              color: "var(--color-primary)",
              cursor: "pointer",
              opacity: pauseBusy ? 0.6 : 1,
            }}
          >
            {paused ? "Resume" : "Pause"}
          </button>
          <button
            onClick={handleStop}
            disabled={stopBusy}
            style={{
              fontSize: "11px",
              padding: "3px 8px",
              borderRadius: "var(--radius)",
              border: "1px solid var(--color-danger)",
              background: "transparent",
              color: "var(--color-danger)",
              cursor: "pointer",
              opacity: stopBusy ? 0.6 : 1,
            }}
          >
            {stopBusy ? "Stopping..." : "Stop"}
          </button>
        </div>
      </div>

      {latest && (
        <>
          <div
            style={{
              height: "6px",
              borderRadius: "3px",
              background: "var(--border)",
              marginTop: "6px",
              overflow: "hidden",
            }}
          >
            <div
              style={{
                height: "100%",
                width: `${percent}%`,
                background: "var(--color-primary)",
                transition: "width 0.2s ease",
              }}
            />
          </div>
          <p style={{ fontSize: "12px", color: "var(--text-secondary)", margin: "6px 0 0" }}>
            Now processing: <strong style={{ color: "var(--text-primary)" }}>{latest.title}</strong>
          </p>
        </>
      )}

      {recent.length > 0 && (
        <div style={{ display: "flex", gap: "6px", marginTop: "10px", overflowX: "auto" }}>
          {recent.map((evt, i) => (
            <div
              key={`${evt.title}-${evt.completed}-${i}`}
              title={evt.title}
              style={{
                flexShrink: 0,
                width: "40px",
                height: "60px",
                borderRadius: "4px",
                overflow: "hidden",
                background: "var(--sage-100, #E4ECDA)",
                border: evt.status === "PartialMatch" ? "2px solid var(--color-danger)" : "1px solid var(--border)",
                opacity: i === 0 ? 1 : 0.6,
              }}
            >
              {evt.posterUrl && (
                <img
                  src={evt.posterUrl}
                  alt={evt.title}
                  style={{ width: "100%", height: "100%", objectFit: "cover" }}
                />
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
