using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>Внешность NPC: цвета одежды, кожи, волос, рост. Детерминирована по seed.</summary>
    public struct NpcLook
    {
        public Color Skin, Top, Bottom, Hair, Shoes;
        public float Height;
        public bool Female;
        public bool Bag;
        public Color BagColor;

        static readonly Color[] Skins = { new Color(0.96f, 0.8f, 0.69f), new Color(0.87f, 0.68f, 0.55f), new Color(0.72f, 0.53f, 0.4f), new Color(0.55f, 0.38f, 0.27f), new Color(0.4f, 0.27f, 0.2f) };
        static readonly Color[] Tops =
        {
            new Color(0.15f, 0.2f, 0.35f), new Color(0.75f, 0.2f, 0.2f), new Color(0.9f, 0.9f, 0.88f), new Color(0.2f, 0.45f, 0.3f), new Color(0.85f, 0.65f, 0.2f),
            new Color(0.35f, 0.35f, 0.38f), new Color(0.55f, 0.35f, 0.6f), new Color(0.3f, 0.55f, 0.75f), new Color(0.1f, 0.1f, 0.1f), new Color(0.7f, 0.45f, 0.35f),
        };
        static readonly Color[] Bottoms = { new Color(0.15f, 0.18f, 0.28f), new Color(0.1f, 0.1f, 0.1f), new Color(0.45f, 0.4f, 0.32f), new Color(0.3f, 0.3f, 0.32f), new Color(0.25f, 0.35f, 0.5f) };
        static readonly Color[] Hairs = { new Color(0.1f, 0.08f, 0.06f), new Color(0.3f, 0.2f, 0.12f), new Color(0.55f, 0.4f, 0.25f), new Color(0.8f, 0.68f, 0.45f), new Color(0.6f, 0.6f, 0.6f) };

        public static NpcLook FromSeed(int seed)
        {
            var r = new DeterministicRandom((ulong)seed * 2654435761UL + 7);
            bool female = r.Chance(0.5);
            return new NpcLook
            {
                Skin = Skins[r.Range(0, Skins.Length)], Top = Tops[r.Range(0, Tops.Length)], Bottom = Bottoms[r.Range(0, Bottoms.Length)],
                Hair = Hairs[r.Range(0, Hairs.Length)], Shoes = new Color(0.08f, 0.07f, 0.06f), Female = female,
                Height = female ? r.Range(0.92f, 1.02f) : r.Range(0.97f, 1.08f),
            };
        }

        public static NpcLook Staff(int seed, bool female, Color uniform)
        {
            var look = FromSeed(seed);
            look.Female = female;
            look.Top = uniform;
            look.Bottom = new Color(0.1f, 0.1f, 0.12f);
            return look;
        }

        public static NpcLook Player(Color shirt)
        {
            var look = FromSeed(12345);
            look.Female = false;
            look.Top = shirt;
            look.Height = 1.03f;
            return look;
        }
    }

    /// <summary>
    /// Низкополигональный человек из 5 рендереров (корпус, 2 ноги, 2 руки) с общими мешами
    /// и палитрой материалов — сотни NPC укладываются в несколько SRP-батчей.
    /// </summary>
    public sealed class NpcBody : MonoBehaviour
    {
        static Mesh _torsoMale, _torsoFemale, _leg, _arm, _bagMesh;

        Transform _legL, _legR, _armL, _armR, _torso;
        float _phase;
        Renderer[] _renderers;
        bool _visible = true;

        public static NpcBody Create(Transform parent, NpcLook look)
        {
            EnsureMeshes();
            var M = MaterialLibrary.Instance;
            var root = new GameObject("NPC");
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * look.Height;
            var body = root.AddComponent<NpcBody>();
            var top = M.Paint("npc_top", Quant(look.Top), null, 1f, 0.15f);
            var bottom = M.Paint("npc_bottom", Quant(look.Bottom), null, 1f, 0.1f);
            var skin = M.Paint("npc_skin", Quant(look.Skin), null, 1f, 0.3f);
            var hair = M.Paint("npc_hair", Quant(look.Hair), null, 1f, 0.2f);
            var shoes = M.Paint("npc_shoes", look.Shoes, null, 1f, 0.4f);

            body._torso = Part(root.transform, "Torso", look.Female ? _torsoFemale : _torsoMale, new[] { top, skin, hair }, new Vector3(0, 0.95f, 0));
            body._legL = Part(root.transform, "LegL", _leg, new[] { bottom, shoes }, new Vector3(-0.1f, 0.95f, 0));
            body._legR = Part(root.transform, "LegR", _leg, new[] { bottom, shoes }, new Vector3(0.1f, 0.95f, 0));
            float shoulder = look.Female ? 0.19f : 0.22f;
            body._armL = Part(body._torso, "ArmL", _arm, new[] { top, skin }, new Vector3(-shoulder, 0.55f, 0));
            body._armR = Part(body._torso, "ArmR", _arm, new[] { top, skin }, new Vector3(shoulder, 0.55f, 0));
            body._renderers = root.GetComponentsInChildren<Renderer>();
            if (look.Bag) body.SetBag(true, look.BagColor);
            return body;
        }

        // Квантование цвета — меньше уникальных материалов.
        static Color Quant(Color c) => new Color(Mathf.Round(c.r * 20f) / 20f, Mathf.Round(c.g * 20f) / 20f, Mathf.Round(c.b * 20f) / 20f);

        static Transform Part(Transform parent, string name, Mesh mesh, Material[] mats, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go.transform;
        }

        static void EnsureMeshes()
        {
            if (_torsoMale != null) return;
            _torsoMale = BuildTorso(false);
            _torsoFemale = BuildTorso(true);

            var mb = new MeshBuilder();
            mb.AddBox(0, new Vector3(0, -0.42f, 0), new Vector3(0.15f, 0.84f, 0.17f));
            mb.AddBox(1, new Vector3(0, -0.89f, 0.04f), new Vector3(0.13f, 0.1f, 0.27f));
            _leg = mb.Build("NpcLeg");

            mb = new MeshBuilder();
            mb.AddBox(0, new Vector3(0, -0.28f, 0), new Vector3(0.1f, 0.56f, 0.11f));
            mb.AddSphere(1, new Vector3(0, -0.62f, 0), new Vector3(0.055f, 0.07f, 0.055f), 3, 5);
            _arm = mb.Build("NpcArm");

            mb = new MeshBuilder();
            mb.AddBox(0, new Vector3(0, -0.15f, 0), new Vector3(0.3f, 0.32f, 0.12f));
            _bagMesh = mb.Build("NpcBag");
        }

        static Mesh BuildTorso(bool female)
        {
            var mb = new MeshBuilder();
            float shoulders = female ? 0.36f : 0.44f;
            // Корпус: таз, грудь, плечи — сужение к талии
            mb.AddBox(0, new Vector3(0, 0.08f, 0), new Vector3(female ? 0.36f : 0.34f, 0.2f, 0.2f));
            mb.AddBox(0, new Vector3(0, 0.35f, 0), new Vector3(shoulders - 0.06f, 0.36f, 0.22f));
            mb.AddBox(0, new Vector3(0, 0.56f, 0), new Vector3(shoulders, 0.1f, 0.2f));
            mb.AddCylinder(1, new Vector3(0, 0.6f, 0), 0.05f, 0.08f, 6, false);
            mb.AddSphere(1, new Vector3(0, 0.79f, 0.01f), new Vector3(0.1f, 0.125f, 0.115f), 6, 8);
            mb.AddSphere(1, new Vector3(0, 0.79f, 0.115f), new Vector3(0.02f, 0.03f, 0.02f), 2, 4); // нос
            if (female)
            {
                mb.AddSphere(2, new Vector3(0, 0.83f, -0.01f), new Vector3(0.112f, 0.11f, 0.125f), 5, 8);
                mb.AddBox(2, new Vector3(0, 0.68f, -0.08f), new Vector3(0.2f, 0.3f, 0.08f));
            }
            else
            {
                mb.AddSphere(2, new Vector3(0, 0.85f, -0.01f), new Vector3(0.105f, 0.08f, 0.12f), 4, 8);
            }
            return mb.Build(female ? "NpcTorsoF" : "NpcTorsoM");
        }

        /// <summary>Цикл шага. speed — м/с.</summary>
        public void Walk(float speed, float dt)
        {
            _phase += dt * (1.6f + speed * 1.9f);
            float amp = Mathf.Clamp01(speed / 1.4f);
            float s = Mathf.Sin(_phase);
            _legL.localRotation = Quaternion.Euler(s * 32f * amp, 0, 0);
            _legR.localRotation = Quaternion.Euler(-s * 32f * amp, 0, 0);
            _armL.localRotation = Quaternion.Euler(-s * 26f * amp, 0, 3f);
            _armR.localRotation = Quaternion.Euler(s * 26f * amp, 0, -3f);
            _torso.localPosition = new Vector3(0, 0.95f + Mathf.Abs(Mathf.Cos(_phase)) * 0.035f * amp, 0);
        }

        public void Idle(float dt)
        {
            _phase += dt;
            float s = Mathf.Sin(_phase * 0.9f);
            _legL.localRotation = Quaternion.identity;
            _legR.localRotation = Quaternion.identity;
            _armL.localRotation = Quaternion.Euler(s * 3f, 0, 4f);
            _armR.localRotation = Quaternion.Euler(-s * 3f, 0, -4f);
            _torso.localPosition = new Vector3(0, 0.95f + s * 0.005f, 0);
            _torso.localRotation = Quaternion.Euler(0, s * 4f, 0);
        }

        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            foreach (var r in _renderers) r.enabled = visible;
        }

        Transform _bag;

        /// <summary>Пакет с покупками в правой руке (покупатели магазинов одежды и супермаркета).</summary>
        public void SetBag(bool on, Color color)
        {
            if (on && _bag == null)
            {
                EnsureMeshes();
                _bag = Part(_armR, "Bag", _bagMesh, new[] { MaterialLibrary.Instance.Paint("bag", Quant(color), null, 1f, 0.2f) }, new Vector3(0.04f, -0.62f, 0));
                _renderers = GetComponentsInChildren<Renderer>(true);
            }
            if (_bag != null)
            {
                _bag.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Instance.Paint("bag", Quant(color), null, 1f, 0.2f);
                _bag.gameObject.SetActive(on);
            }
        }


        /// <summary>Тени только у ближних NPC.</summary>
        public void SetShadows(bool on)
        {
            var mode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;
            foreach (var r in _renderers) if (r.shadowCastingMode != mode) r.shadowCastingMode = mode;
        }
    }

    public sealed class IdleAnimator : MonoBehaviour
    {
        public NpcBody Body;
        void Update() { if (Body != null) Body.Idle(Time.deltaTime); }
    }
}
