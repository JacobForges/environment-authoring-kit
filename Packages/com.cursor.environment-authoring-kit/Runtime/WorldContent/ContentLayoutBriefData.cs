using System;
using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    [Serializable]
    public class ContentLayoutBriefFile
    {
        public int version;
        public string sceneName;
        public string finalizedUtc;
        public ContentPlan contentPlan;
    }

    [Serializable]
    public class ContentPlan
    {
        public string sceneName;
        public ContentZoneDef[] zones;
        public ContentNpcDef[] npcs;
        public ContentEnemyDef[] enemies;
        public ContentPropDef[] props;
        public PlaymodeSmokeTest playmodeSmokeTest;
    }

    [Serializable]
    public class ContentZoneDef
    {
        public string id;
        public string label;
        public string notes;
    }

    [Serializable]
    public class ContentNpcDef
    {
        public string id;
        public string role;
        public string displayName;
        public WorldPosition worldPosition;
        public float rotationY;
        public string zoneId;
        public string questId;
        public NpcDialogDef dialog;
        public ContentBlockerDef[] gateBlockers;
    }

    [Serializable]
    public class ContentEnemyDef
    {
        public string id;
        public string displayName;
        public WorldPosition worldPosition;
        public WorldPosition[] patrolWaypoints;
        public ContentBlockerDef[] blockers;
    }

    [Serializable]
    public class ContentPropDef
    {
        public string id;
        public string label;
        public WorldPosition worldPosition;
        public string textureHint;
        public ContentBlockerDef[] blockers;
    }

    [Serializable]
    public class WorldPosition
    {
        public float x;
        public float y;
        public float z;

        public Vector3 ToVector3() => new Vector3(x, y, z);
    }

    [Serializable]
    public class NpcDialogDef
    {
        public string greeting;
        public DialogNodeDef[] nodes;
        public NpcShopDef shop;
    }

    [Serializable]
    public class DialogNodeDef
    {
        public string id;
        public string text;
        public DialogOptionDef[] options;
    }

    [Serializable]
    public class DialogOptionDef
    {
        public string label;
        public string nextId;
        public ContentBlockerDef[] blockers;
        public bool opensShop;
    }

    [Serializable]
    public class NpcShopDef
    {
        public string currency;
        public ShopItemDef[] items;
    }

    [Serializable]
    public class ShopItemDef
    {
        public string id;
        public string name;
        public int price;
        public ContentBlockerDef[] blockers;
    }

    [Serializable]
    public class ContentBlockerDef
    {
        public string type;
        public string questId;
        public float minTrainingGrade;
        public string hint;
    }

    [Serializable]
    public class PlaymodeSmokeTest
    {
        public string[] requiredNpcIds;
        public string[] steps;
    }
}
