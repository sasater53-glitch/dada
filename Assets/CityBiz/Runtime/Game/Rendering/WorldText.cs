using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>Вывески и таблички на встроенном шрифте (поддерживает кириллицу).</summary>
    public static class WorldText
    {
        static Font _font;
        public const int FontSize = 64;

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// <summary>
        /// Создаёт TextMesh, читаемый со стороны -Z родителя (с улицы). height — примерная высота строки в метрах;
        /// maxWidth > 0 — текст автоматически ужимается по ширине.
        /// </summary>
        public static TextMesh Create(Transform parent, string name, string text, Vector3 localPos, float height, Color color,
            float maxWidth = 0f, TextAnchor anchor = TextAnchor.MiddleCenter, float yaw = 0f)
        {
            // 3D-текст должен перекрываться геометрией, а не рисоваться поверх стен.
            Shader.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.LessEqual);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.fontSize = FontSize;
            tm.characterSize = height / (FontSize * 0.1f);
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Font.material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (maxWidth > 0f) go.AddComponent<TextFitter>().MaxWidth = maxWidth;
            return tm;
        }

        public static void SetText(TextMesh tm, string text)
        {
            if (tm == null || tm.text == text) return;
            tm.text = text;
            var fitter = tm.GetComponent<TextFitter>();
            if (fitter != null) fitter.Refit();
        }
    }
}
