using UnityEngine;
using UnityEngine.InputSystem;

namespace TFOU.Ship
{
    /// <summary>
    /// Thin wrapper over the new Input System so gameplay code never touches
    /// device polling directly. Keyboard + mouse + gamepad are all supported.
    /// </summary>
    public static class ShipInput
    {
        /// <summary>Throttle axis: +1 ahead, -1 astern.</summary>
        public static float Throttle()
        {
            float v = 0f;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
            }
            var gp = Gamepad.current;
            if (gp != null) v += gp.leftStick.y.ReadValue();
            return Mathf.Clamp(v, -1f, 1f);
        }

        /// <summary>Steering axis: +1 starboard, -1 port.</summary>
        public static float Steer()
        {
            float h = 0f;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
            }
            var gp = Gamepad.current;
            if (gp != null) h += gp.leftStick.x.ReadValue();
            return Mathf.Clamp(h, -1f, 1f);
        }

        /// <summary>Flank speed boost (left shift / right trigger).</summary>
        public static bool Boost()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)) return true;
            var gp = Gamepad.current;
            return gp != null && gp.rightTrigger.ReadValue() > 0.5f;
        }

        /// <summary>All-stop brake (space / gamepad B).</summary>
        public static bool Brake()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.isPressed) return true;
            var gp = Gamepad.current;
            return gp != null && gp.bButton.isPressed;
        }

        public static bool OrbitHeld()
        {
            var m = Mouse.current;
            return m != null && m.rightButton.isPressed;
        }

        public static Vector2 MouseDelta()
        {
            var m = Mouse.current;
            return m != null ? m.delta.ReadValue() : Vector2.zero;
        }

        /// <summary>Normalized scroll wheel delta.</summary>
        public static float Scroll()
        {
            var m = Mouse.current;
            if (m != null) return Mathf.Clamp(m.scroll.ReadValue().y / 120f, -1f, 1f);
            var gp = Gamepad.current;
            if (gp != null && gp.dpad.up.isPressed) return 0.3f;
            if (gp != null && gp.dpad.down.isPressed) return -0.3f;
            return 0f;
        }

        public static bool KeyDown(KeyCode key)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (key)
            {
                case KeyCode.C: return kb.cKey.wasPressedThisFrame;
                case KeyCode.Tab: return kb.tabKey.wasPressedThisFrame;
                case KeyCode.O: return kb.oKey.wasPressedThisFrame;
                case KeyCode.P: return kb.pKey.wasPressedThisFrame;
                case KeyCode.F: return kb.fKey.wasPressedThisFrame;
                case KeyCode.H: return kb.hKey.wasPressedThisFrame;
                default: return false;
            }
        }

        public static bool GamepadButtonDown(char button)
        {
            var gp = Gamepad.current;
            if (gp == null) return false;
            switch (button)
            {
                case 'y': return gp.yButton.wasPressedThisFrame;
                case 'x': return gp.xButton.wasPressedThisFrame;
                default: return false;
            }
        }
    }
}
