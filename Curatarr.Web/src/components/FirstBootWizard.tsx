import { useState } from "react";
import { api, MediaKind } from "../api/client";

// ─── Types ──────────────────────────────────────────────────────────────

interface WizardState {
  step: number;
  adminUsername: string;
  adminPassword: string;
  adminDisplayName: string;
  // One media type chosen up front (Sprint 4 #1). Every source root and the
  // first virtual library inherit it, so it is never asked for twice.
  mediaType: MediaKind;
  sourceRoots: { name: string; path: string }[];
  pathMappings: { fromPrefix: string; toPrefix: string }[];
  libraryName: string;
  libraryOutputPath: string;
}

const initial: WizardState = {
  step: 1,
  adminUsername: "",
  adminPassword: "",
  adminDisplayName: "",
  mediaType: "Movie",
  sourceRoots: [{ name: "", path: "" }],
  pathMappings: [{ fromPrefix: "", toPrefix: "" }],
  libraryName: "My Library",
  libraryOutputPath: "",
};

// ─── Styles ─────────────────────────────────────────────────────────────

const overlayStyle: React.CSSProperties = {
  position: "fixed",
  inset: 0,
  background: "var(--surface-page)",
  zIndex: 1000,
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
  fontFamily: "var(--font-body)",
};

const cardStyle: React.CSSProperties = {
  background: "var(--surface-card)",
  border: "1px solid var(--border)",
  borderRadius: "var(--radius-card)",
  padding: "32px 36px",
  width: "540px",
  maxHeight: "88vh",
  overflowY: "auto",
  boxShadow: "0 12px 40px rgba(0,0,0,0.18)",
};

const titleStyle: React.CSSProperties = {
  fontFamily: "var(--font-heading)",
  fontSize: "22px",
  color: "var(--text-primary)",
  marginBottom: "4px",
};

const subtitleStyle: React.CSSProperties = {
  fontSize: "13px",
  color: "var(--text-secondary)",
  marginBottom: "20px",
  lineHeight: 1.5,
};

const labelStyle: React.CSSProperties = {
  fontSize: "13px",
  color: "var(--text-secondary)",
  marginBottom: "4px",
  display: "block",
};

const inputStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  padding: "8px 10px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--border)",
  background: "var(--surface-raised)",
  color: "var(--text-primary)",
  width: "100%",
  boxSizing: "border-box",
  marginBottom: "14px",
};

const selectStyle: React.CSSProperties = {
  ...inputStyle,
  appearance: "auto" as const,
};

const rowStyle: React.CSSProperties = {
  display: "flex",
  gap: "8px",
  alignItems: "flex-end",
};

const buttonStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  fontWeight: 500,
  padding: "8px 16px",
  borderRadius: "var(--radius)",
  border: "none",
  cursor: "pointer",
};

const primaryBtn: React.CSSProperties = {
  ...buttonStyle,
  background: "var(--color-primary)",
  color: "var(--text-on-accent)",
};

const secondaryBtn: React.CSSProperties = {
  ...buttonStyle,
  background: "transparent",
  color: "var(--text-secondary)",
  border: "1px solid var(--border)",
};

const removeBtn: React.CSSProperties = {
  ...buttonStyle,
  background: "transparent",
  color: "var(--color-danger)",
  border: "1px solid var(--color-danger)",
  fontSize: "12px",
  padding: "6px 10px",
};

const stepIndicatorStyle: React.CSSProperties = {
  display: "flex",
  gap: "10px",
  marginBottom: "24px",
  flexWrap: "wrap",
};

const dotStyle = (active: boolean): React.CSSProperties => ({
  width: "10px",
  height: "10px",
  borderRadius: "50%",
  background: active ? "var(--color-primary)" : "var(--border)",
  transition: "background 0.2s",
});

const errorStyle: React.CSSProperties = {
  background: "rgba(220,50,50,0.1)",
  border: "1px solid var(--color-danger)",
  borderRadius: "var(--radius)",
  padding: "10px 14px",
  fontSize: "13px",
  color: "var(--color-danger)",
  marginBottom: "14px",
};

const phaseLabelStyle = (active: boolean): React.CSSProperties => ({
  fontSize: "11px",
  color: active ? "var(--text-primary)" : "var(--text-muted)",
  fontWeight: active ? 500 : 400,
});

// ─── Component ──────────────────────────────────────────────────────────

export function FirstBootWizard({ onComplete }: { onComplete: () => void }) {
  const [wiz, setWiz] = useState<WizardState>(initial);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const update = (patch: Partial<WizardState>) => setWiz((s) => ({ ...s, ...patch }));

  const steps = ["Account", "Sources", "Paths", "Library"];

  // ── Validation ────────────────────────────────────────────────────────
  const canProceed = (): boolean => {
    switch (wiz.step) {
      case 1:
        return (
          wiz.adminUsername.trim().length > 0 &&
          wiz.adminPassword.length >= 8 &&
          wiz.adminDisplayName.trim().length > 0
        );
      case 2:
        return wiz.sourceRoots.some((s) => s.name.trim() !== "" && s.path.trim() !== "");
      case 3:
        return wiz.pathMappings.some((p) => p.fromPrefix.trim() !== "" && p.toPrefix.trim() !== "");
      case 4:
        return wiz.libraryName.trim().length > 0 && wiz.libraryOutputPath.trim().length > 0;
      default:
        return true;
    }
  };

  // ── Submit ────────────────────────────────────────────────────────────
  const handleSubmit = async () => {
    setLoading(true);
    setError("");

    try {
      await api.bootstrap({
        adminUsername: wiz.adminUsername.trim(),
        adminPassword: wiz.adminPassword,
        adminDisplayName: wiz.adminDisplayName.trim(),
        sourceRoots: wiz.sourceRoots.filter((s) => s.name.trim() && s.path.trim()),
        pathMappings: wiz.pathMappings.filter((p) => p.fromPrefix.trim() && p.toPrefix.trim()),
        libraryName: wiz.libraryName.trim(),
        libraryOutputPath: wiz.libraryOutputPath.trim(),
        mediaType: wiz.mediaType,
      });

      // Auto-login as the brand new admin (username == email).
      await api.login(wiz.adminUsername.trim(), wiz.adminPassword);
      onComplete();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  };

  // ── Render ────────────────────────────────────────────────────────────
  return (
    <div style={overlayStyle}>
      <div style={cardStyle}>
        {/* Step indicator */}
        <div style={stepIndicatorStyle}>
          {steps.map((label, i) => (
            <div key={label} style={{ display: "flex", alignItems: "center", gap: "4px" }}>
              <div style={dotStyle(wiz.step === i + 1)} />
              <span style={phaseLabelStyle(wiz.step === i + 1)}>{label}</span>
            </div>
          ))}
        </div>

        {error && <div style={errorStyle}>{error}</div>}

        {/* ── Step 1: Admin Account ───────────────────────────────────── */}
        {wiz.step === 1 && (
          <>
            <h2 style={titleStyle}>Welcome to Curatarr</h2>
            <p style={subtitleStyle}>This is the first boot — create your administrator account to get started.</p>

            <label style={labelStyle}>Username (also used to sign in)</label>
            <input
              style={inputStyle}
              value={wiz.adminUsername}
              onChange={(e) => update({ adminUsername: e.target.value })}
              placeholder="e.g. admin"
              autoFocus
            />

            <label style={labelStyle}>Display name</label>
            <input
              style={inputStyle}
              value={wiz.adminDisplayName}
              onChange={(e) => update({ adminDisplayName: e.target.value })}
              placeholder="e.g. Admin"
            />

            <label style={labelStyle}>Password (min 8 characters)</label>
            <input
              style={inputStyle}
              type="password"
              value={wiz.adminPassword}
              onChange={(e) => update({ adminPassword: e.target.value })}
              placeholder="••••••••"
            />

            <label style={labelStyle}>Media type</label>
            <select
              style={selectStyle}
              value={wiz.mediaType}
              onChange={(e) => update({ mediaType: e.target.value as MediaKind })}
            >
              <option value="Movie">Movie</option>
              <option value="Show">Show</option>
              <option value="Anime">Anime</option>
            </select>
            <p style={{ ...subtitleStyle, marginTop: "-6px", marginBottom: "0px" }}>
              Your source roots and first library will use this type. You can add other types later.
            </p>
          </>
        )}

        {/* ── Step 2: Source Roots ─────────────────────────────────────── */}
        {wiz.step === 2 && (
          <>
            <h2 style={titleStyle}>Source roots</h2>
            <p style={subtitleStyle}>
              Where does your media live <em>inside the container</em>? Use mounted paths, e.g.{" "}
              <code>/media/Movies</code> — not host paths like <code>/mnt/media</code>.
            </p>

            {wiz.sourceRoots.map((sr, i) => (
              <div key={i} style={rowStyle}>
                <div style={{ flex: 1 }}>
                  <label style={labelStyle}>Name</label>
                  <input
                    style={inputStyle}
                    value={sr.name}
                    onChange={(e) => {
                      const roots = [...wiz.sourceRoots];
                      roots[i] = { ...roots[i], name: e.target.value };
                      update({ sourceRoots: roots });
                    }}
                    placeholder="Movies"
                  />
                </div>
                <div style={{ flex: 2 }}>
                  <label style={labelStyle}>Container path</label>
                  <input
                    style={inputStyle}
                    value={sr.path}
                    onChange={(e) => {
                      const roots = [...wiz.sourceRoots];
                      roots[i] = { ...roots[i], path: e.target.value };
                      update({ sourceRoots: roots });
                    }}
                    placeholder="/media/Movies"
                  />
                </div>
                {wiz.sourceRoots.length > 1 && (
                  <button
                    style={{ ...removeBtn, marginBottom: "14px" }}
                    onClick={() => update({ sourceRoots: wiz.sourceRoots.filter((_, j) => j !== i) })}
                  >
                    ✕
                  </button>
                )}
              </div>
            ))}

            <button
              style={{ ...secondaryBtn, marginTop: "2px" }}
              onClick={() => update({ sourceRoots: [...wiz.sourceRoots, { name: "", path: "" }] })}
            >
              + Add another source root
            </button>
          </>
        )}

        {/* ── Step 3: Path Mappings ────────────────────────────────────── */}
        {wiz.step === 3 && (
          <>
            <h2 style={titleStyle}>Path mappings</h2>
            <p style={subtitleStyle}>
              Translate container paths to what your media player (Plex, Jellyfin, etc.) sees. Example:{" "}
              <code>/media</code> → <code>/mnt/media</code>. Optional if you don't use symlinks.
            </p>

            {wiz.pathMappings.map((pm, i) => (
              <div key={i} style={rowStyle}>
                <div style={{ flex: 1 }}>
                  <label style={labelStyle}>From (Curatarr's view)</label>
                  <input
                    style={inputStyle}
                    value={pm.fromPrefix}
                    onChange={(e) => {
                      const mappings = [...wiz.pathMappings];
                      mappings[i] = { ...mappings[i], fromPrefix: e.target.value };
                      update({ pathMappings: mappings });
                    }}
                    placeholder="/media"
                  />
                </div>
                <div style={{ flex: 1 }}>
                  <label style={labelStyle}>To (Plex / player's view)</label>
                  <input
                    style={inputStyle}
                    value={pm.toPrefix}
                    onChange={(e) => {
                      const mappings = [...wiz.pathMappings];
                      mappings[i] = { ...mappings[i], toPrefix: e.target.value };
                      update({ pathMappings: mappings });
                    }}
                    placeholder="/mnt/media"
                  />
                </div>
                {wiz.pathMappings.length > 1 && (
                  <button
                    style={{ ...removeBtn, marginBottom: "14px" }}
                    onClick={() => update({ pathMappings: wiz.pathMappings.filter((_, j) => j !== i) })}
                  >
                    ✕
                  </button>
                )}
              </div>
            ))}

            <button
              style={{ ...secondaryBtn, marginTop: "2px" }}
              onClick={() => update({ pathMappings: [...wiz.pathMappings, { fromPrefix: "", toPrefix: "" }] })}
            >
              + Add another mapping
            </button>
          </>
        )}

        {/* ── Step 4: Virtual Library ──────────────────────────────────── */}
        {wiz.step === 4 && (
          <>
            <h2 style={titleStyle}>Virtual library</h2>
            <p style={subtitleStyle}>
              Create your first virtual library — the folder where Curatarr builds the symlink tree for your
              player. You can add more libraries later from the Libraries page.
            </p>

            <label style={labelStyle}>Library name</label>
            <input
              style={inputStyle}
              value={wiz.libraryName}
              onChange={(e) => update({ libraryName: e.target.value })}
              placeholder="Movies"
            />

            <label style={labelStyle}>Output path (inside the container)</label>
            <input
              style={inputStyle}
              value={wiz.libraryOutputPath}
              onChange={(e) => update({ libraryOutputPath: e.target.value })}
              placeholder="/output/VirtualLibraries/Movies"
            />
            <p style={{ ...subtitleStyle, marginTop: "4px", marginBottom: "0px" }}>
              This library will use the <strong>{wiz.mediaType}</strong> media type chosen in step 1.
            </p>
          </>
        )}

        {/* ── Navigation ───────────────────────────────────────────────── */}
        <div style={{ display: "flex", justifyContent: "space-between", marginTop: "24px" }}>
          {wiz.step > 1 ? (
            <button style={secondaryBtn} onClick={() => update({ step: wiz.step - 1 })}>
              ← Back
            </button>
          ) : (
            <div />
          )}

          {wiz.step < 4 ? (
            <button
              style={primaryBtn}
              disabled={!canProceed()}
              onClick={() => update({ step: wiz.step + 1 })}
            >
              Next →
            </button>
          ) : (
            <button
              style={{ ...primaryBtn, opacity: loading ? 0.6 : 1 }}
              disabled={loading || !canProceed()}
              onClick={handleSubmit}
            >
              {loading ? "Setting up..." : "Finish & start →"}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}