#if UNITY_EDITOR
using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Rolling RAM samples for Hub — forex-style candlestick chart (auto-scaled % of editor budget).
    /// </summary>
    public static class CaveBuildHardwareMonitor
    {
        const int CandleCount = 36;
        const int MaxTicksPerCandle = 4;
        const double TickIntervalSeconds = 0.15;
        const double CandleFinalizeSeconds = 0.55;
        const float MinChartSpan01 = 0.012f;

        struct RamCandle
        {
            public float Open;
            public float High;
            public float Low;
            public float Close;
            public bool HasData;
        }

        static readonly RamCandle[] Candles = new RamCandle[CandleCount];
        static int _candleWriteIndex;
        static int _candleFilled;

        static float _tickOpen;
        static float _tickHigh;
        static float _tickLow;
        static float _tickClose;
        static int _tickCount;
        static double _candleStartedAt;

        static double _lastSampleAt;
        static bool _hooked;
        const float HeaderHeight = 18f;
        const float FooterHeight = 16f;
        const float AxisGutter = 28f;

        static GUIStyle _pctLabelStyle;
        static GUIStyle _budgetLabelStyle;
        static GUIStyle _trendLabelStyle;
        static GUIStyle _axisLabelStyle;
        static string _stepLabel = string.Empty;

        public static float LastUsage01 { get; private set; }

        public static void SetStepLabel(string label) => _stepLabel = label ?? string.Empty;

        public static void EnsureSampling()
        {
            if (_hooked)
                return;
            _hooked = true;
            _lastSampleAt = 0;
            EditorApplication.update += OnUpdate;
            RecordSample(CaptureUsage01());
        }

        public static void StopSampling()
        {
            if (!_hooked)
                return;
            _hooked = false;
            EditorApplication.update -= OnUpdate;
            ResetHistory();
        }

        /// <summary>Extra tick when a paced queue step finishes — keeps candles moving during heavy builds.</summary>
        public static void OnQueueStepCompleted()
        {
            if (!_hooked)
                return;
            RecordSample(CaptureUsage01());
        }

        static void ResetHistory()
        {
            _candleWriteIndex = 0;
            _candleFilled = 0;
            _tickCount = 0;
            _candleStartedAt = 0;
            LastUsage01 = 0f;
            _stepLabel = string.Empty;
            for (var i = 0; i < CandleCount; i++)
                Candles[i] = default;
        }

        static void OnUpdate()
        {
            if (!CaveBuildRunStatusPublisher.HasActiveSession &&
                !CaveBuildStepCounter.HasSession)
            {
                StopSampling();
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastSampleAt < TickIntervalSeconds)
                return;
            _lastSampleAt = now;
            RecordSample(CaptureUsage01());
        }

        static float CaptureUsage01()
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                var ws = proc.WorkingSet64;
                var capBytes = (long)(ResolveBudgetGb() * 1024L * 1024L * 1024L);
                return Mathf.Clamp01(ws / (float)capBytes);
            }
            catch
            {
                return Mathf.Clamp01(GC.GetTotalMemory(false) / (512f * 1024f * 1024f));
            }
        }

        static float ResolveBudgetGb() => EnvironmentKitHardwareBudget.ResolveEditorRamBudgetGb();

        static void RecordSample(float usage01)
        {
            LastUsage01 = usage01;

            if (_tickCount == 0)
            {
                _tickOpen = _tickHigh = _tickLow = _tickClose = usage01;
                _tickCount = 1;
                _candleStartedAt = EditorApplication.timeSinceStartup;
                return;
            }

            _tickHigh = Mathf.Max(_tickHigh, usage01);
            _tickLow = Mathf.Min(_tickLow, usage01);
            _tickClose = usage01;
            _tickCount++;

            var now = EditorApplication.timeSinceStartup;
            if (_tickCount >= MaxTicksPerCandle || now - _candleStartedAt >= CandleFinalizeSeconds)
                FinalizeCandle();
        }

        static void FinalizeCandle()
        {
            if (_tickCount == 0)
                return;

            Candles[_candleWriteIndex] = new RamCandle
            {
                Open = _tickOpen,
                High = _tickHigh,
                Low = _tickLow,
                Close = _tickClose,
                HasData = true,
            };
            _candleWriteIndex = (_candleWriteIndex + 1) % CandleCount;
            _candleFilled = Mathf.Min(CandleCount, _candleFilled + 1);
            _tickCount = 0;
        }

        static void EnsureLabelStyles()
        {
            _pctLabelStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white },
            };
            _budgetLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.LowerLeft,
                normal = { textColor = new Color(0.78f, 0.95f, 0.92f, 1f) },
            };
            _trendLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.65f, 0.82f, 0.78f, 0.92f) },
            };
            _axisLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 8,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.55f, 0.68f, 0.64f, 0.85f) },
            };
        }

        static int OrderedCandleIndex(int displayIndex)
        {
            if (_candleFilled < CandleCount)
                return displayIndex;
            return (_candleWriteIndex + displayIndex) % CandleCount;
        }

        static void ResolveChartRange(int count, out float ymin, out float ymax)
        {
            ymin = 1f;
            ymax = 0f;
            for (var i = 0; i < count; i++)
            {
                var c = Candles[OrderedCandleIndex(i)];
                if (!c.HasData)
                    continue;
                ymin = Mathf.Min(ymin, c.Low);
                ymax = Mathf.Max(ymax, c.High);
            }

            if (_tickCount > 0)
            {
                ymin = Mathf.Min(ymin, _tickLow);
                ymax = Mathf.Max(ymax, _tickHigh);
            }

            ymin = Mathf.Min(ymin, LastUsage01);
            ymax = Mathf.Max(ymax, LastUsage01);

            if (ymax <= ymin)
                ymax = ymin + MinChartSpan01;

            var span = ymax - ymin;
            if (span < MinChartSpan01)
            {
                var mid = (ymax + ymin) * 0.5f;
                ymin = mid - MinChartSpan01 * 0.5f;
                ymax = mid + MinChartSpan01 * 0.5f;
            }

            var pad = Mathf.Max(MinChartSpan01 * 0.35f, span * 0.12f);
            ymin = Mathf.Max(0f, ymin - pad);
            ymax = Mathf.Min(1f, ymax + pad);

            if (ymax - ymin < MinChartSpan01)
                ymax = Mathf.Min(1f, ymin + MinChartSpan01);
        }

        static float ValueToY(float value01, Rect rect, float ymin, float ymax)
        {
            var t = Mathf.InverseLerp(ymin, ymax, value01);
            return rect.yMax - t * rect.height;
        }

        static void DrawChartGrid(Rect rect, float ymin, float ymax)
        {
            var grid = new Color(0.22f, 0.28f, 0.32f, 0.55f);
            for (var band = 1; band <= 3; band++)
            {
                var t = band / 4f;
                var y = rect.yMax - rect.height * t;
                EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1f), grid);
            }

            var axisRect = new Rect(rect.x - AxisGutter + 2f, rect.y, AxisGutter - 4f, rect.height);
            GUI.Label(new Rect(axisRect.x, rect.y, axisRect.width, 12f), $"{Mathf.RoundToInt(ymax * 100f)}%", _axisLabelStyle);
            GUI.Label(
                new Rect(axisRect.x, rect.yMax - 12f, axisRect.width, 12f),
                $"{Mathf.RoundToInt(ymin * 100f)}%",
                _axisLabelStyle);

            if (ymax >= 0.98f)
            {
                var capY = ValueToY(1f, rect, ymin, ymax);
                if (capY >= rect.y && capY <= rect.yMax)
                {
                    EditorGUI.DrawRect(new Rect(rect.x, capY, rect.width, 1f), new Color(0.95f, 0.55f, 0.35f, 0.55f));
                }
            }
        }

        static void DrawCandles(Rect rect, int count, float ymin, float ymax)
        {
            if (count <= 0)
                return;

            var slot = rect.width / Mathf.Max(count, 1);
            var bodyW = Mathf.Clamp(slot * 0.55f, 2f, 8f);
            var wickW = Mathf.Max(1f, bodyW * 0.22f);

            Handles.BeginGUI();
            for (var i = 0; i < count; i++)
            {
                RamCandle c;
                var forming = false;
                if (i == count - 1 && _tickCount > 0)
                {
                    c = new RamCandle
                    {
                        Open = _tickOpen,
                        High = _tickHigh,
                        Low = _tickLow,
                        Close = _tickClose,
                        HasData = true,
                    };
                    forming = true;
                }
                else
                {
                    c = Candles[OrderedCandleIndex(i)];
                }

                if (!c.HasData)
                    continue;

                var cx = rect.x + (i + 0.5f) * slot;
                var yOpen = ValueToY(c.Open, rect, ymin, ymax);
                var yClose = ValueToY(c.Close, rect, ymin, ymax);
                var yHigh = ValueToY(c.High, rect, ymin, ymax);
                var yLow = ValueToY(c.Low, rect, ymin, ymax);

                var bullish = c.Close >= c.Open;
                var bodyColor = bullish
                    ? new Color(0.18f, 0.92f, 0.72f, forming ? 0.72f : 0.95f)
                    : new Color(0.95f, 0.38f, 0.34f, forming ? 0.72f : 0.95f);
                var wickColor = bullish
                    ? new Color(0.12f, 0.62f, 0.52f, 0.9f)
                    : new Color(0.72f, 0.28f, 0.26f, 0.9f);

                Handles.color = wickColor;
                Handles.DrawAAPolyLine(
                    1.5f,
                    new Vector3(cx, yHigh, 0f),
                    new Vector3(cx, yLow, 0f));

                var top = Mathf.Min(yOpen, yClose);
                var bottom = Mathf.Max(yOpen, yClose);
                if (bottom - top < 1.5f)
                {
                    top -= 0.75f;
                    bottom += 0.75f;
                }

                EditorGUI.DrawRect(
                    new Rect(cx - bodyW * 0.5f, top, bodyW, bottom - top),
                    bodyColor);
            }

            var liveY = ValueToY(LastUsage01, rect, ymin, ymax);
            Handles.color = new Color(0.35f, 0.98f, 0.88f, 0.85f);
            Handles.DrawAAPolyLine(
                1.2f,
                new Vector3(rect.xMax - 18f, liveY, 0f),
                new Vector3(rect.xMax - 2f, liveY, 0f));
            Handles.EndGUI();
        }

        static string FormatTrendArrow()
        {
            if (_candleFilled < 1 && _tickCount < 2)
                return string.Empty;

            float prevClose;
            if (_candleFilled >= 1)
            {
                var idx = (_candleWriteIndex - 1 + CandleCount) % CandleCount;
                prevClose = Candles[idx].Close;
            }
            else
            {
                prevClose = _tickOpen;
            }

            var delta = LastUsage01 - prevClose;
            if (Mathf.Abs(delta) < 0.0008f)
                return "→";
            return delta > 0f ? "↑" : "↓";
        }

        static void DrawPanelChrome(Rect outer)
        {
            EditorGUI.DrawRect(outer, new Color(0.07f, 0.09f, 0.11f, 0.96f));
            var header = new Rect(outer.x, outer.y, outer.width, HeaderHeight);
            EditorGUI.DrawRect(header, new Color(0.1f, 0.13f, 0.16f, 0.98f));
            var footer = new Rect(outer.x, outer.yMax - FooterHeight, outer.width, FooterHeight);
            EditorGUI.DrawRect(footer, new Color(0.09f, 0.11f, 0.14f, 0.98f));
            EditorGUI.DrawRect(new Rect(outer.x, header.yMax, outer.width, 1f), new Color(0.18f, 0.24f, 0.28f, 0.9f));
            EditorGUI.DrawRect(new Rect(outer.x, footer.y, outer.width, 1f), new Color(0.18f, 0.24f, 0.28f, 0.9f));
        }

        static Rect ChartPlotRect(Rect outer) =>
            new Rect(
                outer.x + AxisGutter,
                outer.y + HeaderHeight + 2f,
                outer.width - AxisGutter - 4f,
                outer.height - HeaderHeight - FooterHeight - 4f);

        public static void DrawMiniGraph(float width, float height)
        {
            EnsureLabelStyles();
            EnsureSampling();
            var capGb = ResolveBudgetGb();
            var pct = Mathf.RoundToInt(LastUsage01 * 100f);
            var trend = FormatTrendArrow();
            var displayCount = _candleFilled + (_tickCount > 0 ? 1 : 0);

            var outer = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
            DrawPanelChrome(outer);

            var header = new Rect(outer.x + 8f, outer.y + 1f, outer.width - 16f, HeaderHeight - 2f);
            GUI.Label(
                new Rect(header.x, header.y, header.width * 0.55f, header.height),
                $"RAM {pct}%{(string.IsNullOrEmpty(trend) ? "" : " " + trend)}",
                _pctLabelStyle);

            if (displayCount >= 1)
            {
                ResolveChartRange(displayCount, out var ymin, out var ymax);
                var plot = ChartPlotRect(outer);
                EditorGUI.DrawRect(plot, new Color(0.05f, 0.07f, 0.09f, 0.85f));
                DrawChartGrid(plot, ymin, ymax);
                DrawCandles(plot, displayCount, ymin, ymax);
                GUI.Label(
                    new Rect(header.x + header.width * 0.55f, header.y, header.width * 0.45f, header.height),
                    $"zoom {Mathf.RoundToInt(ymin * 100f)}–{Mathf.RoundToInt(ymax * 100f)}%",
                    _trendLabelStyle);
            }
            else
            {
                var plot = ChartPlotRect(outer);
                EditorGUI.DrawRect(plot, new Color(0.05f, 0.07f, 0.09f, 0.85f));
                GUI.Label(
                    new Rect(plot.x + 6f, plot.y + plot.height * 0.35f, plot.width - 12f, 16f),
                    "Sampling…",
                    _trendLabelStyle);
            }

            var footer = new Rect(outer.x + 8f, outer.yMax - FooterHeight + 1f, outer.width - 16f, FooterHeight - 2f);
            GUI.Label(
                new Rect(footer.x, footer.y, footer.width * 0.62f, footer.height),
                $"of {capGb:F0} GB editor budget",
                _budgetLabelStyle);
            if (displayCount >= 1)
            {
                GUI.Label(
                    new Rect(footer.x + footer.width * 0.62f, footer.y, footer.width * 0.38f, footer.height),
                    $"{displayCount} candles",
                    _trendLabelStyle);
            }

            if (!string.IsNullOrEmpty(_stepLabel))
            {
                var stepRect = new Rect(outer.x + 8f, outer.y + HeaderHeight + 1f, outer.width - 16f, 14f);
                GUI.Label(stepRect, _stepLabel, _trendLabelStyle);
            }
        }
    }
}
#endif
