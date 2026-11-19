import { useEffect, useState } from "react";
import { api, PathMapping } from "../api/client";

const inputStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  padding: "6px 8px",
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

export function PathMappingsSection() {
  const [mappings, setMappings] = useState<PathMapping[]>([]);
  const [fromPrefix, setFromPrefix] = useState("");
  const [toPrefix, setToPrefix] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  function refresh() {
    api.listPathMappings().then(setMappings).catch((e) => setError(e.message));
  }
  useEffect(refresh, []);

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await api.createPathMapping(fromPrefix, toPrefix);
      setFromPrefix("");
      setToPrefix("");
      refresh();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete(id: string) {
    if (!confirm("Remove this path mapping? New symlinks will stop translating paths through it.")) return;
    try {
      await api.deletePathMapping(id);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <div style={{ marginTop: "1.5rem" }}>
      <h3 style={{ fontFamily: "var(--font-body)", fontSize: "14px", color: "var(--text-primary)" }}>
        Path mappings
      </h3>
      <p style={{ fontSize: "13px", color: "var(--text-secondary)", lineHeight: 1.5 }}>
        Translates the path Curatarr sees into the path Plex sees for the same storage -- e.g. if
        Curatarr runs in Docker and sees your media at <code>/mnt/data</code>, but the Plex server
        mounts the same NAS share at <code>/Volumes/data</code>, add a mapping from the first to
        the second. Only applied to the symlink's target (the real file) when it's created --
        Curatarr's own file checks always use its own path.
      </p>

      <form onSubmit={handleAdd} style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", margin: "0.75rem 0" }}>
        <input placeholder="From (Curatarr sees, e.g. /mnt/data)" value={fromPrefix} onChange={(e) => setFromPrefix(e.target.value)} required style={{ ...inputStyle, flex: "1 1 200px" }} />
        <input placeholder="To (Plex sees, e.g. /Volumes/data)" value={toPrefix} onChange={(e) => setToPrefix(e.target.value)} required style={{ ...inputStyle, flex: "1 1 200px" }} />
        <button type="submit" disabled={submitting} style={buttonStyle}>
          {submitting ? "Adding..." : "Add mapping"}
        </button>
      </form>

      {error && <p style={{ color: "var(--color-danger)", fontSize: "13px" }}>{error}</p>}

      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
        <thead>
          <tr style={{ textAlign: "left", borderBottom: "1px solid var(--border)" }}>
            <th style={{ padding: "6px 4px" }}>From</th>
            <th style={{ padding: "6px 4px" }}>To</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {mappings.map((m) => (
            <tr key={m.id} style={{ borderBottom: "1px solid var(--border)" }}>
              <td style={{ padding: "6px 4px", color: "var(--text-primary)" }}>{m.fromPrefix}</td>
              <td style={{ padding: "6px 4px", color: "var(--text-secondary)" }}>{m.toPrefix}</td>
              <td style={{ padding: "6px 4px" }}>
                <button
                  onClick={() => handleDelete(m.id)}
                  style={{ border: "none", background: "none", color: "var(--color-danger)", cursor: "pointer", fontSize: "12px" }}
                >
                  Remove
                </button>
              </td>
            </tr>
          ))}
          {mappings.length === 0 && (
            <tr>
              <td colSpan={3} style={{ padding: "10px 4px", color: "var(--text-muted)" }}>
                No path mappings configured -- Curatarr's own paths are used as-is.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
