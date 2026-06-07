#if UNITY_EDITOR
using System;
using System.Text.RegularExpressions;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Voiceover captions for demo recap (rule-based draft; optional AI polish at encode time).</summary>
    public static class CaveBuildDemoNarration
    {
        public readonly struct Lines
        {
            public readonly string Line1;
            public readonly string Line2;
            public readonly string Line3;

            public Lines(string line1, string line2, string line3 = null)
            {
                Line1 = TrimLine(line1, 88);
                Line2 = TrimLine(line2, 155);
                Line3 = string.IsNullOrWhiteSpace(line3) ? null : TrimLine(line3, 120);
            }

            public string CombinedForMarkdown
            {
                get
                {
                    if (string.IsNullOrEmpty(Line3))
                        return $"{Line1} {Line2}".Trim();
                    return $"{Line1} {Line2} {Line3}".Trim();
                }
            }

            public bool HasContent => !string.IsNullOrEmpty(Line1) || !string.IsNullOrEmpty(Line2);
        }

        public static Lines BuildStartLines => new(
            "Starting the world build.",
            "I'll explain what each milestone is doing and why it matters — not just read the log.",
            "Keep Scene view visible; slides come from that camera.");

        public static Lines BuildEndLines => new(
            "Build finished.",
            "This is the scene exactly where the pipeline stopped — terrain, content, and whatever steps completed.",
            null);

        public static Lines ForCheckpoint(string phase, string sub, int step, string buildMode)
        {
            var lines = TryNarrate(phase, sub, step, buildMode);
            if (lines.HasContent)
                return lines;

            return new Lines(
                "Still assembling the world.",
                "Compare this frame to the previous slide to see what changed in the Scene view.",
                null);
        }

        static Lines TryNarrate(string phase, string sub, int step, string buildMode)
        {
            var p = NormalizeToken(phase);
            var s = NormalizeToken(sub);
            var blob = $"{p} {s}";
            var scope = NormalizeToken(buildMode);

            if (TryMatchCraterTile(s, blob, out var crater))
                return crater;

            if (TryMatchFraction(s, blob, @"terrain ladder|terrain meat|grade rung", out var n, out var d, out _))
                return new Lines(
                    $"Terrain quality pass {n} of {d} on the play disk.",
                    "We fix height and walkability tile-by-tile so combat areas stay fair before foothills and peaks consume the border.",
                    ScopeNote(scope));

            if (TryMatchFraction(s, blob, @"macro step|pipeline step", out n, out d, out _))
                return new Lines(
                    $"Pipeline milestone {n} of {d}.",
                    "Later steps are blocked until this one commits — that's why the build feels slow but stays recoverable.",
                    ScopeNote(scope));

            if (blob.Contains("pre_build") || blob.Contains("pre-build") || blob.Contains("prebuild"))
                return new Lines(
                    "Pre-build quality gate.",
                    "If seams or layout fail here we abort instead of burning an hour on terrain that will get torn down anyway.",
                    null);

            if (blob.Contains("startup") || blob.Contains("clone setup") || blob.Contains("preflight"))
                return new Lines(
                    "Project warm-up and preflight.",
                    "Catalogs, clone checks, and layout audit run first so missing prefabs or bad anchors fail in minutes not hours.",
                    null);

            if (blob.Contains("research") || blob.Contains("researchagent") || blob.Contains("concept"))
                return new Lines(
                    "Loading research and reference rules.",
                    "Concept art and anti-patterns steer sculpt and layout so we don't repeat the star-maze or wrong-scale mistakes.",
                    null);

            if (blob.Contains("layout pre-plan") || blob.Contains("layout preplan"))
                return new Lines(
                    "Pre-planning tile layout.",
                    "Nine play tiles plus outer rings get a logical placement pass before heightmaps lock in wrong positions.",
                    null);

            if (blob.Contains("nine-tile") || blob.Contains("play disk") || blob.Contains("playdisk"))
                return new Lines(
                    "Locking the nine-tile play disk.",
                    "The walkable arena must stay flat, square, and seam-clean — everything else rings around this footprint.",
                    null);

            if (blob.Contains("stitch") || blob.Contains("seam") || blob.Contains("restitch"))
                return new Lines(
                    "Seaming terrain tiles.",
                    "Shared edges are welded so you don't feel a lip or height jump when crossing from one tile to the next.",
                    null);

            if (blob.Contains("dem") || blob.Contains("lidar") || blob.Contains("georef"))
                return new Lines(
                    "Anchoring elevation to real-world DEM data.",
                    "Macro hills read believable because height is georeferenced before local sculpt adds game-readable detail.",
                    null);

            if (blob.Contains("directional") && blob.Contains("grid"))
                return new Lines(
                    "Building the full terrain grid in rings.",
                    "Foothills, peaks, and horizon tiles place one-by-one so seams stay controlled instead of one giant sculpt tearing apart.",
                    null);

            if (blob.Contains("foothill") || blob.Contains("outer ring") || blob.Contains("wilderness tile"))
                return new Lines(
                    "Growing the wilderness ring.",
                    "Each outer tile is sculpted and seamed before the next — that's how the play disk stays isolated from messy overlap.",
                    null);

            if (blob.Contains("mountain pipeline") || blob.Contains("mountain_research"))
                return new Lines(
                    "Mountain pipeline — massif, mouths, annex.",
                    "Height, cave mouths, labyrinth, and trails run after the play disk contract is stable enough to align against.",
                    null);

            if (blob.Contains("wilderness_cave_mouth") || blob.Contains("cave mouth"))
                return new Lines(
                    "Placing wilderness cave mouths.",
                    "Outdoor mouth markers become ramp targets and the anchor for underground entry — surface and cave must agree here.",
                    null);

            if (blob.Contains("labyrinth_research"))
                return new Lines(
                    "Planning the south labyrinth annex.",
                    "Design calls for wide bench spines you can read from the play side — not a tight hedge maze or grid of pits.",
                    null);

            if (blob.Contains("labyrinth_carve") || blob.Contains("labyrinth"))
                return new Lines(
                    "Carving the south labyrinth walkways.",
                    "Heightmap spines cut walkable benches into the annex so players can loop, retreat, and sight the mountain mouths.",
                    null);

            if (blob.Contains("summit") || blob.Contains("peak sculpt") || blob.Contains("peak cap"))
                return new Lines(
                    "Summit and peak caps.",
                    "Silhouettes need to read at distance without stealing height from the flat nine-tile combat disk.",
                    null);

            if (blob.Contains("cliff"))
                return new Lines(
                    "Cliff passes on the massif.",
                    "Vertical faces sell scale while keeping scripted paths and mouths reachable after all the height shifts.",
                    null);

            if (blob.Contains("mountain_trail") || (blob.Contains("trail") && blob.Contains("mountain")))
                return new Lines(
                    "Mountain trails.",
                    "Descent routes are carved so traversal still works once peaks and cliffs are in their final positions.",
                    null);

            if (blob.Contains("play_smooth") || blob.Contains("crater repair"))
                return new Lines(
                    "Final play-disk polish.",
                    "Crater repair and smoothing remove pits that would trip players or break line-of-sight in combat.",
                    null);

            if (blob.Contains("plan v4") || blob.Contains("worldplan") || blob.Contains("cc0") || blob.Contains("npc layout"))
                return new Lines(
                    "Populating the world with characters and props.",
                    "CC0 content, NPCs, loot, and landmarks land on stable terrain so the space reads lived-in, not empty heightmap.",
                    null);

            if (blob.Contains("surface trail") || blob.Contains("surface_trails"))
                return new Lines(
                    "Surface trails and paths.",
                    "Routes on GeneratedSurfaceWorld show how to cross the disk without guessing where designers intended foot traffic.",
                    null);

            if (blob.Contains("surface finish") || blob.Contains("surface world"))
                return new Lines(
                    "Finishing the surface world layer.",
                    "Trails, water, openings, and nav hooks complete before cave geometry claims the underground contract.",
                    null);

            if (blob.Contains("terrain ai") || blob.Contains("terrain phase"))
                return new Lines(
                    "Automated terrain phases.",
                    "Each phase grades and may repair before the next is allowed to touch height — failures stop the chain early.",
                    null);

            if (blob.Contains("cave") && (blob.Contains("queue") || blob.Contains("shell") || blob.Contains("geometry")))
                return new Lines(
                    "Cave geometry pass.",
                    "Underground layout runs after surface mouths and terrain are trustworthy enough to snap routes and portals.",
                    ScopeNote(scope));

            if (blob.Contains("validate") || blob.Contains("audit") || blob.Contains("layout_audit"))
                return new Lines(
                    "Layout audit.",
                    "Seam gaps and tile alignment are measured here — failing audits block mountains or caves until restitch fixes land.",
                    null);

            if (blob.Contains("enhancement") || blob.Contains("meat pass"))
                return new Lines(
                    "Terrain enhancement / meat pass.",
                    "The grader flagged issues, so this pass repairs height and grounding instead of pretending the last score was fine.",
                    null);

            if (blob.Contains("surface only") || scope.Contains("surface only"))
                return new Lines(
                    "Surface-only build scope.",
                    "Terrain and surface content run without queuing full cave shell generation — faster iteration on the open world.",
                    null);

            if (blob.Contains("fullworld") || blob.Contains("full world") || scope.Contains("fullworld"))
                return new Lines(
                    "Full-world build scope.",
                    "Surface terrain and mountains complete first; cave work queues only after the open-world contract is in place.",
                    null);

            if (blob.Contains("batch"))
                return new Lines(
                    "Batch cave build.",
                    "Multiple cave layouts generate in sequence — useful for comparing rolls without rebuilding the entire surface.",
                    null);

            if (!string.IsNullOrEmpty(p))
                return new Lines(
                    HumanizePhaseTitle(p),
                    "Watch the terrain in frame — this milestone advanced the open-world layout even if the log name was opaque.",
                    ScopeNote(scope));

            return default;
        }

        static string ScopeNote(string scope)
        {
            if (string.IsNullOrEmpty(scope))
                return null;
            if (scope.Contains("cave") && scope.Contains("only"))
                return "Cave-only session: surface was built earlier.";
            return null;
        }

        static string HumanizePhaseTitle(string phase) =>
            string.IsNullOrEmpty(phase) ? "Build milestone" : "Working on " + phase.Replace('_', ' ');

        static bool TryMatchCraterTile(string sub, string blob, out Lines lines)
        {
            lines = default;
            var m = Regex.Match(
                sub,
                @"(?:crater\s+repair|repair)\s+(?:play\s+)?tile\s+(\d+)\s*/\s*(\d+)",
                RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(sub, @"tile\s+(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase);
            if (!m.Success)
                return false;

            var n = int.Parse(m.Groups[1].Value);
            var d = int.Parse(m.Groups[2].Value);
            lines = new Lines(
                $"Fixing craters on play tile {n} of {d}.",
                "Each tile is repaired separately so the nine-tile disk stays walkable before foothills and mountains lock around it.",
                "Pits and rim noise are removed, not just painted over.");
            return true;
        }

        static bool TryMatchFraction(string sub, string blob, string hintPattern, out int n, out int d, out string _)
        {
            n = d = 0;
            _ = null;
            if (!Regex.IsMatch(blob, hintPattern, RegexOptions.IgnoreCase))
                return false;

            var m = Regex.Match(sub, @"(\d+)\s*/\s*(\d+)");
            if (!m.Success)
                m = Regex.Match(sub, @"(\d+)\s*of\s*(\d+)", RegexOptions.IgnoreCase);
            if (!m.Success)
                return false;

            n = int.Parse(m.Groups[1].Value);
            d = int.Parse(m.Groups[2].Value);
            return true;
        }

        static string NormalizeToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var s = raw.Trim();
            s = Regex.Replace(s, @"\[[^\]]+\]", "");
            s = Regex.Replace(s, @"\bload×[\d.]+\s*\w*", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\brun\s*\[\d+/\d+\]\s*", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Replace('_', ' ').ToLowerInvariant();
        }

        static string TrimLine(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (text.Length <= max)
                return text;

            var cut = text.Substring(0, max - 1);
            var lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > max / 2)
                cut = cut.Substring(0, lastSpace);
            return cut.TrimEnd('.', ' ') + "…";
        }
    }
}
#endif
