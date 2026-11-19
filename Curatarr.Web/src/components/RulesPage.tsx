import { useEffect, useState } from "react";
import { api, MembershipRole, VirtualLibrary } from "../api/client";
import { LibraryRulesPanel } from "./LibraryRulesPanel";

type LibraryWithRole = VirtualLibrary & { myRole?: MembershipRole };

/** Library picker + shared rule panel, reachable by admins and library curators. */
export function RulesPage({ isAdmin }: { isAdmin: boolean }) {
  const [libraries, setLibraries] = useState<LibraryWithRole[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const load = isAdmin ? api.listAllLibraries() : api.listMyLibraries();
    load
      .then((libs) => {
        const typed = libs as LibraryWithRole[];
        setLibraries(typed);
        setSelectedId((current) =>
          typed.some((l) => l.id === current) ? current : (typed[0]?.id ?? null)
        );
      })
      .catch((e) => setError(e.message));
  }, [isAdmin]);

  const selected = libraries.find((l) => l.id === selectedId) ?? null;
  const canEdit = !!selected && (isAdmin || selected.myRole === "Owner" || selected.myRole === "Curator");

  return (
    <div style={{ maxWidth: "860px", fontFamily: "var(--font-body)" }}>
      <h2 style={{ fontFamily: "var(--font-heading)", fontSize: "20px", color: "var(--text-primary)" }}>
        Automatic rules
      </h2>
      <p style={{ fontSize: "13px", color: "var(--text-secondary)" }}>
        Approve or hide titles for one library from its metadata. Manual marks and hides always win,
        and rules never sync links by themselves — press Sync after applying.
      </p>
      {error && <p style={{ color: "var(--color-danger)", fontSize: "13px" }}>{error}</p>}

      {libraries.length === 0 ? (
        <p style={{ color: "var(--text-muted)", fontSize: "13px" }}>No libraries available.</p>
      ) : (
        <>
          <label style={{ display: "flex", flexDirection: "column", gap: "4px", maxWidth: "320px", marginBottom: "0.75rem" }}>
            <span style={{ color: "var(--text-secondary)", fontSize: "12px" }}>Library</span>
            <select
              value={selectedId ?? ""}
              onChange={(e) => setSelectedId(e.target.value)}
              style={{
                fontFamily: "var(--font-body)", fontSize: "13px", padding: "6px 8px",
                borderRadius: "var(--radius)", border: "1px solid var(--border)",
                background: "var(--surface-card)", color: "var(--text-primary)",
              }}
            >
              {libraries.map((l) => (
                <option key={l.id} value={l.id}>{l.name} ({l.mediaType})</option>
              ))}
            </select>
          </label>

          {selected && (
            <>
              {!canEdit && (
                <p style={{ color: "var(--text-muted)", fontSize: "12px" }}>
                  You have view-only access to this library.
                </p>
              )}
              <LibraryRulesPanel
                key={selected.id}
                libraryId={selected.id}
                mediaType={selected.mediaType}
                canEdit={canEdit}
              />
            </>
          )}
        </>
      )}
    </div>
  );
}
