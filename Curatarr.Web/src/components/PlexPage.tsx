import { useEffect, useRef, useState } from "react";
import { api, PlexStatus } from "../api/client";

const inputStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  padding: "6px 10px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--border)",
  background: "var(--surface-card)",
  color: "var(--text-primary)",
};

const buttonStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  padding: "6px 14px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--color-primary)",
  background: "var(--color-primary)",
  color: "var(--text-on-accent)",
  cursor: "pointer",
};

const dangerButtonStyle: React.CSSProperties = {
  ...buttonStyle,
  border: "1px solid var(--color-danger, #e53e3e)",
  background: "transparent",
  color: "var(--color-danger, #e53e3e)",
};

const cardStyle: React.CSSProperties = {
  background: "var(--surface-card)",
  border: "1px solid var(--border)",
  borderRadius: "var(--radius)",
  padding: "16px",
};

const labelStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "12px",
  fontWeight: 600,
  color: "var(--text-muted)",
  letterSpacing: "0.5px",
  textTransform: "uppercase",
};

const mutedStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "12px",
  color: "var(--text-muted)",
  margin: 0,
};

const delay = (ms: number) => new Promise((resolve) => window.setTimeout(resolve, ms));

export function PlexPage() {
  const [status, setStatus] = useState<PlexStatus | null>(null);
  const [serverUrl, setServerUrl] = useState("");
  const [loading, setLoading] = useState(true);
  const [linking, setLinking] = useState(false);
  const [saving, setSaving] = useState(false);
  const [unlinking, setUnlinking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [authUrl, setAuthUrl] = useState<string | null>(null);

  const mounted = useRef(true);
  const polling = useRef(false);

  useEffect(() => {
    return () => {
      mounted.current = false;
      polling.current = false;
    };
  }, []);

  function applyStatus(next: PlexStatus) {
    setStatus(next);
    setServerUrl(next.serverUrl ?? "");
  }

  useEffect(() => {
    api
      .getPlexStatus()
      .then((next) => {
        if (mounted.current) applyStatus(next);
      })
      .catch((e) => {
        if (mounted.current) setError((e as Error).message);
      })
      .finally(() => {
        if (mounted.current) setLoading(false);
      });
  }, []);

  async function handleLink() {
    setError(null);
    setMessage(null);
    setAuthUrl(null);
    setLinking(true);
    polling.current = true;

    try {
      const { pinId, authUrl: url } = await api.startPlexOAuth();
      if (!mounted.current) return;
      setAuthUrl(url);

      const popup = window.open(url, "plex-auth", "width=800,height=700");
      if (!popup) {
        setError("Your browser blocked the Plex popup. Open the link below to continue.");
      }

      // Poll for up to ~5 minutes (150 × 2s).
      for (let attempt = 0; attempt < 150 && polling.current; attempt++) {
        await delay(2000);
        if (!polling.current || !mounted.current) return;

        const result = await api.pollPlexOAuth(pinId);
        if (result.status !== "linked") continue;

        polling.current = false;
        const next = await api.getPlexStatus();
        if (!mounted.current) return;
        applyStatus(next);
        setAuthUrl(null);
        setError(null);
        setMessage(
          `Linked to Plex${result.accountName ? ` as ${result.accountName}` : ""}.`
        );
        return;
      }

      if (polling.current && mounted.current) {
        setError("Plex authorization timed out. Try linking again.");
      }
    } catch (e) {
      if (mounted.current) setError((e as Error).message);
    } finally {
      polling.current = false;
      if (mounted.current) setLinking(false);
    }
  }

  async function handleSaveServerUrl(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setMessage(null);
    setError(null);
    try {
      const next = await api.updatePlexSettings({ serverUrl });
      if (!mounted.current) return;
      applyStatus(next);
      setMessage("Server URL saved.");
    } catch (err) {
      if (mounted.current) setError((err as Error).message);
    } finally {
      if (mounted.current) setSaving(false);
    }
  }

  async function handleRefreshToggle(checked: boolean) {
    setSaving(true);
    setMessage(null);
    setError(null);
    try {
      const next = await api.updatePlexSettings({ refreshOnChange: checked });
      if (!mounted.current) return;
      applyStatus(next);
      setMessage(
        checked
          ? "Plex will refresh after Curatarr changes."
          : "Automatic Plex refresh disabled."
      );
    } catch (err) {
      if (mounted.current) setError((err as Error).message);
    } finally {
      if (mounted.current) setSaving(false);
    }
  }

  async function handleUnlink() {
    if (!window.confirm("Unlink this Plex account? Automatic refreshes will stop until you link again."))
      return;

    setUnlinking(true);
    setMessage(null);
    setError(null);
    try {
      const next = await api.unlinkPlex();
      if (!mounted.current) return;
      applyStatus(next);
      setMessage("Plex account unlinked.");
    } catch (err) {
      if (mounted.current) setError((err as Error).message);
    } finally {
      if (mounted.current) setUnlinking(false);
    }
  }

  if (loading) {
    return (
      <div style={{ fontFamily: "var(--font-body)", fontSize: "14px", color: "var(--text-muted)" }}>
        Loading Plex settings…
      </div>
    );
  }

  return (
    <div style={{ maxWidth: "680px" }}>
      <h2
        style={{
          fontFamily: "var(--font-heading)",
          fontSize: "20px",
          color: "var(--text-primary)",
          marginBottom: "8px",
        }}
      >
        Plex
      </h2>
      <p style={{ ...mutedStyle, marginBottom: "16px" }}>
        Link a Plex account so Curatarr can ask Plex to rescan the matching library sections
        after its symlinks change.
      </p>

      {error && (
        <div
          style={{
            fontFamily: "var(--font-body)",
            fontSize: "12px",
            color: "var(--color-danger, #e53e3e)",
            marginBottom: "12px",
          }}
        >
          {error}
        </div>
      )}

      {message && (
        <div
          style={{
            fontFamily: "var(--font-body)",
            fontSize: "12px",
            color: "var(--color-primary)",
            marginBottom: "12px",
          }}
        >
          {message}
        </div>
      )}

      {!status?.linked ? (
        <div style={{ ...cardStyle, display: "flex", flexDirection: "column", gap: "12px" }}>
          <div>
            <div style={labelStyle}>Plex account</div>
            <div style={{ fontFamily: "var(--font-body)", fontSize: "13px", marginTop: "4px" }}>
              Not linked
            </div>
          </div>

          <div style={{ display: "flex", gap: "12px", alignItems: "center", flexWrap: "wrap" }}>
            <button
              type="button"
              onClick={handleLink}
              disabled={linking}
              style={{ ...buttonStyle, opacity: linking ? 0.6 : 1 }}
            >
              {linking ? "Waiting for Plex…" : "Link Plex account"}
            </button>
            {linking && (
              <span style={mutedStyle}>
                Complete the sign-in in the Plex window. This page will update automatically.
              </span>
            )}
          </div>

          {authUrl && (
            <p style={mutedStyle}>
              Popup blocked?{" "}
              <a
                href={authUrl}
                target="_blank"
                rel="noreferrer"
                style={{ color: "var(--color-primary)" }}
              >
                Open the Plex authorization page
              </a>
              .
            </p>
          )}
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
          <div style={{ ...cardStyle, display: "flex", flexDirection: "column", gap: "8px" }}>
            <div style={{ display: "flex", justifyContent: "space-between", gap: "12px" }}>
              <span style={labelStyle}>Account</span>
              <span style={{ fontFamily: "var(--font-body)", fontSize: "13px" }}>
                {status.accountName ?? "Unknown"}
              </span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", gap: "12px" }}>
              <span style={labelStyle}>Server</span>
              <span style={{ fontFamily: "var(--font-body)", fontSize: "13px" }}>
                {status.serverName ?? "Unknown"}
              </span>
            </div>
          </div>

          <form
            onSubmit={handleSaveServerUrl}
            style={{ ...cardStyle, display: "flex", flexDirection: "column", gap: "8px" }}
          >
            <label style={labelStyle} htmlFor="plex-server-url">
              Server URL
            </label>
            <input
              id="plex-server-url"
              type="text"
              value={serverUrl}
              onChange={(e) => setServerUrl(e.target.value)}
              placeholder="http://192.168.1.10:32400"
              style={{ ...inputStyle, width: "100%" }}
            />
            <div style={{ display: "flex", gap: "12px", alignItems: "center" }}>
              <button
                type="submit"
                disabled={saving}
                style={{ ...buttonStyle, opacity: saving ? 0.6 : 1 }}
              >
                {saving ? "Saving…" : "Save"}
              </button>
              <span style={mutedStyle}>Filled in automatically when you link; override it if Plex is reachable at another address.</span>
            </div>
          </form>

          <div style={{ ...cardStyle, display: "flex", flexDirection: "column", gap: "12px" }}>
            <label
              style={{
                display: "flex",
                alignItems: "center",
                gap: "10px",
                fontFamily: "var(--font-body)",
                fontSize: "13px",
                color: "var(--text-primary)",
              }}
            >
              <input
                type="checkbox"
                checked={status.refreshOnChange}
                disabled={saving}
                onChange={(e) => handleRefreshToggle(e.target.checked)}
              />
              Refresh Plex after changes
            </label>

            <div style={mutedStyle}>
              Last refresh:{" "}
              {status.lastRefreshAt ? new Date(status.lastRefreshAt).toLocaleString() : "Never"}
              {status.lastRefreshStatus ? ` — ${status.lastRefreshStatus}` : ""}
            </div>

            <div>
              <button
                type="button"
                onClick={handleUnlink}
                disabled={unlinking}
                style={{ ...dangerButtonStyle, opacity: unlinking ? 0.6 : 1 }}
              >
                {unlinking ? "Unlinking…" : "Unlink"}
              </button>
            </div>
          </div>
        </div>
      )}

      <p style={{ ...mutedStyle, marginTop: "16px" }}>
        Curatarr translates each virtual library&apos;s output path through your Path Mappings,
        then refreshes the Plex section whose folder contains it. The Plex token is stored
        encrypted; it is never sent back to this browser.
      </p>
    </div>
  );
}
