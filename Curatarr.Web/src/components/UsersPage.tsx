import { useEffect, useState } from "react";
import { api, AppUser } from "../api/client";
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

export function UsersPage() {
  const [users, setUsers] = useState<AppUser[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [currentUserEmail, setCurrentUserEmail] = useState<string | null>(null);
  const [roleBusyId, setRoleBusyId] = useState<string | null>(null);

  // add-user form (admin sets the password directly)
  const [newEmail, setNewEmail] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [newDisplayName, setNewDisplayName] = useState("");
  const [creatingUser, setCreatingUser] = useState(false);

  // invite form (emails a one-time setup link)
  const [inviteEmail, setInviteEmail] = useState("");
  const [inviting, setInviting] = useState(false);
  const [inviteMessage, setInviteMessage] = useState<string | null>(null);

  // rename display name / email
  const [editingUserId, setEditingUserId] = useState<string | null>(null);
  const [editDisplayName, setEditDisplayName] = useState("");
  const [editEmail, setEditEmail] = useState("");

  // admin-set-password
  const [passwordUserId, setPasswordUserId] = useState<string | null>(null);
  const [newUserPassword, setNewUserPassword] = useState("");

  function refreshUsers() {
    api.listUsers().then(setUsers).catch((e) => setError(e.message));
  }

  useEffect(refreshUsers, []);
  useEffect(() => {
    api.me().then((me) => setCurrentUserEmail(me.email)).catch(() => setCurrentUserEmail(null));
  }, []);

  async function handleRoleChange(u: AppUser, role: "Admin" | "Viewer") {
    setRoleBusyId(u.id);
    setError(null);
    try {
      const updated = await api.setUserRole(u.id, [role]);
      setUsers((prev) => prev.map((x) => (x.id === u.id ? { ...x, roles: updated.roles } : x)));
    } catch (err) {
      setError((err as Error).message);
      refreshUsers();
    } finally {
      setRoleBusyId(null);
    }
  }

  async function handleCreateUser(e: React.FormEvent) {
    e.preventDefault();
    setCreatingUser(true);
    setError(null);
    try {
      await api.register(newEmail, newPassword, newDisplayName || undefined);
      setNewEmail("");
      setNewPassword("");
      setNewDisplayName("");
      refreshUsers();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setCreatingUser(false);
    }
  }

  async function handleInvite(e: React.FormEvent) {
    e.preventDefault();
    setInviting(true);
    setError(null);
    setInviteMessage(null);
    try {
      const res = await api.inviteUser(inviteEmail);
      setInviteMessage(res.message ?? `Invite sent to ${inviteEmail} (dev mode: check the API console for the link).`);
      setInviteEmail("");
      refreshUsers();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setInviting(false);
    }
  }

  function startEditUser(u: AppUser) {
    setEditingUserId(u.id);
    setEditDisplayName(u.displayName ?? "");
    setEditEmail(u.email);
  }

  async function handleSaveUser(id: string) {
    try {
      await api.updateUser(id, editDisplayName, editEmail);
      setEditingUserId(null);
      refreshUsers();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function handleSetPassword(id: string) {
    try {
      await api.adminSetPassword(id, newUserPassword);
      setPasswordUserId(null);
      setNewUserPassword("");
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function handleDeleteUser(u: AppUser) {
    if (!confirm(`Delete the account for ${u.email}? This can't be undone.`)) return;
    try {
      await api.deleteUser(u.id);
      refreshUsers();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <div style={{ maxWidth: "680px", fontFamily: "var(--font-body)" }}>
      <h2 style={{ fontFamily: "var(--font-heading)", fontSize: "20px", color: "var(--text-primary)" }}>Users</h2>
      <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>
        Account management. Library access (who can curate which library) now lives on the
        Libraries page, alongside the library it applies to.
      </p>

      <div style={{ display: "flex", gap: "2rem", flexWrap: "wrap", marginTop: "1.25rem" }}>
        <div style={{ flex: "1 1 260px" }}>
          <h3 style={{ fontFamily: "var(--font-body)", fontSize: "14px", color: "var(--text-primary)" }}>
            Add user directly
          </h3>
          <form onSubmit={handleCreateUser} style={{ display: "flex", flexDirection: "column", gap: "0.5rem" }}>
            <input type="email" placeholder="Email" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} required style={inputStyle} />
            <input type="password" placeholder="Password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} required style={inputStyle} />
            <input placeholder="Display name (optional)" value={newDisplayName} onChange={(e) => setNewDisplayName(e.target.value)} style={inputStyle} />
            <button type="submit" disabled={creatingUser} style={{ ...buttonStyle, alignSelf: "flex-start" }}>
              {creatingUser ? "Adding..." : "Add user"}
            </button>
          </form>
        </div>

        <div style={{ flex: "1 1 260px" }}>
          <h3 style={{ fontFamily: "var(--font-body)", fontSize: "14px", color: "var(--text-primary)" }}>
            Invite by email
          </h3>
          <p style={{ fontSize: "12px", color: "var(--text-secondary)", marginTop: 0 }}>
            They set their own password via a one-time link.
          </p>
          <form onSubmit={handleInvite} style={{ display: "flex", flexDirection: "column", gap: "0.5rem" }}>
            <input type="email" placeholder="Email to invite" value={inviteEmail} onChange={(e) => setInviteEmail(e.target.value)} required style={inputStyle} />
            <button type="submit" disabled={inviting} style={{ ...buttonStyle, alignSelf: "flex-start" }}>
              {inviting ? "Sending..." : "Send invite"}
            </button>
          </form>
          {inviteMessage && <p style={{ fontSize: "12px", color: "var(--color-success)" }}>{inviteMessage}</p>}
        </div>
      </div>

      {error && <p style={{ color: "var(--color-danger)", fontSize: "13px", marginTop: "1rem" }}>{error}</p>}

      <h3 style={{ fontFamily: "var(--font-body)", fontSize: "14px", color: "var(--text-primary)", marginTop: "1.5rem" }}>
        All accounts
      </h3>
      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "13px" }}>
        <thead>
          <tr style={{ textAlign: "left", borderBottom: "1px solid var(--border)" }}>
            <th style={{ padding: "6px 4px" }}>Email</th>
            <th style={{ padding: "6px 4px" }}>Display name</th>
            <th style={{ padding: "6px 4px" }}>Role</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {users.map((u) => (
            <tr key={u.id} style={{ borderBottom: "1px solid var(--border)" }}>
              <td style={{ padding: "6px 4px", color: "var(--text-primary)" }}>
                {editingUserId === u.id ? (
                  <input type="email" value={editEmail} onChange={(e) => setEditEmail(e.target.value)} style={{ ...inputStyle, width: "100%" }} />
                ) : (
                  u.email
                )}
              </td>
              <td style={{ padding: "6px 4px", color: "var(--text-secondary)" }}>
                {editingUserId === u.id ? (
                  <input value={editDisplayName} onChange={(e) => setEditDisplayName(e.target.value)} style={{ ...inputStyle, width: "100%" }} />
                ) : (
                  u.displayName ?? "--"
                )}
              </td>
              <td style={{ padding: "6px 4px" }}>
                <select
                  value={u.roles?.includes("Admin") ? "Admin" : "Viewer"}
                  onChange={(e) => void handleRoleChange(u, e.target.value as "Admin" | "Viewer")}
                  disabled={roleBusyId === u.id || (u.email === currentUserEmail && !!u.roles?.includes("Admin"))}
                  title={u.email === currentUserEmail ? "You can't remove your own Admin role" : undefined}
                  style={inputStyle}
                >
                  <option value="Viewer">Viewer</option>
                  <option value="Admin">Admin</option>
                </select>
              </td>
              <td style={{ padding: "6px 4px", whiteSpace: "nowrap" }}>
                {editingUserId === u.id ? (
                  <>
                    <button onClick={() => handleSaveUser(u.id)} style={{ ...buttonStyle, padding: "4px 8px", fontSize: "12px", marginRight: "4px" }}>
                      Save
                    </button>
                    <button onClick={() => setEditingUserId(null)} style={{ border: "none", background: "none", color: "var(--text-secondary)", cursor: "pointer", fontSize: "12px" }}>
                      Cancel
                    </button>
                  </>
                ) : passwordUserId === u.id ? (
                  <>
                    <input
                      type="password"
                      placeholder="New password"
                      value={newUserPassword}
                      onChange={(e) => setNewUserPassword(e.target.value)}
                      style={{ ...inputStyle, width: "130px", marginRight: "4px" }}
                    />
                    <button onClick={() => handleSetPassword(u.id)} style={{ ...buttonStyle, padding: "4px 8px", fontSize: "12px", marginRight: "4px" }}>
                      Save
                    </button>
                    <button onClick={() => setPasswordUserId(null)} style={{ border: "none", background: "none", color: "var(--text-secondary)", cursor: "pointer", fontSize: "12px" }}>
                      Cancel
                    </button>
                  </>
                ) : (
                  <>
                    <button onClick={() => startEditUser(u)} style={{ border: "none", background: "none", color: "var(--color-primary)", cursor: "pointer", fontSize: "12px", marginRight: "10px" }}>
                      Edit
                    </button>
                    <button onClick={() => setPasswordUserId(u.id)} style={{ border: "none", background: "none", color: "var(--color-primary)", cursor: "pointer", fontSize: "12px", marginRight: "10px" }}>
                      Set password
                    </button>
                    <DeleteXButton onClick={() => handleDeleteUser(u)} title="Delete account" />
                  </>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
