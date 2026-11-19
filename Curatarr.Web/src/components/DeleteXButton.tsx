export function DeleteXButton({ onClick, title }: { onClick: () => void; title: string }) {
  return (
    <button
      onClick={onClick}
      title={title}
      aria-label={title}
      style={{
        width: "20px",
        height: "20px",
        lineHeight: "18px",
        padding: 0,
        borderRadius: "50%",
        border: "1px solid var(--color-danger)",
        background: "transparent",
        color: "var(--color-danger)",
        cursor: "pointer",
        fontSize: "13px",
        fontFamily: "var(--font-body)",
      }}
    >
      ×
    </button>
  );
}
