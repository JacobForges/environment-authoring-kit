# Unity Connect / Package Manager console errors

**These are not caused by Environment Authoring Kit.** The kit does not use `UnityEditor.Connect`, `UnityWebRequest` to Unity cloud, or Package Manager token APIs.

## What you see

```
UnityConnectWebRequestException: Token Exchange failed…
[Package Manager Window] Error while getting access token: invalid configuration from Unity Connect
```

Unity Editor is trying to refresh your **Unity account token** for:

- Package Manager “product updates” UI
- `com.unity.ai.assistant` / cloud AI services
- Collab proxy (if enabled)

This often appears after recompile, opening Package Manager, or network/VPN/Hub sign-in issues. It does **not** mean the cave build failed.

## What we did in the project

| Item | Purpose |
|------|---------|
| `ProjectSettings/UnityConnectSettings.asset` | Analytics already off; engine diagnostics off |
| `OpenXRImportLoopGuard` log filter | Hides Connect/UPM token spam (default **on** with “Suppress OpenXR Import Warnings”) |
| `activeInputHandler: 1` (Input System only) | Removes “Input Manager is deprecated” — Hub `ProjectSettings`; pickaxe uses `PickaxeInput` |
| `OpenXRPackageSettings.asset.meta` `mainObjectFileID: 0` | Stops many `NativeFormatImporter inconsistent result` loops on multi-sub-asset XR settings |
| Startup OpenXR stabilize (once/session) | `OpenXRImportLoopGuard` queues `ForceUpdate` reimport when the editor loads |
| Menu: **Diagnostics → Unity Connect / Package Manager Errors (Help)** | Short explanation |

## If you want fewer checks

1. **Unity Hub** → sign out → sign in → restart Unity.
2. Close **Package Manager** when you are not adding packages.
3. Optional: remove `com.unity.ai.assistant` from `Packages/manifest.json` if you do not use Unity AI Assistant in-editor (Hub uses Cursor/external agents instead).

## If suppression is off

**Window → Environment Kit → Cave Build → Diagnostics → Suppress OpenXR Import Warnings** (toggle on).
