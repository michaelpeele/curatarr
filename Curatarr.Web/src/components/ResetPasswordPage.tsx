import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { api } from "../api/client";

const inputStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "14px",
  padding: "8px 10px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--border)",
  background: "var(--surface-card)",
  color: "var(--text-primary)",
};
const buttonStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "14px",
  padding: "8px 10px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--color-primary)",
  background: "var(--color-primary)",
  color: "var(--text-on-accent)",
  cursor: "pointer",
};

export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const email = params.get("email") ?? "";
  const token = params.get("token") ?? "";
  const [newPassword, setNewPassword] = useState("");
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await api.resetPassword(email, token, newPassword);
      setDone(true);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div style={{ minHeight: "100vh", background: "var(--surface-page)", display: "flex", alignItems: "center", justifyContent: "center" }}>
      <div style={{ maxWidth: "320px", width: "100%", padding: "0 1rem", fontFamily: "var(--font-body)" }}>
        <h1 style={{ fontFamily: "var(--font-heading)", fontSize: "24px", color: "var(--text-primary)", marginBottom: "1rem" }}>
          Reset password
        </h1>
        {!email || !token ? (
          <p style={{ color: "var(--color-danger)", fontSize: "13px" }}>
            This link looks incomplete. Request a new password reset and use the link from the email exactly as sent.
          </p>
        ) : done ? (
          <p style={{ fontSize: "13px", color: "var(--text-primary)" }}>
            Password updated. You can go back and sign in with your new password.
          </p>
        ) : (
          <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
            <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>Setting a new password for {email}</p>
            <input
              type="password"
              placeholder="New password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              required
              style={inputStyle}
            />
            {error && <p style={{ color: "var(--color-danger)", fontSize: "13px", margin: 0 }}>{error}</p>}
            <button type="submit" disabled={submitting} style={buttonStyle}>
              {submitting ? "Saving..." : "Set new password"}
            </button>
          </form>
        )}
      </div>
    </div>
  );
}
