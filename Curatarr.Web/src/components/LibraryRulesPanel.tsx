import { useEffect, useState } from "react";
import {
  api,
  LibraryRule,
  MediaKind,
  RuleAction,
  RuleField,
  RuleOperator,
  RulePreview,
} from "../api/client";
import { useToast } from "../context/ToastContext";

const inputStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "12px",
  padding: "5px 7px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--border)",
  background: "var(--surface-card)",
  color: "var(--text-primary)",
};
const buttonStyle: React.CSSProperties = {
  fontFamily: "var(--font-body)",
  fontSize: "12px",
  padding: "5px 10px",
  borderRadius: "var(--radius)",
  border: "1px solid var(--color-primary)",
  background: "var(--color-primary)",
  color: "var(--text-on-accent)",
  cursor: "pointer",
};
const quietButton: React.CSSProperties = {
  ...buttonStyle,
  border: "1px solid var(--border)",
  background: "transparent",
  color: "var(--text-primary)",
};

/**
 * Per-library automatic rules panel. Fields/operators come from the shared
 * backend registry (/api/library-fields), so adding a grid filter later needs
 * no changes here. Preview never writes; Apply asks for confirmation and
 * reminds the user that Sync is still required for removals on disk.
 */
export function LibraryRulesPanel({
  libraryId,
  mediaType,
  canEdit,
}: {
  libraryId: string;
  mediaType: MediaKind;
  canEdit: boolean;
}) {
  const { showToast } = useToast();
  const [fields, setFields] = useState<RuleField[]>([]);
  const [rules, setRules] = useState<LibraryRule[]>([]);
  const [field, setField] = useState("");
  const [operator, setOperator] = useState<RuleOperator>("contains");
  const [value, setValue] = useState("");
  const [action, setAction] = useState<RuleAction>("approve");
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<RulePreview | null>(null);
  const [busy, setBusy] = useState(false);

  function refresh() {
    api.listRules(libraryId).then(setRules).catch((e) => setError(e.message));
  }

  useEffect(() => {
    setError(null);
    setPreview(null);
    api.listRuleFields().then(setFields).catch((e) => setError(e.message));
    refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [libraryId]);

  // Only fields that apply to this library's media type (e.g. no Network on
  // a Movie library). The registry is the single source of truth.
  const usableFields = fields.filter((f) => f.mediaTypes.includes(mediaType));
  const selected = usableFields.find((f) => f.value === field);

  function chooseField(next: string) {
    setField(next);
    const def = usableFields.find((f) => f.value === next);
    if (def && !def.operators.includes(operator)) setOperator(def.operators[0]);
  }

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await api.createRule(libraryId, {
        field,
        operator,
        value,
        action,
        priority: rules.length,
        enabled: true,
      });
      setField("");
      setOperator("contains");
      setValue("");
      setAction("approve");
      setPreview(null);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function handleToggle(rule: LibraryRule) {
    try {
      await api.updateRule(libraryId, { ...rule, enabled: !rule.enabled });
      setPreview(null);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function handleDelete(rule: LibraryRule) {
    if (!confirm(`Delete rule: ${rule.field} ${rule.operator} ${rule.value} → ${rule.action}?`)) return;
    try {
      await api.deleteRule(libraryId, rule.id);
      setPreview(null);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function handleMove(index: number, delta: number) {
    const ordered = [...rules].sort((a, b) => a.priority - b.priority || a.id.localeCompare(b.id));
    const target = index + delta;
    if (target < 0 || target >= ordered.length) return;
    [ordered[index], ordered[target]] = [ordered[target], ordered[index]];
    try {
      // Reassign contiguous priorities so later rules reliably override earlier ones.
      await Promise.all(ordered.map((r, i) =>
        api.updateRule(libraryId, { ...r, priority: i })
      ));
      setPreview(null);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function handlePreview() {
    setError(null);
    setBusy(true);
    try {
      setPreview(await api.previewRules(libraryId));
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function handleApply() {
    if (!confirm(
      "Apply these rules and change approval/hide state now?\n\n" +
      "Physical removals from the library happen on the next Sync — " +
      "you must press Sync afterwards for removals to take effect on disk and in Plex."
    )) return;
    setError(null);
    setBusy(true);
    try {
      const result = await api.applyRules(libraryId);
      showToast(
        `Rules applied: ${result.approved} approved, ${result.hidden} hidden, ` +
        `${result.unapproved} unapproved, ${result.unhidden} unhidden. Sync is still required for removals.`,
        "warning"
      );
      setPreview(null);
      refresh();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={{ background: "var(--surface-raised)", borderRadius: "var(--radius)", padding: "10px 12px", fontSize: "12px" }}>
      <p style={{ margin: "0 0 8px", color: "var(--text-secondary)" }}>
        Rules write the same approvals and hidden markers as manual curation. Manual decisions always
        win; later rules override earlier ones. Applying a rule never syncs links — press Sync afterwards
        for library removals to take effect on disk.
      </p>

      {rules.length > 0 && (
        <table style={{ width: "100%", borderCollapse: "collapse", marginBottom: "0.5rem" }}>
          <thead>
            <tr style={{ textAlign: "left", color: "var(--text-muted)" }}>
              <th style={{ padding: "4px" }}>#</th>
              <th style={{ padding: "4px" }}>Rule</th>
              <th style={{ padding: "4px" }}>Action</th>
              <th style={{ padding: "4px" }}>On</th>
              <th style={{ padding: "4px" }}></th>
            </tr>
          </thead>
          <tbody>
            {[...rules]
              .sort((a, b) => a.priority - b.priority || a.id.localeCompare(b.id))
              .map((rule, index) => {
                const originalIndex = [...rules]
                  .sort((a, b) => a.priority - b.priority || a.id.localeCompare(b.id))
                  .findIndex((r) => r.id === rule.id);
                return (
                  <tr key={rule.id} style={{ borderTop: "1px solid var(--border)" }}>
                    <td style={{ padding: "4px", color: "var(--text-muted)" }}>{index + 1}</td>
                    <td style={{ padding: "4px", color: "var(--text-primary)" }}>
                      {fields.find((f) => f.value === rule.field)?.label ?? rule.field}{" "}
                      <span style={{ color: "var(--text-secondary)" }}>{rule.operator}</span>{" "}
                      <strong>{rule.value}</strong>
                    </td>
                    <td style={{ padding: "4px", color: rule.action === "hide" ? "var(--color-danger)" : "var(--color-success)" }}>
                      {rule.action}
                    </td>
                    <td style={{ padding: "4px" }}>
                      <input
                        type="checkbox"
                        checked={rule.enabled}
                        disabled={!canEdit}
                        onChange={() => handleToggle(rule)}
                        aria-label={`Enable rule ${rule.id}`}
                      />
                    </td>
                    <td style={{ padding: "4px", whiteSpace: "nowrap" }}>
                      {canEdit && (
                        <>
                          <button onClick={() => handleMove(originalIndex, -1)} disabled={index === 0} style={{ ...quietButton, padding: "2px 6px" }} title="Move earlier">↑</button>{" "}
                          <button onClick={() => handleMove(originalIndex, 1)} disabled={index === rules.length - 1} style={{ ...quietButton, padding: "2px 6px" }} title="Move later">↓</button>{" "}
                          <button onClick={() => handleDelete(rule)} style={{ ...quietButton, padding: "2px 6px" }} title="Delete rule">✕</button>
                        </>
                      )}
                    </td>
                  </tr>
                );
              })}
          </tbody>
        </table>
      )}
      {rules.length === 0 && <p style={{ color: "var(--text-muted)" }}>No rules yet for this library.</p>}

      {canEdit && (
        <form onSubmit={handleAdd} style={{ display: "flex", gap: "0.4rem", flexWrap: "wrap", alignItems: "flex-end", marginTop: "0.5rem" }}>
          <label style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
            <span style={{ color: "var(--text-secondary)" }}>Field</span>
            <select value={field} onChange={(e) => chooseField(e.target.value)} style={inputStyle} required>
              <option value="">Select…</option>
              {usableFields.map((f) => <option key={f.value} value={f.value}>{f.label}</option>)}
            </select>
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
            <span style={{ color: "var(--text-secondary)" }}>Operator</span>
            <select value={operator} onChange={(e) => setOperator(e.target.value as RuleOperator)} style={inputStyle} disabled={!selected}>
              {(selected?.operators ?? []).map((op) => <option key={op} value={op}>{op}</option>)}
            </select>
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: "2px", flex: "1 1 160px" }}>
            <span style={{ color: "var(--text-secondary)" }}>Value {selected?.hint ? `(${selected.hint})` : ""}</span>
            <input value={value} onChange={(e) => setValue(e.target.value)} style={{ ...inputStyle, width: "100%" }} placeholder="e.g. Family or PG,TV-MA" required />
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
            <span style={{ color: "var(--text-secondary)" }}>Action</span>
            <select value={action} onChange={(e) => setAction(e.target.value as RuleAction)} style={inputStyle}>
              <option value="approve">approve</option>
              <option value="hide">hide</option>
            </select>
          </label>
          <button type="submit" disabled={busy || !field || !value.trim()} style={buttonStyle}>Add rule</button>
        </form>
      )}

      <div style={{ display: "flex", gap: "0.5rem", marginTop: "0.75rem", alignItems: "center" }}>
        <button onClick={handlePreview} disabled={busy} style={quietButton}>Preview</button>
        {canEdit && <button onClick={handleApply} disabled={busy} style={buttonStyle}>Apply…</button>}
      </div>

      {preview && (
        <div style={{ marginTop: "0.5rem", padding: "8px", border: "1px solid var(--border)", borderRadius: "var(--radius)" }}>
          <strong style={{ color: "var(--text-primary)" }}>
            Dry run: {preview.toApprove} to approve, {preview.toHide} to hide
          </strong>
          {preview.samples.length > 0 && (
            <p style={{ margin: "4px 0 0", color: "var(--text-secondary)" }}>
              Samples: {preview.samples.join(", ")}
            </p>
          )}
          <p style={{ margin: "4px 0 0", color: "var(--text-muted)" }}>
            Preview changes nothing. Apply still requires a separate Sync for removals.
          </p>
        </div>
      )}
      {error && <p style={{ color: "var(--color-danger)", fontSize: "12px", marginTop: "6px" }}>{error}</p>}
    </div>
  );
}
