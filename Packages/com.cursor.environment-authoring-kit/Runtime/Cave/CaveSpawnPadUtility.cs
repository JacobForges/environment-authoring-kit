using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Solid walkable spawn pad — CharacterController needs a real collider under the feet.</summary>
    public static class CaveSpawnPadUtility
    {
        public const string PadName = "SpawnGroundPad";

        public static void EnsureUnderSpawn(Transform spawn, Vector3 size)
        {
            if (spawn == null)
                return;

            var pad = spawn.Find(PadName);
            if (pad == null)
            {
                var padGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                padGo.name = PadName;
                padGo.transform.SetParent(spawn, false);
                RegisterCreated(padGo);
                pad = padGo.transform;
            }

            ConfigurePad(pad, size);
        }

        static void ConfigurePad(Transform pad, Vector3 size)
        {
            pad.localPosition = new Vector3(0f, -0.35f, 0f);
            pad.localRotation = Quaternion.identity;
            pad.localScale = size;

            var meshRenderer = pad.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                meshRenderer.enabled = false;

            var box = pad.GetComponent<BoxCollider>();
            if (box == null)
                box = pad.gameObject.AddComponent<BoxCollider>();

            box.size = Vector3.one;
            box.center = Vector3.zero;
            box.isTrigger = false;

            if (pad.GetComponent<CaveWalkableMarker>() == null)
                pad.gameObject.AddComponent<CaveWalkableMarker>();

            pad.gameObject.isStatic = true;
        }

        static void RegisterCreated(GameObject padGo)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RegisterCreatedObjectUndo(padGo, "Spawn Ground Pad");
#endif
        }
    }
}
