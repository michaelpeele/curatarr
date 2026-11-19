import React from "react";
import { VirtualLibrary, MembershipRole, api, ScheduleStatus } from "../api/client";
import { ScanProgressPanel } from "./ScanProgressPanel";
import { ScheduleModal, formatStatus } from "./ScheduleModal";
import { useToast } from "../context/ToastContext";
import { useViewport } from "../lib/useViewport";

interface ScanResult {
  scanned: number;
  created: number;
  updated: number;
  stopped: boolean;
}
interface ReconcileResult {
  created: string[];
  removed: string[];
  broken: string[];
}

export function BottomBar(props: {
  libraries: (VirtualLibrary & { myRole?: MembershipRole })[];
  selectedLibraryId: string | null;
  onSelectLibrary: (id: string | null) => void;
  /** Calendar-only: null means "All libraries" and is a valid selection. */
  allowAllLibraries?: boolean;
  isAdmin: boolean;
  scanning: boolean;
  onScan: (libraryId: string) => void;
  reconciling: boolean;
  onReconcile: () => void;
  meEmail: string;
  onLogout: () => void;
  scanResult: ScanResult | null;
  scanError: string | null;
  onDismissScan: () => void;
  reconcileResult: ReconcileResult | null;
  reconcileError: string | null;
  onDismissReconcile: () => void;
  /** Fires the per-library reconcile immediately (admin evaluate). */
  onEvaluateSchedule?: () => void;
}) {
  const [scheduleOpen, setScheduleOpen] = React.useState(false);
  const [scheduleLabel, setScheduleLabel] = React.useState("");
  const { showToast } = useToast();

  // Next/stop-showing hint for the selected library. Refreshes every 60 s so
  // the label flips on its own when a window boundary passes.
  React.useEffect(() => {
    const libraryId = props.selectedLibraryId;
    if (!libraryId) {
      setScheduleLabel("");
      return;
    }
    let cancelled = false;
    const load = () => {
      api
        .getScheduleStatus(libraryId)
        .then((st: ScheduleStatus) => {
          if (!cancelled) setScheduleLabel(formatStatus(st));
        })
        .catch(() => {
          if (!cancelled) setScheduleLabel("");
        });
    };
    load();
    const timer = window.setInterval(load, 60_000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [props.selectedLibraryId]);
  const { isMobile } = useViewport();
  
  // Show toast when scan result/error appears (once)
  const prevResultRef = React.useRef<string | null>(null);
  const prevErrorRef = React.useRef<string | null>(null);
  
  React.useEffect(() => {
    const key = `${props.scanResult?.scanned ?? ''}-${props.scanResult?.created ?? ''}`;
    if (props.scanError && props.scanError !== prevErrorRef.current) {
      prevErrorRef.current = props.scanError;
      showToast(`Scan failed: ${props.scanError}`, "error");
    } else if (props.scanResult && key !== prevResultRef.current) {
      prevResultRef.current = key;
      if (props.scanResult.stopped) {
        showToast(`Scan stopped: ${props.scanResult.scanned} found`, "info");
      } else {
        showToast(
          `Scan complete: ${props.scanResult.created} new, ${props.scanResult.updated} updated`,
          "success"
        );
      }
    }
  }, [props.scanResult, props.scanError, showToast]);

  React.useEffect(() => {
    if (props.reconcileError) {
      showToast(`Sync failed: ${props.reconcileError}`, "error");
    } else if (props.reconcileResult) {
      const {
        created,
        removed
      } = props.reconcileResult;
      showToast(
        `Library updated: ${created.length} added, ${removed.length} removed`,
        "success"
      );
    }
  }, [props.reconcileResult, props.reconcileError, showToast]);
  
  const barStyle: React.CSSProperties = {
    borderTop: "1px solid var(--border)",
    background: "var(--surface-page)",
    flexShrink: 0,
    ...(isMobile ? {
      position: "fixed",
      bottom: 0,
      left: 0,
      right: 0,
      zIndex: 900,
      boxShadow: "0 -2px 8px rgba(0,0,0,0.1)"
    } : {})
  };
  
  return (
    <div style={barStyle}>
      {props.scanning && <ScanProgressPanel active={props.scanning} />}
      {(props.scanResult || props.scanError) && (
        <StatusPanel
          title={props.scanError ? "Scan failed" : props.scanResult?.stopped ? "Scan stopped early" : "Scan complete"}
          error={props.scanError}
          onDismiss={props.onDismissScan}
        >
          {props.scanResult && (
            <>
              <Stat label="Found" count={props.scanResult.scanned} color="var(--text-secondary)" />
              <Stat label="New" count={props.scanResult.created} color="var(--color-success)" />
              <Stat label="Updated" count={props.scanResult.updated} color="var(--color-info)" />
            </>
          )}
        </StatusPanel>
      )}
      {(props.reconcileResult || props.reconcileError) && (
        <StatusPanel
          title={props.reconcileError ? "Sync failed" : "Library Updated"}
          error={props.reconcileError}
          onDismiss={props.onDismissReconcile}
        >
          {props.reconcileResult && (
            <>
              <Stat label="Added" count={props.reconcileResult.created.length} color="var(--color-success)" />
              <Stat label="Removed" count={props.reconcileResult.removed.length} color="var(--color-info)" />
              <Stat label="Broken" count={props.reconcileResult.broken.length} color="var(--color-danger)" />
            </>
          )}
        </StatusPanel>
      )}

      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "0.75rem 1.5rem",
          fontFamily: "var(--font-body)",
          ...(isMobile ? { flexDirection: "column", gap: "0.5rem", padding: "0.5rem 1rem" } : {})
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", ...(isMobile ? { width: "100%", justifyContent: "space-between" } : {}) }}>
          {props.libraries.length > 0 && (
            <>
              <span
                style={{
                  fontSize: "12px",
                  color: "var(--text-secondary)",
                  whiteSpace: "nowrap",
                }}
              >
                Library:
              </span>
              <select
                value={props.selectedLibraryId ?? ""}
                onChange={(e) => props.onSelectLibrary(e.target.value || null)}
                aria-label="Library"
                style={{ fontFamily: "var(--font-body)", fontSize: "13px", padding: "4px 8px", ...(isMobile ? { flex: 1 } : {}) }}
              >
                {props.allowAllLibraries && <option value="">All libraries</option>}
                {props.libraries.map((lib) => (
                  <option key={lib.id} value={lib.id}>
                    {lib.name}
                  </option>
                ))}
              </select>
            </>
          )}
          {props.isAdmin && (
            <>
              <button
                onClick={() => props.selectedLibraryId && props.onScan(props.selectedLibraryId)}
                disabled={props.scanning || !props.selectedLibraryId}
                title={!props.selectedLibraryId ? "Select a single library to scan" : undefined}
                style={buttonStyle}
              >
                {props.scanning ? "Scanning..." : "Scan"}
              </button>
              <button
                onClick={props.onReconcile}
                disabled={props.reconciling}
                title="Marking items only records your choices -- this actually creates/removes the file links"
                style={buttonStyle}
              >
                {props.reconciling ? "Syncing..." : "Sync"}
              </button>
              <button
                onClick={() => setScheduleOpen(true)}
                disabled={!props.selectedLibraryId}
                title={props.selectedLibraryId ? "Choose when this library appears" : "Select a single library to schedule"}
                style={buttonStyle}
              >
                Schedule
              </button>
              {scheduleLabel && (
                <span style={{ fontSize: "12px", color: "var(--text-secondary)" }}>
                  {scheduleLabel}
                </span>
              )}
            </>
          )}
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: "1rem", ...(isMobile ? { justifyContent: "space-between", width: "100%" } : {}) }}>
          <span style={{ fontSize: "13px", color: "var(--text-secondary)" }}>{props.meEmail}</span>
          <button onClick={props.onLogout} style={{ ...buttonStyle, padding: "4px 10px", fontSize: "12px" }}>
            Sign out
          </button>
        </div>
      </div>

      {scheduleOpen && props.selectedLibraryId && props.isAdmin && (
        <ScheduleModal
          libraryId={props.selectedLibraryId}
          libraryName={
            props.libraries.find((l) => l.id === props.selectedLibraryId)?.name ?? "Library"
          }
          onClose={() => setScheduleOpen(false)}
        />
      )}
    </div>
  );
}

function StatusPanel({
  title,
  error,
  onDismiss,
  children,
}: {
  title: string;
  error: string | null;
  onDismiss: () => void;
  children?: React.ReactNode;
}) {
  return (
    <div
      style={{
        padding: "0.6rem 1.5rem",
        borderBottom: "1px solid var(--border)",
        fontFamily: "var(--font-body)",
        fontSize: "13px",
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <strong style={{ color: "var(--text-primary)" }}>{title}</strong>
        <button
          onClick={onDismiss}
          style={{ border: "none", background: "none", color: "var(--text-secondary)", cursor: "pointer", fontSize: "12px" }}
        >
          Dismiss
        </button>
      </div>
      {error && <p style={{ color: "var(--color-danger)", margin: "4px 0 0" }}>{error}</p>}
      {children && <div style={{ display: "flex", gap: "1.5rem", marginTop: "4px" }}>{children}</div>}
    </div>
  );
}

function Stat({ label, count, color }: { label: string; count: number; color: string }) {
  return (
    <span style={{ color: "var(--text-secondary)" }}>
      {label}: <strong style={{ color }}>{count}</strong>
    </span>
  );
}

const buttonStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  padding: "6px 10px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--color-primary)",
  background: "var(--color-primary)",
  color: "var(--text-on-accent)",
  cursor: "pointer",
};
