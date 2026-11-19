import { useState, type ReactNode } from "react";
import { useViewport } from "../lib/useViewport";

export type ViewKey =
  | "library"
  | "calendar"
  | "account"
  | "sources"
  | "libraries"
  | "rules"
  | "users"
  | "settings-pathmappings"
  | "settings-server"
  | "settings-plex"
  | "settings-activity"
  | "settings-logs";

interface NavItem {
  key: ViewKey;
  label: string;
  icon: ReactNode;
  adminOnly?: boolean;
  comingSoon?: boolean;
}

const TOP_ITEMS: NavItem[] = [
  { key: "library", label: "Library", icon: "📚" },
  { key: "calendar", label: "Calendar", icon: "📅" },
  { key: "account", label: "Account", icon: "👤" },
  { key: "sources", label: "Sources", icon: "📁", adminOnly: true },
  { key: "libraries", label: "Libraries", icon: "📦", adminOnly: true },
  { key: "rules", label: "Rules", icon: "✨" },
  { key: "users", label: "Users", icon: "👥", adminOnly: true },
];

const SETTINGS_CHILDREN: NavItem[] = [
  { key: "settings-pathmappings", label: "Path Mappings", icon: "🔗", adminOnly: true },
  { key: "settings-server", label: "Server", icon: <ServerIcon />, adminOnly: true },
  { key: "settings-plex", label: "Plex", icon: "▶️", adminOnly: true },
  { key: "settings-activity", label: "Activity", icon: "📊", adminOnly: true },
  { key: "settings-logs", label: "Logs", icon: "📋", adminOnly: true },
];

export function Sidebar({
  active,
  onChange,
  isAdmin,
}: {
  active: ViewKey;
  onChange: (v: ViewKey) => void;
  isAdmin: boolean;
}) {
  const [settingsExpanded, setSettingsExpanded] = useState(active.startsWith("settings-"));
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const { isMobile, isTablet } = useViewport();

  // Close sidebar on navigation on mobile
  const handleNavClick = (key: ViewKey) => {
    onChange(key);
    if (isMobile) setSidebarOpen(false);
  };

  const sidebarWidth = isMobile ? (sidebarOpen ? "190px" : "0px") : isTablet ? "156px" : "190px";

  return (
    <>
      {isMobile && (
        <button
          onClick={() => setSidebarOpen(!sidebarOpen)}
          style={{
            position: "fixed",
            top: "0.75rem",
            left: "0.75rem",
            zIndex: 1000,
            width: "44px",
            height: "44px",
            borderRadius: "var(--radius)",
            border: "1px solid var(--border)",
            background: "var(--surface-card)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            cursor: "pointer",
            boxShadow: "0 2px 8px rgba(0,0,0,0.1)",
            color: "var(--text-primary)",
            fontSize: "18px",
          }}
          aria-label="Toggle navigation"
        >
          {sidebarOpen ? "✕" : "☰"}
        </button>
      )}
      <nav
        style={{
          width: sidebarWidth,
          flexShrink: 0,
          borderRight: "1px solid var(--border)",
          display: "flex",
          flexDirection: "column",
          padding: isTablet ? "0.75rem 0" : "1rem 0",
          transition: "width 0.2s ease",
          overflow: "hidden",
          position: isMobile ? "fixed" : "relative",
          top: 0,
          left: sidebarOpen ? 0 : -(isMobile ? 200 : 0), // slide off-screen when closed on mobile
          height: "100vh",
          zIndex: isMobile ? 999 : 0,
          background: "var(--surface-page)",
          boxShadow: isMobile && sidebarOpen ? "4px 0 16px rgba(0,0,0,0.1)" : "none",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: isMobile && !sidebarOpen ? "center" : "center",
            padding: isTablet ? "0 0.65rem 0.75rem" : "0 1rem 1rem",
            borderBottom: "1px solid var(--border)",
            marginBottom: "0.5rem",
            opacity: isMobile && !sidebarOpen ? 0 : 1,
            pointerEvents: isMobile && !sidebarOpen ? "none" : "auto",
            transition: "opacity 0.2s ease",
            whiteSpace: "nowrap",
            overflow: "hidden",
          }}
        >
          <img src="/logo.svg" alt="Curatarr" style={{ height: "44px", width: "44px" }} />
          {sidebarOpen || !isMobile && (
            <span style={{ 
              fontFamily: "var(--font-heading)",
              fontSize: isTablet ? "17px" : "20px",
              fontWeight: 600,
              color: "var(--text-primary)",
              marginLeft: "0.75rem",
              display: "flex",
              alignItems: "center",
            }}
            >Curatarr</span>
          )}
        </div>

        {TOP_ITEMS.filter((item) => !item.adminOnly || isAdmin).map((item) => (
          <NavButton
            key={item.key}
            item={item}
            active={active === item.key}
            onClick={() => handleNavClick(item.key)}
            isMobile={isMobile}
            sidebarOpen={sidebarOpen}
            compact={isTablet}
          />
        ))}

        {isAdmin && (
          <>
            <button
              onClick={() => setSettingsExpanded((v) => !v)}
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                textAlign: "left",
                padding: isTablet ? "8px 0.65rem" : "8px 1rem",
                border: "none",
                borderLeft: "3px solid transparent",
                background: "transparent",
                color: "var(--text-secondary)",
                fontFamily: "var(--font-body)",
                fontSize: isTablet ? "12px" : "13px",
                cursor: "pointer",
                opacity: isMobile && !sidebarOpen ? 0 : 1,
                pointerEvents: isMobile && !sidebarOpen ? "none" : "auto",
                whiteSpace: "nowrap",
                overflow: "hidden",
              }}
            >
              {isMobile && !sidebarOpen ? (
                <span style={{ display: "inline-flex" }}><GearIcon /></span>
              ) : (
                <>
                  <span style={{ display: "inline-flex", alignItems: "center", gap: "0.5rem" }}>
                    <GearIcon />
                    <span>Settings</span>
                  </span>
                  <span style={{ fontSize: "10px" }}>{settingsExpanded ? "▾" : "▸"}</span>
                </>
              )}
            </button>
            {settingsExpanded &&
              SETTINGS_CHILDREN.map((item) => (
                <NavButton
                  key={item.key}
                  item={item}
                  active={active === item.key}
                  onClick={() => handleNavClick(item.key)}
                  indent
                  isMobile={isMobile}
                  sidebarOpen={sidebarOpen}
                  compact={isTablet}
                />
              ))}
          </>
        )}
      </nav>
    </>
  );
}

function NavButton({
  item,
  active,
  onClick,
  indent,
  isMobile,
  sidebarOpen,
  compact,
}: {
  item: NavItem;
  active: boolean;
  onClick: () => void;
  indent?: boolean;
  isMobile: boolean;
  sidebarOpen: boolean;
  compact: boolean;
}) {
  const showLabel = !isMobile || sidebarOpen;
  
  return (
    <button
      onClick={onClick}
      style={{
        display: "flex",
        justifyContent: isMobile && !sidebarOpen ? "center" : "space-between",
        alignItems: "center",
        textAlign: "left",
        padding: isMobile && !sidebarOpen
          ? "8px 1rem"
          : compact
            ? `8px ${indent ? "1.05rem" : "0.65rem"}`
            : `8px ${indent ? "1.75rem" : "1rem"} 8px 1rem`,
        border: "none",
        borderLeft: active ? "3px solid var(--color-primary)" : "3px solid transparent",
        background: active ? "var(--surface-raised)" : "transparent",
        color: active ? "var(--text-primary)" : "var(--text-secondary)",
        fontFamily: "var(--font-body)",
        fontSize: compact ? (indent ? "11px" : "12px") : indent ? "12px" : "13px",
        fontWeight: active ? 500 : 400,
        cursor: "pointer",
        gap: compact ? "0.45rem" : "0.75rem",
        whiteSpace: "nowrap",
        overflow: "hidden",
      }}
    >
      <span style={{ fontSize: "18px", flexShrink: 0, display: "inline-flex", alignItems: "center", justifyContent: "center", width: "18px", height: "18px" }}>{item.icon}</span>
      {showLabel && <span>{item.label}</span>}
      {showLabel && item.comingSoon && (
        <span style={{ fontSize: "9px", color: "var(--text-muted)", border: "1px solid var(--border)", borderRadius: "3px", padding: "1px 4px" }}>
          soon
        </span>
      )}
    </button>
  );
}

function GearIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      style={{ flexShrink: 0 }}
    >
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.6 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.6a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
    </svg>
  );
}

function ServerIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      style={{ flexShrink: 0 }}
    >
      <rect x="3" y="3" width="18" height="7" rx="1.5" />
      <rect x="3" y="14" width="18" height="7" rx="1.5" />
      <path d="M7 6.5h.01" />
      <path d="M7 17.5h.01" />
    </svg>
  );
}
