const STEPS = [
  { id: "briefing", label: "Brief" },
  { id: "instrumental", label: "Instrumental" },
  { id: "record", label: "Record" },
  { id: "produce", label: "Produce" },
];

export default function MusicStepper({ current, onBack }) {
  const idx = STEPS.findIndex((s) => s.id === current);
  return (
    <nav className="music-stepper" aria-label="Music production steps">
      {onBack ? (
        <button type="button" className="secondary music-step-back" onClick={onBack}>
          ← Landing
        </button>
      ) : null}
      <ol className="music-step-list">
        {STEPS.map((step, i) => {
          const done = i < idx;
          const active = step.id === current;
          return (
            <li key={step.id} className={active ? "active" : done ? "done" : ""}>
              <span className="music-step-dot" aria-hidden="true">
                {done ? "✓" : i + 1}
              </span>
              {step.label}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
