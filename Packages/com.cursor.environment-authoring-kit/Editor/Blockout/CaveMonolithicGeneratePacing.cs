#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Paced monolithic cave generate — one progress segment per editor-queue chunk.</summary>
    internal static class CaveMonolithicGeneratePacing
    {
        public const int SplineChunkCount = 9;
        public const int LavaTubeChunkCount = 4;

        public sealed class Session
        {
            public Transform EnvironmentRoot;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public System.Func<float, string, bool> ReportProgress;
            public bool UseSplineMesh;
            public int Chunk;
            public LavaTubeCaveBuildReport Report;
            public bool Cancelled;
            public SplineLavaTubeCaveGenerator.SplineMonolithicRuntime SplineRuntime;
            public LavaTubeCaveGenerator.LavaTubeMonolithicRuntime LavaTubeRuntime;
        }

        public static bool TryAdvance(Session session)
        {
            if (session == null)
                return true;

            if (session.Cancelled)
            {
                session.Report ??= new LavaTubeCaveBuildReport { Message = "Cave build cancelled." };
                return true;
            }

            if (session.UseSplineMesh)
            {
                var done = SplineLavaTubeCaveGenerator.TryAdvanceMonolithicChunk(session);
                if (done && session.Report == null)
                    session.Report = new LavaTubeCaveBuildReport { Message = "Spline monolithic chunk finished without report." };
                return done;
            }

            var lavaDone = LavaTubeCaveGenerator.TryAdvanceMonolithicChunk(session);
            if (lavaDone && session.Report == null)
                session.Report = new LavaTubeCaveBuildReport { Message = "Lava tube monolithic chunk finished without report." };
            return lavaDone;
        }

        public static string ChunkLabel(Session session)
        {
            var total = session.UseSplineMesh ? SplineChunkCount : LavaTubeChunkCount;
            return $"cave monolithic geo {session.Chunk + 1}/{total}";
        }
    }
}
#endif
