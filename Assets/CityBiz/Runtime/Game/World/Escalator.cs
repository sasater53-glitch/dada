using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Эскалатор: балюстрады со стеклом, поручни, движущиеся ступени (анимация сдвигом меша ступеней),
    /// наклонный коллайдер и «конвейер», который везёт стоящего на нём игрока.
    /// </summary>
    public sealed class Escalator : MonoBehaviour
    {
        public const float Speed = 0.55f;
        const float StepDepth = 0.4f;

        [SerializeField] Transform _steps;
        [SerializeField] Vector3 _bottom, _top;
        [SerializeField] bool _up;
        [SerializeField] Vector3 _stepDir;
        float _phase;

        public Vector3 Direction => (_up ? 1f : -1f) * (_top - _bottom).normalized;
        public bool GoingUp => _up;
        public Vector3 BottomWorld => transform.TransformPoint(_bottom);
        public Vector3 TopWorld => transform.TransformPoint(_top);

        public void Build(MallKit kit, Vector3 bottom, Vector3 top, bool goingUp)
        {
            _bottom = bottom; _top = top; _up = goingUp;
            var M = kit.M;
            var dir = top - bottom;
            var flat = new Vector3(dir.x, 0, dir.z);
            float run = flat.magnitude, rise = dir.y;
            var fwd = flat / run;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(0, yaw, 0);
            float width = 1.0f;
            const float landing = 1.4f;

            // Корпус: балюстрады и днище (одним мешем)
            var mb = new MeshBuilder();
            var right = rot * Vector3.right;
            var start = bottom - fwd * landing;
            var end = top + fwd * landing;
            foreach (float side in new[] { -1f, 1f })
            {
                var off = right * side * (width * 0.5f + 0.08f);
                // Стеклянная панель вдоль наклона
                var a = bottom + off + Vector3.up * 0.15f;
                var b = top + off + Vector3.up * 0.15f;
                AddPanel(mb, 1, a, b, 0.9f, side);
                // Нижняя обшивка (боковина) — алюминий
                AddPanel(mb, 0, start + off + Vector3.down * 0.05f, bottom + off + Vector3.down * 0.05f, 0.2f, side);
                AddSlope(mb, 0, bottom + off, top + off, side);
                AddPanel(mb, 0, top + off + Vector3.down * 0.05f, end + off + Vector3.down * 0.05f, 0.2f, side);
                // Поручень (чёрная резина)
                var hr0 = start + off + Vector3.up * 1.0f;
                var hr1 = bottom + off + Vector3.up * 1.0f;
                var hr2 = top + off + Vector3.up * 1.0f;
                var hr3 = end + off + Vector3.up * 1.0f;
                Rod(mb, 2, hr0, hr1, 0.07f);
                Rod(mb, 2, hr1, hr2, 0.07f);
                Rod(mb, 2, hr2, hr3, 0.07f);
            }
            // Днище под наклоном (видно с нижнего этажа)
            var under0 = bottom + Vector3.down * 0.6f;
            var under1 = top + Vector3.down * 0.6f;
            mb.AddQuad(0, under0 - right * 0.7f, under0 + right * 0.7f, under1 + right * 0.7f, under1 - right * 0.7f);
            // Площадки входа/выхода (гребёнка)
            mb.AddBox(3, start + fwd * landing * 0.5f + Vector3.up * 0.01f, new Vector3(width + 0.1f, 0.02f, landing), yaw);
            mb.AddBox(3, end - fwd * landing * 0.5f + Vector3.up * 0.01f, new Vector3(width + 0.1f, 0.02f, landing), yaw);
            kit.MeshObject(transform, "Body", mb, new[] { M.Aluminium, M.RailGlass, M.Rubber, M.MetalPanel });

            // Ступени: меш лесенки вдоль наклона, сдвигается на шаг по кругу
            float slope = rise / run;
            int count = Mathf.CeilToInt(run / StepDepth) + 1;
            var steps = new MeshBuilder();
            for (int i = 0; i < count; i++)
            {
                float z = i * StepDepth;
                var p = new Vector3(0, z * slope, z);
                steps.AddBox(0, p + new Vector3(0, -0.04f, StepDepth * 0.5f), new Vector3(width, 0.08f, StepDepth - 0.02f));
                steps.AddBox(1, p + new Vector3(0, -StepDepth * slope * 0.5f, 0.01f), new Vector3(width, StepDepth * slope, 0.02f));
            }
            var stepsGo = kit.MeshObject(transform, "Steps", steps, new[] { M.MetalDark, M.Aluminium }, false);
            _steps = stepsGo.transform;
            _steps.localPosition = bottom;
            _steps.localRotation = rot;
            _stepDir = new Vector3(0, slope, 1f).normalized * StepDepth / Mathf.Cos(Mathf.Atan(slope));

            // Наклонный коллайдер и плоские площадки
            float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            float len = Mathf.Sqrt(run * run + rise * rise);
            var ramp = new GameObject("Ramp");
            ramp.transform.SetParent(transform, false);
            ramp.transform.localPosition = (bottom + top) * 0.5f + Vector3.down * 0.1f;
            ramp.transform.localRotation = rot * Quaternion.Euler(-angle, 0, 0);
            var rc = ramp.AddComponent<BoxCollider>();
            rc.size = new Vector3(width + 0.3f, 0.2f, len);
            ramp.AddComponent<EscalatorSurface>().Owner = this;
            foreach (float side in new[] { -1f, 1f })
            {
                var wall = new GameObject("Side");
                wall.transform.SetParent(transform, false);
                wall.transform.localPosition = (bottom + top) * 0.5f + right * side * (width * 0.5f + 0.15f) + Vector3.up * 0.8f;
                wall.transform.localRotation = rot * Quaternion.Euler(-angle, 0, 0);
                wall.AddComponent<BoxCollider>().size = new Vector3(0.1f, 2f, len + landing);
            }
        }

        static void AddPanel(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float height, float side)
        {
            var up = Vector3.up * height;
            if (side > 0) { mb.AddQuad(sub, a, a + up, b + up, b); mb.AddQuad(sub, b, b + up, a + up, a); }
            else { mb.AddQuad(sub, b, b + up, a + up, a); mb.AddQuad(sub, a, a + up, b + up, b); }
        }

        static void AddSlope(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float side)
        {
            var down = Vector3.down * 0.6f;
            mb.AddQuad(sub, a + down, a, b, b + down);
            mb.AddQuad(sub, b + down, b, a, a + down);
        }

        static void Rod(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float thickness)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            var saved = mb.Transform;
            mb.Transform = saved * Matrix4x4.TRS((a + b) * 0.5f, Quaternion.LookRotation(d / len, Vector3.up), Vector3.one);
            mb.AddBox(sub, Vector3.zero, new Vector3(thickness, thickness, len));
            mb.Transform = saved;
        }

        void Update()
        {
            if (_steps == null) return;
            _phase = Mathf.Repeat(_phase + Time.deltaTime * Speed / StepDepth * (_up ? 1f : -1f), 1f);
            _steps.localPosition = _bottom + (_steps.localRotation * _stepDir) * (_phase - 1f);
        }
    }

    /// <summary>Метка на коллайдере полотна эскалатора — по ней контроллер игрока получает скорость ленты.</summary>
    public sealed class EscalatorSurface : MonoBehaviour
    {
        public Escalator Owner;
    }
}
