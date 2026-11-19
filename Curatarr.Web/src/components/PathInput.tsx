import { useEffect, useRef, useState } from "react";
import { api, BrowseEntry } from "../api/client";
import { FolderBrowserModal } from "./FolderBrowserModal";

const inputStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "13px",
  padding: "6px 8px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--border)",
  background: "var(--surface-card)",
  color: "var(--text-primary)",
};

export function PathInput({
  value,
  onChange,
  placeholder,
  style,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  style?: React.CSSProperties;
}) {
  const [suggestions, setSuggestions] = useState<BrowseEntry[]>([]);
  const [showSuggestions, setShowSuggestions] = useState(false);
  const [browserOpen, setBrowserOpen] = useState(false);
  const debounceRef = useRef<number | null>(null);

  useEffect(() => {
    if (debounceRef.current) window.clearTimeout(debounceRef.current);
    if (!value) {
      setSuggestions([]);
      return;
    }
    debounceRef.current = window.setTimeout(async () => {
      const lastSlash = value.lastIndexOf("/");
      const dir = lastSlash > 0 ? value.slice(0, lastSlash) : "/";
      const partial = lastSlash >= 0 ? value.slice(lastSlash + 1) : value;
      try {
        const result = await api.browseFilesystem(dir);
        setSuggestions(
          result.entries.filter((e) => e.name.toLowerCase().startsWith(partial.toLowerCase())).slice(0, 8)
        );
      } catch {
        setSuggestions([]);
      }
    }, 300);
    return () => {
      if (debounceRef.current) window.clearTimeout(debounceRef.current);
    };
  }, [value]);

  return (
    <div style={{ position: "relative", ...style }}>
      <div style={{ display: "flex", gap: "4px" }}>
        <input
          value={value}
          onChange={(e) => {
            onChange(e.target.value);
            setShowSuggestions(true);
          }}
          onFocus={() => setShowSuggestions(true)}
          onBlur={() => setTimeout(() => setShowSuggestions(false), 150)}
          placeholder={placeholder}
          style={{ ...inputStyle, flex: 1, minWidth: 0 }}
        />
        <button
          type="button"
          onClick={() => setBrowserOpen(true)}
          style={{ ...inputStyle, cursor: "pointer", whiteSpace: "nowrap" }}
        >
          Browse...
        </button>
      </div>
      {showSuggestions && suggestions.length > 0 && (
        <div
          style={{
            position: "absolute",
            top: "100%",
            left: 0,
            right: 0,
            background: "var(--surface-card)",
            border: "1px solid var(--border)",
            borderRadius: "var(--radius)",
            zIndex: 10,
            maxHeight: "160px",
            overflowY: "auto",
          }}
        >
          {suggestions.map((s) => (
            <div
              key={s.path}
              onMouseDown={() => {
                onChange(s.path);
                setShowSuggestions(false);
              }}
              style={{ padding: "6px 8px", fontSize: "12px", cursor: "pointer", color: "var(--text-primary)" }}
            >
              {s.name}
            </div>
          ))}
        </div>
      )}
      {browserOpen && (
        <FolderBrowserModal
          startPath={value || "/"}
          onSelect={(p) => {
            onChange(p);
            setBrowserOpen(false);
          }}
          onClose={() => setBrowserOpen(false)}
        />
      )}
    </div>
  );
}
