import { useState } from "react";
import { api } from "../api/client";

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

export function AccountPage({ email }: { email: string }) {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    setSuccess(false);
    try {
      await api.changePassword(currentPassword, newPassword);
      setCurrentPassword("");
      setNewPassword("");
      setSuccess(true);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div style={{ maxWidth: "400px", fontFamily: "var(--font-body)" }}>
      <h2 style={{ fontFamily: "var(--font-heading)", fontSize: "20px", color: "var(--text-primary)" }}>Account</h2>
      <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>Signed in as {email}</p>

      <h3 style={{ fontFamily: "var(--font-body)", fontSize: "14px", color: "var(--text-primary)", marginTop: "1.25rem" }}>
        Change password
      </h3>
      <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: "0.5rem", maxWidth: "260px" }}>
        <input
          type="password"
          placeholder="Current password"
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          required
          style={inputStyle}
        />
        <input
          type="password"
          placeholder="New password"
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          required
          style={inputStyle}
        />
        {error && <p style={{ color: "var(--color-danger)", fontSize: "12px", margin: 0 }}>{error}</p>}
        {success && <p style={{ color: "var(--color-success)", fontSize: "12px", margin: 0 }}>Password updated.</p>}
        <button type="submit" disabled={submitting} style={{ ...buttonStyle, alignSelf: "flex-start" }}>
          {submitting ? "Saving..." : "Update password"}
        </button>
      </form>
    </div>
  );
}
