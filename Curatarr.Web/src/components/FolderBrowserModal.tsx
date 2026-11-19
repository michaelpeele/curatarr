import { useEffect, useState } from "react";
import { api, BrowseEntry } from "../api/client";

export function FolderBrowserModal({
  startPath,
  onSelect,
  onClose,
}: {
  startPath: string;
  onSelect: (path: string) => void;
  onClose: () => void;
}) {
  const [currentPath, setCurrentPath] = useState(startPath);
  const [parent, setParent] = useState<string | null>(null);
  const [entries, setEntries] = useState<BrowseEntry[]>([]);
  const [error, setError] = useState<string | null>(null);

  function load(path: string) {
    api
      .browseFilesystem(path)
      .then((res) => {
        setCurrentPath(res.path);
        setParent(res.parent);
        setEntries(res.entries);
        setError(null);
      })
      .catch((e) => setError(e.message));
  }

  useEffect(() => {
    load(startPath || "/");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div
      onClick={onClose}
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.5)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 200,
        padding: "1rem",
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          background: "var(--surface-card)",
          borderRadius: "var(--radius-card)",
          border: "2px solid var(--border-strong)",
          width: "480px",
          maxWidth: "100%",
          maxHeight: "70vh",
          display: "flex",
          flexDirection: "column",
          fontFamily: "var(--font-body)",
        }}
      >
        <div
          style={{
            padding: "10px 14px",
            borderBottom: "1px solid var(--border)",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
          }}
        >
          <strong style={{ fontSize: "13px", color: "var(--text-primary)" }}>Browse (server filesystem)</strong>
          <button onClick={onClose} style={{ border: "none", background: "none", cursor: "pointer", color: "var(--text-secondary)" }}>
            ✕
          </button>
        </div>
        <div
          style={{
            padding: "8px 14px",
            fontSize: "12px",
            color: "var(--text-secondary)",
            borderBottom: "1px solid var(--border)",
            wordBreak: "break-all",
          }}
        >
          {currentPath}
        </div>
        <div style={{ flex: 1, overflowY: "auto", padding: "6px 0" }}>
          {error && <p style={{ padding: "0 14px", color: "var(--color-danger)", fontSize: "12px" }}>{error}</p>}
          {parent && (
            <div
              onClick={() => load(parent)}
              style={{ padding: "6px 14px", cursor: "pointer", fontSize: "13px", color: "var(--color-primary)" }}
            >
              .. (up)
            </div>
          )}
          {entries.map((e) => (
            <div
              key={e.path}
              onClick={() => load(e.path)}
              style={{ padding: "6px 14px", cursor: "pointer", fontSize: "13px", color: "var(--text-primary)" }}
            >
              📁 {e.name}
            </div>
          ))}
          {entries.length === 0 && !error && (
            <p style={{ padding: "0 14px", fontSize: "12px", color: "var(--text-muted)" }}>No subfolders here.</p>
          )}
        </div>
        <div style={{ padding: "10px 14px", borderTop: "1px solid var(--border)", display: "flex", justifyContent: "flex-end" }}>
          <button
            onClick={() => onSelect(currentPath)}
            style={{
              fontFamily: "var(--font-body)",
              fontSize: "13px",
              padding: "6px 12px",
              borderRadius: "var(--radius)",
              border: "1px solid var(--color-primary)",
              background: "var(--color-primary)",
              color: "var(--text-on-accent)",
              cursor: "pointer",
            }}
          >
            Select this folder
          </button>
        </div>
      </div>
    </div>
  );
}
