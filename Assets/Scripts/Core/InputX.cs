using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Metro
{
    /// <summary>
    /// Thin wrapper over the Input System so gameplay code reads like the old Input API.
    /// It can also be driven by a simulated mouse/keyboard for automated play-tests.
    /// </summary>
    public static class InputX
    {
        // ------------------------------------------------------------------ simulation (autotests)

        public static bool simulate;
        // Changes requested by a test go into "next" and become visible at the start of the
        // following frame, so every consumer in that frame sees the same edges (down/up).
        static Vector2 simPos, nextPos;
        static bool simLeft, simRight, simMiddle, prevLeft, prevRight, prevMiddle;
        static bool nextLeft, nextRight, nextMiddle;
        static float simScroll, nextScroll;
        static readonly HashSet<Key> simKeys = new HashSet<Key>();
        static readonly HashSet<Key> simKeysPrev = new HashSet<Key>();
        static readonly HashSet<Key> nextKeys = new HashSet<Key>();

        public static void SimMouse(Vector2 screenPos, bool left = false, bool right = false, bool middle = false)
        {
            nextPos = screenPos;
            nextLeft = left;
            nextRight = right;
            nextMiddle = middle;
        }

        public static void SimScroll(float notches) => nextScroll = notches;

        public static void SimKey(Key k, bool down)
        {
            if (down) nextKeys.Add(k);
            else nextKeys.Remove(k);
        }

        /// <summary>Called once at the very start of each frame, before any input consumer.</summary>
        public static void BeginFrame()
        {
            if (!simulate) return;
            prevLeft = simLeft;
            prevRight = simRight;
            prevMiddle = simMiddle;
            simLeft = nextLeft;
            simRight = nextRight;
            simMiddle = nextMiddle;
            simPos = nextPos;
            simScroll = nextScroll;
            nextScroll = 0f;
            simKeysPrev.Clear();
            foreach (var k in simKeys) simKeysPrev.Add(k);
            simKeys.Clear();
            foreach (var k in nextKeys) simKeys.Add(k);
        }

        // ------------------------------------------------------------------ queries

        public static Vector2 MousePos => simulate ? simPos : Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static Vector2 MouseDelta => simulate ? Vector2.zero : Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

        /// <summary>Scroll in "notches" (positive = away from the user).</summary>
        public static float Scroll
        {
            get
            {
                if (simulate) return simScroll;
                var m = Mouse.current;
                if (m == null) return 0f;
                float y = m.scroll.ReadValue().y;
                if (Mathf.Abs(y) < 0.001f) return 0f;
                return Mathf.Abs(y) >= 10f ? y / 120f : y;
            }
        }

        public static bool LeftDown => simulate ? simLeft && !prevLeft : Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool LeftUp => simulate ? !simLeft && prevLeft : Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
        public static bool LeftHeld => simulate ? simLeft : Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool RightDown => simulate ? simRight && !prevRight : Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public static bool RightUp => simulate ? !simRight && prevRight : Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;
        public static bool RightHeld => simulate ? simRight : Mouse.current != null && Mouse.current.rightButton.isPressed;
        public static bool MiddleHeld => simulate ? simMiddle : Mouse.current != null && Mouse.current.middleButton.isPressed;
        public static bool MiddleDown => simulate ? simMiddle && !prevMiddle : Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame;

        public static bool Key(Key k) => simulate ? simKeys.Contains(k) : Keyboard.current != null && Keyboard.current[k].isPressed;
        public static bool KeyDown(Key k) => simulate ? simKeys.Contains(k) && !simKeysPrev.Contains(k) : Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;
        public static bool Shift => Key(UnityEngine.InputSystem.Key.LeftShift) || Key(UnityEngine.InputSystem.Key.RightShift);
        public static bool Ctrl => Key(UnityEngine.InputSystem.Key.LeftCtrl) || Key(UnityEngine.InputSystem.Key.RightCtrl);
        public static bool Alt => Key(UnityEngine.InputSystem.Key.LeftAlt) || Key(UnityEngine.InputSystem.Key.RightAlt);

        public static bool IsModifier(Key k) => k == UnityEngine.InputSystem.Key.LeftShift || k == UnityEngine.InputSystem.Key.RightShift
                                                || k == UnityEngine.InputSystem.Key.LeftCtrl || k == UnityEngine.InputSystem.Key.RightCtrl
                                                || k == UnityEngine.InputSystem.Key.LeftAlt || k == UnityEngine.InputSystem.Key.RightAlt
                                                || k == UnityEngine.InputSystem.Key.LeftWindows || k == UnityEngine.InputSystem.Key.RightWindows;

        /// <summary>A key (not a modifier) that went down this frame, real or simulated: for rebinding.</summary>
        public static bool AnyKeyDown(out Key key)
        {
            key = UnityEngine.InputSystem.Key.None;
            if (simulate)
            {
                foreach (var k in simKeys)
                    if (!simKeysPrev.Contains(k) && !IsModifier(k))
                    {
                        key = k;
                        return true;
                    }
                return false;
            }
            var kb = Keyboard.current;
            if (kb == null || !kb.anyKey.wasPressedThisFrame) return false;
            foreach (var c in kb.allKeys)
                if (c != null && c.wasPressedThisFrame && !IsModifier(c.keyCode))
                {
                    key = c.keyCode;
                    return true;
                }
            return false;
        }
    }
}
