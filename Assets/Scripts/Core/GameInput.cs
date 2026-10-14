using UnityEngine;
using UnityEngine.InputSystem;

namespace KingLastPiece
{
    /// <summary>
    /// Thin wrapper over the Input System devices (keyboard/mouse + gamepad).
    /// Souls-style layout: R1 light, R2 heavy, B/Circle dodge, R3 lock-on, X/Square flask.
    /// </summary>
    public static class GameInput
    {
        public static Vector2 Move
        {
            get
            {
                Vector2 v = Vector2.zero;
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
                }
                var gp = Gamepad.current;
                if (gp != null)
                {
                    Vector2 s = gp.leftStick.ReadValue();
                    if (s.sqrMagnitude > v.sqrMagnitude) v = s;
                }
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        /// <summary>Camera look delta in degrees for this frame.</summary>
        public static Vector2 Look
        {
            get
            {
                Vector2 v = Vector2.zero;
                var m = Mouse.current;
                if (m != null && Cursor.lockState == CursorLockMode.Locked)
                    v += m.delta.ReadValue() * 0.12f;
                var gp = Gamepad.current;
                if (gp != null)
                    v += gp.rightStick.ReadValue() * (160f * Time.unscaledDeltaTime);
                return v;
            }
        }

        public static bool LightAttack =>
            (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
            (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame);

        /// <summary>Light attack button held down (holding keeps the combo going).</summary>
        public static bool LightHeld =>
            (Mouse.current != null && Mouse.current.leftButton.isPressed && Cursor.lockState == CursorLockMode.Locked) ||
            (Keyboard.current != null && Keyboard.current.jKey.isPressed) ||
            (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed);

        /// <summary>Heavy attack button held down (holding repeats the heavy).</summary>
        public static bool HeavyHeld =>
            (Mouse.current != null && Mouse.current.rightButton.isPressed && Cursor.lockState == CursorLockMode.Locked) ||
            (Keyboard.current != null && Keyboard.current.kKey.isPressed) ||
            (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed);

        public static bool HeavyAttack =>
            (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) ||
            (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);

        public static bool Dodge =>
            (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

        public static bool LockOn =>
            (Keyboard.current != null && (Keyboard.current.qKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame)) ||
            (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.rightStickButton.wasPressedThisFrame);

        public static bool Heal =>
            (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);

        public static bool Confirm =>
            (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)) ||
            (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame));

        public static bool Pause =>
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
    }
}
