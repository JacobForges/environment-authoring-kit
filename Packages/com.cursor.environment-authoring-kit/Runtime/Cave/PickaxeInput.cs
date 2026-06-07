using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Desktop swing key — works with Input System-only or legacy Input Manager.</summary>
    static class PickaxeInput
    {
        public static bool WasPressedThisFrame(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return WasPressedInputSystem(key);
#else
            return Input.GetKeyDown(key);
#endif
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        static bool WasPressedInputSystem(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Mouse0:
                    return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
                case KeyCode.Mouse1:
                    return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
                case KeyCode.Mouse2:
                    return Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame;
                case KeyCode.Space:
                    return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
                case KeyCode.E:
                    return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
                case KeyCode.F:
                    return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
                default:
                    return false;
            }
        }
#endif
    }
}
