import { useEffect, useState } from "react";
import { api, setUnauthorizedHandler, Me, VirtualLibrary, MembershipRole, MediaKind } from "./api/client";
import { PosterGrid } from "./components/PosterGrid";
import { Sidebar, ViewKey } from "./components/Sidebar";
import { BottomBar } from "./components/BottomBar";
import { SourcesPage } from "./components/SourcesPage";
import { LibrariesPage } from "./components/LibrariesPage";
import { UsersPage } from "./components/UsersPage";
import { AccountPage } from "./components/AccountPage";
import { CalendarPage } from "./components/CalendarPage";
import { PathMappingsSection } from "./components/PathMappingsSection";
import { ServerPage } from "./components/ServerPage";
import { PlexPage } from "./components/PlexPage";
import { RulesPage } from "./components/RulesPage";
import { FirstBootWizard } from "./components/FirstBootWizard";
import { ActivityPage } from "./components/ActivityPage";
import { LogsPage } from "./components/LogsPage";
import { useViewport } from "./lib/useViewport";

export default function App() {
  const [me, setMe] = useState<Me | null>(null);
  const [checkingAuth, setCheckingAuth] = useState(true);
  const [mustSetup, setMustSetup] = useState<boolean | null>(null);
  // Set when a 401 kills an established session, so the login screen can
  // explain why the user is suddenly back here.
  const [sessionMessage, setSessionMessage] = useState<string | null>(null);

  // Drop the stale session on the first 401 from any non-auth endpoint.
  // Registered only while signed in -- the cold-open `api.me()` probe is
  // expected to 401 for anonymous visitors and must not show this message.
  useEffect(() => {
    if (!me) {
      setUnauthorizedHandler(null);
      return;
    }
    setUnauthorizedHandler(() => {
      setMe(null);
      setSessionMessage("Your session expired. Please sign in again.");
    });
    return () => setUnauthorizedHandler(null);
  }, [me]);

  // First, ask the API whether the app has been bootstrapped (any user exists).
  // If not, show the setup wizard instead of the login screen. Keeps
  // checkingAuth true until the session (if any) has been loaded too, so the
  // login screen never flashes on a cold open.
  useEffect(() => {
    api
      .isBootstrapped()
      .then(async (data) => {
        setMustSetup(!data.isBootstrapped);
        if (data.isBootstrapped) {
          try {
            setMe(await api.me());
          } catch {
            setMe(null);
          }
        }
      })
      .finally(() => setCheckingAuth(false));
  }, []);

  if (checkingAuth || mustSetup === null) return null;

  // First boot — no users at all, show the wizard. When it finishes it has
  // already auto-logged-in; refresh the session so the main app renders.
  if (mustSetup)
    return (
      <FirstBootWizard
        onComplete={() =>
          api.me().then(setMe).catch(() => setMe(null)).finally(() => setMustSetup(false))
        }
      />
    );

  return (
    <div
      style={{
        minHeight: "100vh",
        background: "var(--surface-page)",
        fontFamily: "var(--font-body)",
      }}
    >
      {me ? (
        <AuthedApp
          me={me}
          onLogout={() => {
            setSessionMessage(null);
            setMe(null);
          }}
        />
      ) : (
        <LoginScreen
          message={sessionMessage}
          onLogin={(user) => {
            setSessionMessage(null);
            setMe(user);
          }}
        />
      )}
    </div>
  );
}

function LoginScreen({
  onLogin,
  message,
}: {
  onLogin: (me: Me) => void;
  message?: string | null;
}) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [forgotMode, setForgotMode] = useState(false);
  const [forgotEmail, setForgotEmail] = useState("");
  const [forgotSent, setForgotSent] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await api.login(email, password);
      onLogin(await api.me());
    } catch {
      setError("Couldn't sign in. Check your email and password.");
    } finally {
      setSubmitting(false);
    }
  }

  async function handleForgot(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    try {
      await api.forgotPassword(forgotEmail);
      setForgotSent(true);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div style={{ maxWidth: "320px", margin: "80px auto", padding: "0 1rem" }}>
      <h1
        style={{
          fontFamily: "var(--font-heading)",
          fontSize: "28px",
          fontWeight: 600,
          color: "var(--text-primary)",
          marginBottom: "1.5rem",
        }}
      >
        Curatarr
      </h1>

      {message && (
        <p
          role="status"
          style={{
            fontFamily: "var(--font-body)",
            fontSize: "13px",
            margin: "0 0 1rem",
            padding: "8px 10px",
            borderRadius: "var(--radius)",
            border: "1px solid var(--border)",
            background: "var(--surface-raised)",
            color: "var(--text-primary)",
          }}
        >
          {message}
        </p>
      )}

      {forgotMode ? (
        forgotSent ? (
          <div>
            <p style={{ fontSize: "13px", color: "var(--text-primary)" }}>
              If that email has an account, a reset link was sent (dev mode: check the API console).
            </p>
            <button onClick={() => { setForgotMode(false); setForgotSent(false); }} style={{ ...buttonStyle, marginTop: "0.5rem" }}>
              Back to sign in
            </button>
          </div>
        ) : (
          <form onSubmit={handleForgot} style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
            <input
              type="email"
              placeholder="Your email"
              value={forgotEmail}
              onChange={(e) => setForgotEmail(e.target.value)}
              required
              style={inputStyle}
            />
            <button type="submit" disabled={submitting} style={buttonStyle}>
              {submitting ? "Sending..." : "Send reset link"}
            </button>
            <button
              type="button"
              onClick={() => setForgotMode(false)}
              style={{ border: "none", background: "none", color: "var(--text-secondary)", cursor: "pointer", fontSize: "12px" }}
            >
              Back to sign in
            </button>
          </form>
        )
      ) : (
        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: "0.75rem" }}>
          <input
            type="email"
            placeholder="Email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            style={inputStyle}
          />
          <input
            type="password"
            placeholder="Password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            style={inputStyle}
          />
          {error && <p style={{ color: "var(--color-danger)", fontSize: "13px", margin: 0 }}>{error}</p>}
          <button type="submit" disabled={submitting} style={buttonStyle}>
            {submitting ? "Signing in..." : "Sign in"}
          </button>
          <button
            type="button"
            onClick={() => setForgotMode(true)}
            style={{ border: "none", background: "none", color: "var(--color-primary)", cursor: "pointer", fontSize: "12px", alignSelf: "flex-start" }}
          >
            Forgot password?
          </button>
        </form>
      )}
    </div>
  );
}

function AuthedApp({ me, onLogout }: { me: Me; onLogout: () => void }) {
  const isAdmin = me.roles.includes("Admin");
  const [view, setView] = useState<ViewKey>("library");

  // Access-control hardening (Sprint 4 item 2): the Sidebar already hides
  // admin-only entries, but a stale/leftover view state (or a demoted user
  // mid-session) must not render library/source/user configuration pages.
  // Anything that isn't library/account/calendar is admin-only -- fall back
  // to the library view until the session role says otherwise.
  useEffect(() => {
    if (!isAdmin && view !== "library" && view !== "account" && view !== "calendar" && view !== "rules")
      setView("library");
  }, [isAdmin, view]);
  const { isMobile } = useViewport();

  const [libraries, setLibraries] = useState<(VirtualLibrary & { myRole?: MembershipRole })[]>([]);
  const [librariesVersion, setLibrariesVersion] = useState(0);
  const [selectedLibraryId, setSelectedLibraryId] = useState<string | null>(null);
  const [selectedLibMediaType, setSelectedLibMediaType] = useState<MediaKind | null>(null);
  useEffect(() => {
    if (!selectedLibraryId) { setSelectedLibMediaType(null); return; }
    const lib = libraries.find((l) => l.id === selectedLibraryId);
    setSelectedLibMediaType(lib ? lib.mediaType : null);
  }, [selectedLibraryId, libraries]);

  const [scanning, setScanning] = useState(false);
  const [scanResult, setScanResult] = useState<{ scanned: number; created: number; updated: number; stopped: boolean } | null>(null);
  const [scanError, setScanError] = useState<string | null>(null);
  const [posterRefreshKey, setPosterRefreshKey] = useState(0);

  const [reconciling, setReconciling] = useState(false);
  const [reconcileResult, setReconcileResult] = useState<{
    created: string[];
    removed: string[];
    broken: string[];
    unchanged: string[];
  } | null>(null);
  const [reconcileError, setReconcileError] = useState<string | null>(null);

  useEffect(() => {
    const load = isAdmin ? api.listAllLibraries() : api.listMyLibraries();
    load.then((libs) => {
      setLibraries(libs as (VirtualLibrary & { myRole?: MembershipRole })[]);
      // Calendar uses null as an intentional "All libraries" filter. Do not
      // silently switch it back to the first library after Sync/library reloads.
      const allLibrariesOnCalendar = view === "calendar" && selectedLibraryId === null;
      const stillExists = allLibrariesOnCalendar || libs.some((l) => l.id === selectedLibraryId);
      if (!stillExists) setSelectedLibraryId(libs.length > 0 ? libs[0].id : null);
    });
  }, [isAdmin, view, librariesVersion]);

  async function handleLogout() {
    try {
      await api.logout();
    } catch {
      // Already-expired session: the cookie is gone either way, so still
      // return to the login screen instead of surfacing a 401.
    }
    onLogout();
  }

  async function handleScan() {
    setScanning(true);
    setScanError(null);
    try {
      const result = await api.scan(selectedLibraryId ?? undefined);
      setScanResult(result);
      setPosterRefreshKey((k) => k + 1);
    } catch (e) {
      setScanError((e as Error).message);
    } finally {
      setScanning(false);
    }
  }

  async function handleReconcile() {
    setReconciling(true);
    setReconcileError(null);
    try {
      const result = await api.reconcile();
      setReconcileResult(result);
      // Sync changes effective hidden state server-side (LastReconciledAt moves).
      // Force the grid to refetch page 0 and reload libraries so the eye turns
      // green and confirmed hides leave All immediately, without a filter toggle.
      setPosterRefreshKey((k) => k + 1);
      setLibrariesVersion((v) => v + 1);
    } catch (e) {
      setReconcileError((e as Error).message);
    } finally {
      setReconciling(false);
    }
  }

  return (
    <div style={{ display: "flex", height: "100vh", position: "relative" }}>
      <Sidebar active={view} onChange={setView} isAdmin={isAdmin} />
      <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0, paddingBottom: isMobile ? "72px" : 0 }}>
        <main style={{ flex: 1, overflowY: "auto", padding: "1.5rem" }}>
          {view === "library" && (
            <PosterGrid
              libraryId={selectedLibraryId}
              libraryMediaType={selectedLibMediaType}
              refreshKey={posterRefreshKey}
              scanning={scanning}
              lastReconciledAt={libraries.find(l => l.id === selectedLibraryId)?.lastReconciledAt ?? null}
              isAdmin={isAdmin}
            />
          )}
          {view === "account" && <AccountPage email={me.email} />}
          {view === "sources" && <SourcesPage />}
          {view === "libraries" && <LibrariesPage onChanged={() => setLibrariesVersion((v) => v + 1)} />}
          {view === "rules" && <RulesPage isAdmin={isAdmin} />}
          {view === "users" && <UsersPage />}
          {view === "calendar" && (
            <CalendarPage
              libraries={libraries}
              selectedLibraryId={selectedLibraryId}
              onSelectLibrary={setSelectedLibraryId}
              libraryMediaType={selectedLibMediaType}
              refreshKey={posterRefreshKey}
              isAdmin={isAdmin}
            />
          )}
          {view === "settings-pathmappings" && (
            <div style={{ maxWidth: "680px" }}>
              <h2 style={{ fontFamily: "var(--font-heading)", fontSize: "20px", color: "var(--text-primary)" }}>
                Path Mappings
              </h2>
              <PathMappingsSection />
            </div>
          )}
          {view === "settings-server" && (
            <div style={{ maxWidth: "680px" }}>
              <ServerPage />
            </div>
          )}
          {view === "settings-plex" && <PlexPage />}
          {view === "settings-activity" && <ActivityPage />}
          {view === "settings-logs" && <LogsPage />}
        </main>
        <BottomBar
          libraries={libraries}
          selectedLibraryId={selectedLibraryId}
          onSelectLibrary={setSelectedLibraryId}
          allowAllLibraries={view === "calendar"}
          isAdmin={isAdmin}
          scanning={scanning}
          onScan={handleScan}
          reconciling={reconciling}
          onReconcile={handleReconcile}
          meEmail={me.email}
          onLogout={handleLogout}
          scanResult={scanResult}
          scanError={scanError}
          onDismissScan={() => {
            setScanResult(null);
            setScanError(null);
          }}
          reconcileResult={reconcileResult}
          reconcileError={reconcileError}
          onDismissReconcile={() => {
            setReconcileResult(null);
            setReconcileError(null);
          }}
        />
      </div>
    </div>
  );
}

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