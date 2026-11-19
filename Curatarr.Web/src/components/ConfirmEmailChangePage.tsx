import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { api } from "../api/client";

export function ConfirmEmailChangePage() {
  const [params] = useSearchParams();
  const userId = params.get("userId") ?? "";
  const newEmail = params.get("newEmail") ?? "";
  const token = params.get("token") ?? "";
  const [status, setStatus] = useState<"working" | "done" | "error">("working");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!userId || !newEmail || !token) {
      setStatus("error");
      setError("This confirmation link looks incomplete.");
      return;
    }
    api
      .confirmEmailChange(userId, newEmail, token)
      .then(() => setStatus("done"))
      .catch((e) => {
        setStatus("error");
        setError(e.message);
      });
  }, [userId, newEmail, token]);

  return (
    <div style={{ minHeight: "100vh", background: "var(--surface-page)", display: "flex", alignItems: "center", justifyContent: "center" }}>
      <div style={{ maxWidth: "360px", width: "100%", padding: "0 1rem", fontFamily: "var(--font-body)", textAlign: "center" }}>
        <h1 style={{ fontFamily: "var(--font-heading)", fontSize: "24px", color: "var(--text-primary)", marginBottom: "1rem" }}>
          Confirm email
        </h1>
        {status === "working" && <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>Confirming...</p>}
        {status === "done" && (
          <p style={{ fontSize: "13px", color: "var(--text-primary)" }}>
            Confirmed -- {newEmail} is now your login email.
          </p>
        )}
        {status === "error" && <p style={{ fontSize: "13px", color: "var(--color-danger)" }}>{error}</p>}
      </div>
    </div>
  );
}
