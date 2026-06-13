using System;
using System.IO;
using UnityEngine;
using Hub;

namespace Hub.Competition
{
    [Serializable]
    public sealed class CompetitionSeasonManifest
    {
        public string seasonId = "2026-06-a";
        public string displayName = "Season 1";
        public int seasonIndex;
        public string mapScene = "MainScene";
        public string bundleKey;
        public long frozenUtc;
    }

    [Serializable]
    public sealed class CompetitionSeasonRotation
    {
        public CompetitionSeasonManifest[] seasons = { new() };
        public int rotateDays = 28;
    }

    public static class CompetitionSeasonManager
    {
        static CompetitionSeasonManifest _active;
        static CompetitionSeasonRotation _rotation;

        public static CompetitionSeasonManifest Active
        {
            get
            {
                if (_active == null)
                    EnsureLoaded();
                return _active;
            }
        }

        public static void InvalidateCache()
        {
            _active = null;
            _rotation = null;
        }

        public static void EnsureLoaded()
        {
            if (_active != null)
                return;

            LoadRotation();
            TryRotateIfDue();
            if (_active != null)
                return;

            var path = HubContentPaths.ResolveConfigFile("Competition/season/season_manifest.json");
            if (!File.Exists(path))
            {
                _active = new CompetitionSeasonManifest();
                return;
            }

            try
            {
                _active = JsonUtility.FromJson<CompetitionSeasonManifest>(File.ReadAllText(path))
                          ?? new CompetitionSeasonManifest();
            }
            catch
            {
                _active = new CompetitionSeasonManifest();
            }
        }

        static void LoadRotation()
        {
            var path = HubContentPaths.ResolveConfigFile("Competition/season/season_rotation.json");
            if (!File.Exists(path))
                return;

            try
            {
                _rotation = JsonUtility.FromJson<CompetitionSeasonRotation>(File.ReadAllText(path));
            }
            catch
            {
                _rotation = null;
            }
        }

        public static void TryRotateIfDue()
        {
            if (_rotation?.seasons == null || _rotation.seasons.Length == 0)
                return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var days = Math.Max(1, _rotation.rotateDays);
            var window = days * 86400L;
            var index = (int)((now / window) % _rotation.seasons.Length);
            var pick = _rotation.seasons[index];
            if (pick == null)
                return;

            if (pick.frozenUtc <= 0)
                pick.frozenUtc = now;

            _active = pick;
        }
    }
}
