export default function ThinkingIndicator({ label, variant = "director" }) {
  if (!label) return null;
  return (
    <div className={`thinking-bar thinking-${variant}`} role="status" aria-live="polite">
      <span className="thinking-spinner" aria-hidden />
      <span>{label}</span>
    </div>
  );
}
