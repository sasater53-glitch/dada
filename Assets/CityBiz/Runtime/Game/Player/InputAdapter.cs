using UnityEngine;
#if ENABLE_INPUT_SYSTEM && CITYBIZ_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CityBiz.Game
{
    public enum GameKey { Interact, Portfolio, Map, Menu, Help, Debug, CameraMode, Chat, Jump, Sprint, Hire }

    /// <summary>
    /// Единая точка ввода: работает и с новой Input System (шаблоны Unity 6), и со старым Input Manager.
    /// </summary>
    public static class InputAdapter
    {
#if ENABLE_INPUT_SYSTEM && CITYBIZ_INPUT_SYSTEM
        static Key ToKey(GameKey k)
        {
            switch (k)
            {
                case GameKey.Interact: return Key.E;
                case GameKey.Portfolio: return Key.Tab;
                case GameKey.Map: return Key.M;
                case GameKey.Menu: return Key.Escape;
                case GameKey.Help: return Key.F1;
                case GameKey.Debug: return Key.F3;
                case GameKey.CameraMode: return Key.V;
                case GameKey.Chat: return Key.Enter;
                case GameKey.Jump: return Key.Space;
                case GameKey.Hire: return Key.H;
                default: return Key.LeftShift;
            }
        }

        public static bool Down(GameKey k) => Keyboard.current != null && Keyboard.current[ToKey(k)].wasPressedThisFrame;
        public static bool Held(GameKey k) => Keyboard.current != null && Keyboard.current[ToKey(k)].isPressed;

        public static Vector2 Move
        {
            get
            {
                var kb = Keyboard.current;
                if (kb == null) return Vector2.zero;
                float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            }
        }

        public static Vector2 Look => Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
        public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        public static bool RightMouseHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
        static KeyCode ToKey(GameKey k)
        {
            switch (k)
            {
                case GameKey.Interact: return KeyCode.E;
                case GameKey.Portfolio: return KeyCode.Tab;
                case GameKey.Map: return KeyCode.M;
                case GameKey.Menu: return KeyCode.Escape;
                case GameKey.Help: return KeyCode.F1;
                case GameKey.Debug: return KeyCode.F3;
                case GameKey.CameraMode: return KeyCode.V;
                case GameKey.Chat: return KeyCode.Return;
                case GameKey.Jump: return KeyCode.Space;
                case GameKey.Hire: return KeyCode.H;
                default: return KeyCode.LeftShift;
            }
        }

        public static bool Down(GameKey k) => Input.GetKeyDown(ToKey(k));
        public static bool Held(GameKey k) => Input.GetKey(ToKey(k));

        public static Vector2 Move
        {
            get
            {
                float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
                float y = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
                return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            }
        }

        public static Vector2 Look => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 2f;
        public static float Scroll => Input.mouseScrollDelta.y;
        public static bool RightMouseHeld => Input.GetMouseButton(1);
#endif
    }
}
