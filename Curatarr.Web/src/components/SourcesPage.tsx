import { useEffect, useState } from "react";
import { api, SourceRoot, MediaKind } from "../api/client";
import { PathInput } from "./PathInput";
import { DeleteXButton } from "./DeleteXButton";

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

export function SourcesPage() {
  const [roots, setRoots] = useState<SourceRoot[]>([]);
  const [name, setName] = useState("");
  const [path, setPath] = useState("");
  const [mediaType, setMediaType] = useState<MediaKind>("Movie");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState("");
  const [editPath, setEditPath] = useState("");
  const [editMediaType, setEditMediaType] = useState<MediaKind>("Movie");

  function refresh() {
    api.listSourceRoots().then(setRoots).catch((e) => setError(e.message));
  }

  useEffect(refresh, []);

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await api.createSourceRoot(name, path, mediaType);
      setName("");
      setPath("");
      refresh();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete(root: SourceRoot) {
    if (!confirm(`Remove source root "${root.name}"? This only stops future scans of it -- already-scanned items and files on disk aren't touched.`)) return;
    try {
      await api.deleteSourceRoot(root.id);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  function startEdit(root: SourceRoot) {
    setEditingId(root.id);
    setEditName(root.name);
    setEditPath(root.path);
    setEditMediaType(root.mediaType);
  }

  async function handleSaveEdit(id: string) {
    try {
      await api.updateSourceRoot(id, editName, editPath, editMediaType);
      setEditingId(null);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <div style={{ maxWidth: "720px", fontFamily: "var(--font-body)" }}>
      <h2 style={{ fontFamily: "var(--font-heading)", fontSize: "20px", color: "var(--text-primary)" }}>Sources</h2>
      <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>
        Folders on disk that get scanned for movies/shows. Removing a root here only stops future
        scans of it -- it doesn't touch already-scanned items or any files.
      </p>

      <form onSubmit={handleAdd} style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", margin: "1rem 0", alignItems: "flex-start" }}>
        <input placeholder="Name (e.g. Real Movies)" value={name} onChange={(e) => setName(e.target.value)} required style={{ ...inputStyle, flex: "1 1 160px" }} />
        <PathInput value={path} onChange={setPath} placeholder="Path (e.g. /Volumes/data/movies)" style={{ flex: "2 1 260px" }} />
        <select value={mediaType} onChange={(e) => setMediaType(e.target.value as MediaKind)} style={inputStyle}>
          <option value="Movie">Movie</option>
          <option value="Show">Show</option>
          <option value="Anime">Anime</option>
        </select>
        <button type="submit" disabled={submitting} style={buttonStyle}>
          {submitting ? "Adding..." : "Add root"}
        </button>
      </form>

      {error && <p style={{ color: "var(--color-danger)", fontSize: "13px" }}>{error}</p>}

      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
        <thead>
          <tr style={{ textAlign: "left", borderBottom: "1px solid var(--border)" }}>
            <th style={{ padding: "6px 4px" }}>Name</th>
            <th style={{ padding: "6px 4px" }}>Path</th>
            <th style={{ padding: "6px 4px" }}>Type</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {roots.map((r) =>
            editingId === r.id ? (
              <tr key={r.id} style={{ borderBottom: "1px solid var(--border)" }}>
                <td style={{ padding: "6px 4px" }}>
                  <input value={editName} onChange={(e) => setEditName(e.target.value)} style={{ ...inputStyle, width: "100%" }} />
                </td>
                <td style={{ padding: "6px 4px" }}>
                  <PathInput value={editPath} onChange={setEditPath} style={{ width: "100%" }} />
                </td>
                <td style={{ padding: "6px 4px" }}>
                  <select value={editMediaType} onChange={(e) => setEditMediaType(e.target.value as MediaKind)} style={inputStyle}>
                    <option value="Movie">Movie</option>
                    <option value="Show">Show</option>
                    <option value="Anime">Anime</option>
                  </select>
                </td>
                <td style={{ padding: "6px 4px", whiteSpace: "nowrap" }}>
                  <button onClick={() => handleSaveEdit(r.id)} style={{ ...buttonStyle, padding: "4px 8px", fontSize: "12px", marginRight: "4px" }}>
                    Save
                  </button>
                  <button
                    onClick={() => setEditingId(null)}
                    style={{ border: "none", background: "none", color: "var(--text-secondary)", cursor: "pointer", fontSize: "12px" }}
                  >
                    Cancel
                  </button>
                </td>
              </tr>
            ) : (
              <tr key={r.id} style={{ borderBottom: "1px solid var(--border)" }}>
                <td style={{ padding: "6px 4px", color: "var(--text-primary)" }}>{r.name}</td>
                <td style={{ padding: "6px 4px", color: "var(--text-secondary)" }}>{r.path}</td>
                <td style={{ padding: "6px 4px", color: "var(--text-secondary)" }}>{r.mediaType}</td>
                <td style={{ padding: "6px 4px", whiteSpace: "nowrap" }}>
                  <button
                    onClick={() => startEdit(r)}
                    style={{ border: "none", background: "none", color: "var(--color-primary)", cursor: "pointer", fontSize: "12px", marginRight: "10px" }}
                  >
                    Rename
                  </button>
                  <DeleteXButton onClick={() => handleDelete(r)} title="Remove source root" />
                </td>
              </tr>
            )
          )}
          {roots.length === 0 && (
            <tr>
              <td colSpan={4} style={{ padding: "12px 4px", color: "var(--text-muted)" }}>
                No source roots registered yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
