import { useEffect, useRef, useState } from "react";

const GLYPHS = [
  "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
  "abcdefghijklmnopqrstuvwxyz",
  "0123456789",
  "ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩ",
  "αβγδεζηθικλμνξοπρστυφχψω",
  "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ",
  "абвгдежзийклмнопрстуфхцчшщъыьэюя",
  "אבגדהוזחטיכלמנסעפצקרשת",
  "ابتثجحخدذرزسشصضطظعغفقكلمنهوي",
  "अआइईउऊएऐओऔकखगघचछजझटठडढणतथदधनपफबभमयरलवशषसह",
  "あいうえおかきくけこさしすせそたちつてとなにぬねの",
  "アイウエオカキクケコサシスセソタチツテトナニヌネノ",
  "한글레이아웃생성메시환경동굴지형",
  "环境洞窟地形生成布局网格",
  "環境洞窟地形生成佈局網格",
  "⌁◈◇◆▣▤▥▦▧▨▩⬡⬢⟐⟡✦✧",
  "𝔞𝔟𝔠δεζηθικλμνξοπρστυφχψω",
  "∀∃∂∇∞≈≠≤≥±×÷",
  "ꙮ꙾Ҁҁ҂ÆØÅß",
  "ᚠᚢᚦᚨᚱᚲᚷᚹᚺᚾᛁᛃᛇᛈᛉᛊᛏᛒᛖᛗᛚᛜᛞᛟ",
  "◌⌇⍟⎔⏣␡␣⍰⍱⍲⍳",
  "ζξχψΩΘΞΦΛΣ",
].join("");

function useSyntheticProgress(active) {
  const [pct, setPct] = useState(0);
  useEffect(() => {
    if (!active) {
      setPct(0);
      return undefined;
    }
    setPct(6);
    const started = Date.now();
    const id = window.setInterval(() => {
      const elapsed = Date.now() - started;
      setPct(Math.min(92, 6 + elapsed / 65));
    }, 100);
    return () => window.clearInterval(id);
  }, [active]);
  return pct;
}

export function useCardWorkProgress(active, job) {
  const synthetic = useSyntheticProgress(active && !job);
  if (!active) return { progress: 0, indeterminate: false };
  if (job && job.phase !== "error" && job.progress != null) {
    return {
      progress: Math.max(0, Math.min(100, Number(job.progress) || 0)),
      indeterminate: false,
    };
  }
  return { progress: synthetic, indeterminate: false };
}

export default function CardMatrixRain({ active, message, progress, indeterminate }) {
  const canvasRef = useRef(null);
  const frameRef = useRef(0);
  const dropsRef = useRef([]);
  const columnsRef = useRef(0);

  useEffect(() => {
    if (!active) return undefined;

    const canvas = canvasRef.current;
    if (!canvas) return undefined;

    const ctx = canvas.getContext("2d");
    if (!ctx) return undefined;

    let running = true;
    const fontSize = 14;

    const resize = () => {
      const parent = canvas.parentElement;
      if (!parent) return;
      const w = parent.clientWidth;
      const h = parent.clientHeight;
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      canvas.width = Math.floor(w * dpr);
      canvas.height = Math.floor(h * dpr);
      canvas.style.width = `${w}px`;
      canvas.style.height = `${h}px`;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      columnsRef.current = Math.max(1, Math.floor(w / fontSize));
      dropsRef.current = Array.from({ length: columnsRef.current }, () =>
        Math.floor(Math.random() * (h / fontSize))
      );
      ctx.fillStyle = "#061018";
      ctx.fillRect(0, 0, w, h);
    };

    resize();
    const ro = new ResizeObserver(resize);
    ro.observe(canvas.parentElement);

    const draw = () => {
      if (!running) return;
      const w = canvas.parentElement?.clientWidth || 0;
      const h = canvas.parentElement?.clientHeight || 0;
      const cols = columnsRef.current;
      const drops = dropsRef.current;

      ctx.fillStyle = "rgba(6, 14, 26, 0.14)";
      ctx.fillRect(0, 0, w, h);

      for (let i = 0; i < cols; i += 1) {
        const x = i * fontSize;
        const y = drops[i] * fontSize;
        const glyph = GLYPHS[Math.floor(Math.random() * GLYPHS.length)];

        ctx.shadowBlur = 0;
        ctx.fillStyle = "rgba(0, 72, 110, 0.55)";
        ctx.font = `${fontSize}px "SF Mono", "Menlo", "Consolas", monospace`;
        ctx.fillText(glyph, x, y - fontSize * 1.4);

        ctx.shadowColor = "#00e8ff";
        ctx.shadowBlur = 10;
        ctx.fillStyle = "#9afcff";
        ctx.fillText(glyph, x, y);

        ctx.shadowBlur = 0;
        ctx.fillStyle = "rgba(0, 196, 232, 0.35)";
        ctx.fillText(glyph, x, y + fontSize * 0.85);

        if (y > h + fontSize * 2 && Math.random() > 0.965) {
          drops[i] = 0;
        }
        drops[i] += 0.55 + Math.random() * 0.85;
      }

      frameRef.current = window.requestAnimationFrame(draw);
    };

    frameRef.current = window.requestAnimationFrame(draw);

    return () => {
      running = false;
      window.cancelAnimationFrame(frameRef.current);
      ro.disconnect();
    };
  }, [active]);

  if (!active) return null;

  const pct = Math.max(0, Math.min(100, Number(progress) || 0));
  const status = (message || "Working…").trim();

  return (
    <div className="concept-matrix-overlay" aria-live="polite" aria-busy="true">
      <canvas ref={canvasRef} className="concept-matrix-canvas" aria-hidden />
      <div className="concept-matrix-vignette" aria-hidden />
      <div className="concept-matrix-hud">
        <p className="concept-matrix-status">{status}</p>
        <div className="concept-matrix-progress-wrap">
          <div className="concept-matrix-progress-track">
            {indeterminate ? (
              <div className="concept-matrix-progress-indeterminate" />
            ) : (
              <div
                className="concept-matrix-progress-fill"
                style={{ width: `${pct}%` }}
              />
            )}
          </div>
          {!indeterminate && (
            <span className="concept-matrix-pct">{Math.round(pct)}%</span>
          )}
        </div>
      </div>
    </div>
  );
}
