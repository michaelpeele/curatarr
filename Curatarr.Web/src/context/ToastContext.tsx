import { createContext, useContext, useState, useCallback } from "react";

export type ToastType = "success" | "error" | "warning" | "info";

interface Toast {
  id: string;
  type: ToastType;
  message: string;
}

interface ToastContextType {
  toasts: Toast[];
  showToast: (message: string, type?: ToastType) => void;
  dismissToast: (id: string) => void;
}

const ToastContext = createContext<ToastContextType | null>(null);

export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error("useToast must be used within ToastProvider");
  return ctx;
}

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const showToast = useCallback((message: string, type: ToastType = "info") => {
    const id = Math.random().toString(36).slice(2);
    setToasts((prev) => [...prev, { id, type, message }]);
    setTimeout(() => {
      setToasts((prev) => prev.filter((t) => t.id !== id));
    }, 4000);
  }, []);

  const dismissToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  return (
    <ToastContext.Provider value={{ toasts, showToast, dismissToast }}>
      {children}
      <ToastContainer toasts={toasts} onDismiss={dismissToast} />
    </ToastContext.Provider>
  );
}

function ToastContainer({ toasts, onDismiss }: { toasts: Toast[]; onDismiss: (id: string) => void }) {
  if (toasts.length === 0) return null;

  return (
    <div
      style={{
        position: "fixed",
        bottom: "80px",
        left: "50%",
        transform: "translateX(-50%)",
        display: "flex",
        flexDirection: "column-reverse",
        gap: "8px",
        zIndex: 1000,
        pointerEvents: "none",
        maxWidth: "400px",
        width: "calc(100% - 2rem)",
      }}
    >
      {toasts.map((toast) => (
        <ToastItem
          key={toast.id}
          toast={toast}
          onDismiss={onDismiss}
        />
      ))}
    </div>
  );
}

function ToastItem({ toast, onDismiss }: { toast: Toast; onDismiss: (id: string) => void }) {
  const bgColor =
    toast.type === "success" ? "var(--color-success, #10b981)" :
    toast.type === "error" ? "var(--color-danger, #ef4444)" :
    toast.type === "warning" ? "var(--color-warning, #f59e0b)" :
    "var(--color-primary, #6366f1)";

  const textColor = toast.type === "warning" ? "var(--text-primary)" : "var(--text-on-accent, white)";

  return (
    <div
      onClick={() => onDismiss(toast.id)}
      style={{
        background: bgColor,
        color: textColor,
        padding: "10px 16px",
        borderRadius: "var(--radius, 8px)",
        fontFamily: "var(--font-body)",
        fontSize: "13px",
        fontWeight: 500,
        boxShadow: "0 4px 12px rgba(0,0,0,0.2)",
        cursor: "pointer",
        pointerEvents: "auto",
        display: "flex",
        alignItems: "center",
        gap: "8px",
        animation: "slideIn 0.2s ease-out",
      }}
    >
      <span style={{ flex: 1 }}>{toast.message}</span>
      <button
        onClick={(e) => { e.stopPropagation(); onDismiss(toast.id); }}
        style={{
          background: "none",
          border: "none",
          color: textColor,
          cursor: "pointer",
          fontSize: "16px",
          lineHeight: 1,
          padding: "0 2px",
          opacity: 0.7,
        }}
      >
        ×
      </button>
    </div>
  );
}
