import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { api } from "../api/client";
import { useToast } from "../context/ToastContext";

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

export function AcceptInvitePage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const { showToast } = useToast();
  const userId = params.get("userId") ?? "";
  const token = params.get("token") ?? "";

  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [wantsAlternateEmail, setWantsAlternateEmail] = useState(false);
  const [alternateEmail, setAlternateEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      const result = await api.acceptInvite(
        userId,
        token,
        password,
        displayName,
        wantsAlternateEmail ? alternateEmail : undefined
      );
      // ToastProvider sits above this route, so the success message survives
      // the navigation; App's cold boot then picks up the new account (or the
      // signed-out login screen if auto-login fails).
      showToast(`${result.message} Sign in with ${result.email}.`, "success");
      try {
        await api.login(result.email, password);
      } catch {
        showToast(`Account ready. Sign in with ${result.email}.`, "info");
      }
      navigate("/", { replace: true });
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div style={{ minHeight: "100vh", background: "var(--surface-page)", display: "flex", alignItems: "center", justifyContent: "center" }}>
      <div style={{ maxWidth: "360px", width: "100%", padding: "0 1rem", fontFamily: "var(--font-body)" }}>
        <h1 style={{ fontFamily: "var(--font-heading)", fontSize: "24px", color: "var(--text-primary)", marginBottom: "1rem" }}>
          Welcome to Curatarr
        </h1>
        {!userId || !token ? (
          <p style={{ color: "var(--color-danger)", fontSize: "13px" }}>
            This invite link looks incomplete. Ask whoever invited you to send a new one.
          </p>
        ) : (
          <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
            <input
              placeholder="Your name"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              style={inputStyle}
            />
            <input
              type="password"
              placeholder="Choose a password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              style={inputStyle}
            />
            <label style={{ fontSize: "13px", color: "var(--text-secondary)", display: "flex", alignItems: "center", gap: "6px" }}>
              <input type="checkbox" checked={wantsAlternateEmail} onChange={(e) => setWantsAlternateEmail(e.target.checked)} />
              Use a different email than the one this invite was sent to
            </label>
            {wantsAlternateEmail && (
              <input
                type="email"
                placeholder="Preferred email"
                value={alternateEmail}
                onChange={(e) => setAlternateEmail(e.target.value)}
                required
                style={inputStyle}
              />
            )}
            {error && <p style={{ color: "var(--color-danger)", fontSize: "13px", margin: 0 }}>{error}</p>}
            <button type="submit" disabled={submitting} style={buttonStyle}>
              {submitting ? "Setting up..." : "Finish setting up account"}
            </button>
          </form>
        )}
      </div>
    </div>
  );
}
