export function ComingSoonPage({ title, description }: { title: string; description: string }) {
  return (
    <div style={{ maxWidth: "480px", fontFamily: "var(--font-body)" }}>
      <h2 style={{ fontFamily: "var(--font-heading)", fontSize: "20px", color: "var(--text-primary)" }}>{title}</h2>
      <p style={{ fontSize: "13px", color: "var(--text-secondary)", lineHeight: 1.5 }}>{description}</p>
    </div>
  );
}
