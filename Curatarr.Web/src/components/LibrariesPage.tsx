import { useEffect, useState } from "react";
import { api, VirtualLibrary, MediaKind, AppUser, LibraryMember, MembershipRole } from "../api/client";
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

export function LibrariesPage({ onChanged }: { onChanged?: () => void }) {
  const [libraries, setLibraries] = useState<VirtualLibrary[]>([]);
  const [name, setName] = useState("");
  const [outputPath, setOutputPath] = useState("");
  const [mediaType, setMediaType] = useState<MediaKind>("Movie");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState("");
  const [editOutputPath, setEditOutputPath] = useState("");
  const [editMediaType, setEditMediaType] = useState<MediaKind>("Movie");
  const [expandedMembersId, setExpandedMembersId] = useState<string | null>(null);

  function refresh() {
    api.listAllLibraries().then(setLibraries).catch((e) => setError(e.message));
  }

  useEffect(refresh, []);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await api.createLibrary(name, mediaType, outputPath);
      setName("");
      setOutputPath("");
      refresh();
      onChanged?.();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete(lib: VirtualLibrary) {
    if (!confirm(`Delete library "${lib.name}"? Its approvals will be removed too (files on disk aren't touched until you Sync).`)) return;
    try {
      await api.deleteLibrary(lib.id);
      refresh();
      onChanged?.();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  function startEdit(lib: VirtualLibrary) {
    setEditingId(lib.id);
    setEditName(lib.name);
    setEditOutputPath(lib.outputPath);
    setEditMediaType(lib.mediaType);
  }

  async function handleSaveEdit(id: string) {
    const lib = libraries.find((l) => l.id === id);
    if (!lib) return;

    const pathChanged = lib.outputPath !== editOutputPath;

    // Prompt 1: warn BEFORE changing the target path, since the existing
    // links will need to be relocated to the new location. Give the user a
    // clear choice to bail out (Cancel) or proceed (OK).
    if (pathChanged) {
      const ok = confirm(
        `Changing this library's target path requires updating its existing links to the new location.\n\n` +
          `Old path: ${lib.outputPath}\nNew path: ${editOutputPath}\n\n` +
          `Continue with the change?`
      );
      if (!ok) return;
    }

    try {
      await api.updateLibrary(id, editName, editMediaType, editOutputPath);
      setEditingId(null);
      refresh();
      onChanged?.();

      // Prompt 2: only after the change succeeds -- the links have moved, so
      // Plex must be re-pointed at the new location to keep resolving them.
      if (pathChanged) {
        alert(
          `Library links have been updated to the new location.\n\n` +
            `Don't forget to update your Plex library to reference the new path:\n${editOutputPath}`
        );
      }
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <div style={{ maxWidth: "760px", fontFamily: "var(--font-body)" }}>
      <h2 style={{ fontFamily: "var(--font-heading)", fontSize: "20px", color: "var(--text-primary)" }}>Libraries</h2>
      <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>
        Curated, per-audience libraries -- each gets its own virtual directory tree and its own set
        of approved titles. Expand a row's "Access" to manage who can curate it.
      </p>

      <form onSubmit={handleCreate} style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap", margin: "1rem 0", alignItems: "flex-start" }}>
        <input placeholder="Name (e.g. Kids Movies)" value={name} onChange={(e) => setName(e.target.value)} required style={{ ...inputStyle, flex: "1 1 160px" }} />
        <PathInput value={outputPath} onChange={setOutputPath} placeholder="Output path" style={{ flex: "2 1 260px" }} />
        <select value={mediaType} onChange={(e) => setMediaType(e.target.value as MediaKind)} style={inputStyle}>
          <option value="Movie">Movie</option>
          <option value="Show">Show</option>
          <option value="Anime">Anime</option>
        </select>
        <button type="submit" disabled={submitting} style={buttonStyle}>
          {submitting ? "Creating..." : "Create library"}
        </button>
      </form>

      {error && <p style={{ color: "var(--color-danger)", fontSize: "13px" }}>{error}</p>}

      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
        <thead>
          <tr style={{ textAlign: "left", borderBottom: "1px solid var(--border)" }}>
            <th style={{ padding: "6px 4px" }}>Name</th>
            <th style={{ padding: "6px 4px" }}>Type</th>
            <th style={{ padding: "6px 4px" }}>Output path</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {libraries.map((lib) =>
            editingId === lib.id ? (
              <tr key={lib.id} style={{ borderBottom: "1px solid var(--border)" }}>
                <td style={{ padding: "6px 4px" }}>
                  <input value={editName} onChange={(e) => setEditName(e.target.value)} style={{ ...inputStyle, width: "100%" }} />
                </td>
                <td style={{ padding: "6px 4px" }}>
                  <select value={editMediaType} onChange={(e) => setEditMediaType(e.target.value as MediaKind)} style={inputStyle}>
                    <option value="Movie">Movie</option>
                    <option value="Show">Show</option>
                    <option value="Anime">Anime</option>
                  </select>
                </td>
                <td style={{ padding: "6px 4px" }}>
                  <PathInput value={editOutputPath} onChange={setEditOutputPath} style={{ width: "100%" }} />
                </td>
                <td style={{ padding: "6px 4px", whiteSpace: "nowrap" }}>
                  <button onClick={() => handleSaveEdit(lib.id)} style={{ ...buttonStyle, padding: "4px 8px", fontSize: "12px", marginRight: "4px" }}>
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
              <>
                <tr key={lib.id} style={{ borderBottom: expandedMembersId === lib.id ? "none" : "1px solid var(--border)" }}>
                  <td style={{ padding: "6px 4px", color: "var(--text-primary)" }}>{lib.name}</td>
                  <td style={{ padding: "6px 4px", color: "var(--text-secondary)" }}>{lib.mediaType}</td>
                  <td style={{ padding: "6px 4px", color: "var(--text-secondary)" }}>{lib.outputPath}</td>
                  <td style={{ padding: "6px 4px", whiteSpace: "nowrap" }}>
                    <button
                      onClick={() => setExpandedMembersId(expandedMembersId === lib.id ? null : lib.id)}
                      style={{ border: "none", background: "none", color: "var(--color-primary)", cursor: "pointer", fontSize: "12px", marginRight: "8px" }}
                    >
                      {expandedMembersId === lib.id ? "Hide access" : "Access"}
                    </button>
                    <button
                      onClick={() => startEdit(lib)}
                      style={{ border: "none", background: "none", color: "var(--color-primary)", cursor: "pointer", fontSize: "12px", marginRight: "10px" }}
                    >
                      Rename
                    </button>
                    <DeleteXButton onClick={() => handleDelete(lib)} title="Delete library" />
                  </td>
                </tr>
                {expandedMembersId === lib.id && (
                  <tr style={{ borderBottom: "1px solid var(--border)" }}>
                    <td colSpan={4} style={{ padding: "0 4px 12px" }}>
                      <LibraryAccessPanel libraryId={lib.id} />
                    </td>
                  </tr>
                )}
              </>
            )
          )}
          {libraries.length === 0 && (
            <tr>
              <td colSpan={4} style={{ padding: "12px 4px", color: "var(--text-muted)" }}>
                No libraries yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

function LibraryAccessPanel({ libraryId }: { libraryId: string }) {
  const [users, setUsers] = useState<AppUser[]>([]);
  const [members, setMembers] = useState<LibraryMember[]>([]);
  const [addUserId, setAddUserId] = useState("");
  const [addRole, setAddRole] = useState<MembershipRole>("Curator");
  const [error, setError] = useState<string | null>(null);

  function refresh() {
    api.listLibraryMembers(libraryId).then(setMembers).catch((e) => setError(e.message));
  }

  useEffect(() => {
    api.listUsers().then(setUsers).catch((e) => setError(e.message));
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [libraryId]);

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    if (!addUserId) return;
    try {
      await api.addMember(libraryId, addUserId, addRole);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function handleRoleChange(userId: string, role: MembershipRole) {
    try {
      await api.updateMemberRole(libraryId, userId, role);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function handleRemove(userId: string, email: string) {
    if (!confirm(`Remove ${email}'s access to this library?`)) return;
    try {
      await api.removeMember(libraryId, userId);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  const memberIds = new Set(members.map((m) => m.userId));
  const availableUsers = users.filter((u) => !memberIds.has(u.id));

  return (
    <div style={{ background: "var(--surface-raised)", borderRadius: "var(--radius)", padding: "10px 12px", fontSize: "13px" }}>
      <table style={{ width: "100%", borderCollapse: "collapse", marginBottom: "0.5rem" }}>
        <tbody>
          {members.map((m) => (
            <tr key={m.userId}>
              <td style={{ padding: "4px 4px", color: "var(--text-primary)" }}>{m.email}</td>
              <td style={{ padding: "4px 4px" }}>
                <select value={m.role} onChange={(e) => handleRoleChange(m.userId, e.target.value as MembershipRole)} style={inputStyle}>
                  <option value="Owner">Owner</option>
                  <option value="Curator">Curator</option>
                  <option value="Viewer">Viewer</option>
                </select>
              </td>
              <td style={{ padding: "4px 4px" }}>
                <DeleteXButton onClick={() => handleRemove(m.userId, m.email)} title="Remove access" />
              </td>
            </tr>
          ))}
          {members.length === 0 && (
            <tr>
              <td colSpan={3} style={{ padding: "4px", color: "var(--text-muted)" }}>
                No members yet.
              </td>
            </tr>
          )}
        </tbody>
      </table>
      <form onSubmit={handleAdd} style={{ display: "flex", gap: "0.5rem", flexWrap: "wrap" }}>
        <select value={addUserId} onChange={(e) => setAddUserId(e.target.value)} style={{ ...inputStyle, flex: "1 1 180px" }}>
          <option value="">Select a user...</option>
          {availableUsers.map((u) => (
            <option key={u.id} value={u.id}>
              {u.email}
            </option>
          ))}
        </select>
        <select value={addRole} onChange={(e) => setAddRole(e.target.value as MembershipRole)} style={inputStyle}>
          <option value="Owner">Owner</option>
          <option value="Curator">Curator</option>
          <option value="Viewer">Viewer</option>
        </select>
        <button type="submit" disabled={!addUserId} style={{ ...buttonStyle, padding: "4px 10px", fontSize: "12px" }}>
          Add
        </button>
      </form>
      {error && <p style={{ color: "var(--color-danger)", fontSize: "12px", marginTop: "6px" }}>{error}</p>}
    </div>
  );
}
