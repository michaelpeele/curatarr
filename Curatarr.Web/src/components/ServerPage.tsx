import { useEffect, useState } from "react";
import { api } from "../api/client";

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

export function ServerPage() {
  const [timeZone, setTimeZone] = useState("");
  const [zones, setZones] = useState<string[]>([]);
  const [nowInZone, setNowInZone] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api
      .getScheduleSettings()
      .then((d) => {
        setTimeZone(d.timeZone);
        setZones(d.zones);
        setNowInZone(d.nowInZone);
      })
      .catch((e) => setError((e as Error).message))
      .finally(() => setLoading(false));
  }, []);

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setMessage(null);
    setError(null);
    try {
      const res = await api.setScheduleSettings(timeZone);
      setMessage(res.message);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <div style={{ fontFamily: "var(--font-body)", fontSize: "14px", color: "var(--text-muted)" }}>
        Loading server settings…
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
          marginBottom: "16px",
        }}
      >
        Server Settings
      </h2>

      <form onSubmit={handleSave} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
        <div style={{ ...cardStyle, display: "flex", flexDirection: "column", gap: "8px" }}>
          <label
            style={{ ...labelStyle, display: "flex", justifyContent: "space-between", alignItems: "center" }}
            htmlFor="tz-select"
          >
            <span>Timezone</span>
            <span style={{ fontSize: "11px", fontWeight: 400, textTransform: "none", letterSpacing: 0 }}>
              {timeZone ? timeZone : "Server local (not configured)"}
            </span>
          </label>

          <select
            id="tz-select"
            value={timeZone}
            onChange={(e) => setTimeZone(e.target.value)}
            style={{ ...inputStyle, width: "100%" }}
          >
            <option value="">Server local (not configured)</option>
            {zones.map((z) => (
              <option key={z} value={z}>
                {z}
              </option>
            ))}
          </select>

          {nowInZone && (
            <div style={{ fontFamily: "var(--font-body)", fontSize: "12px", color: "var(--text-muted)" }}>
              Current time in zone: {nowInZone}
            </div>
          )}
        </div>

        <div style={{ display: "flex", gap: "12px", alignItems: "center" }}>
          <button
            type="submit"
            disabled={saving}
            style={{
              ...buttonStyle,
              opacity: saving ? 0.6 : 1,
            }}
          >
            {saving ? "Saving…" : "Save"}
          </button>

          {message && (
            <span style={{ fontFamily: "var(--font-body)", fontSize: "12px", color: "var(--color-primary)" }}>
              {message}
            </span>
          )}
        </div>

        {error && (
          <div style={{ fontFamily: "var(--font-body)", fontSize: "12px", color: "var(--color-danger, #e53e3e)" }}>
            {error}
          </div>
        )}

        <p style={{ fontFamily: "var(--font-body)", fontSize: "12px", color: "var(--text-muted)", margin: 0 }}>
          The timezone drives library schedule evaluation. Log timestamps are always UTC. Restart the API container
          after changing the zone so the schedule engine picks it up.
        </p>
      </form>
    </div>
  );
}
