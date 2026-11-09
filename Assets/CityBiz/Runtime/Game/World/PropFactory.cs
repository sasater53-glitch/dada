using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>Модель пропса для инстансинга: общий меш, материалы по подмешам, упрощённый коллайдер.</summary>
    public sealed class PropModel
    {
        public Mesh Mesh;
        public Material[] Materials;
        public Vector3 ColliderCenter, ColliderSize;
        public bool Shadows = true;
        public float MaxDistance = 220f;
    }

    /// <summary>Уличная мебель и элементы благоустройства. Меши строятся один раз и переиспользуются.</summary>
    public sealed class PropFactory
    {
        readonly MaterialLibrary M;
        readonly Dictionary<long, PropModel> _cache = new Dictionary<long, PropModel>();
        public static readonly Vector3 LampHeadLocal = new Vector3(0, 6.7f, 1.6f);

        public PropFactory(MaterialLibrary m) { M = m; }

        public static bool IsInstanced(PropKind k) =>
            k != PropKind.RoadSign && k != PropKind.Billboard && k != PropKind.Kiosk && k != PropKind.PhoneBooth;

        public PropModel Get(PropKind kind, int variant)
        {
            long key = ((long)kind << 8) | (uint)(variant & 0xFF);
            if (_cache.TryGetValue(key, out var model)) return model;
            var mb = new MeshBuilder();
            var mats = new List<Material>();
            int Sub(Material m) { int i = mats.IndexOf(m); if (i >= 0) return i; mats.Add(m); return mats.Count - 1; }
            model = new PropModel();
            switch (kind)
            {
                case PropKind.StreetLamp:
                {
                    int metal = Sub(M.Paint("lamp_pole", new Color(0.16f, 0.18f, 0.2f), null, 1f, 0.5f));
                    int lens = Sub(M.LampLens);
                    mb.AddCylinder(metal, Vector3.zero, 0.17f, 0.7f, 10);
                    mb.AddCylinder(metal, new Vector3(0, 0.7f, 0), 0.1f, 6.3f, 8, true, 0.07f);
                    mb.AddBox(metal, new Vector3(0, 6.95f, 0.8f), new Vector3(0.07f, 0.07f, 1.6f));
                    mb.AddBox(metal, new Vector3(0, 6.88f, LampHeadLocal.z), new Vector3(0.34f, 0.14f, 0.75f));
                    mb.AddBox(lens, new Vector3(0, 6.79f, LampHeadLocal.z), new Vector3(0.26f, 0.04f, 0.6f), 0f, true);
                    model.ColliderCenter = new Vector3(0, 1.5f, 0); model.ColliderSize = new Vector3(0.3f, 3f, 0.3f);
                    break;
                }
                case PropKind.Tree:
                {
                    var rng = new DeterministicRandom((ulong)(variant + 11));
                    int bark = Sub(M.Bark), leaves = Sub(M.Leaves(variant)), grate = Sub(M.MetalDark);
                    float trunkH = variant == 1 ? 4.2f : 3.0f;
                    mb.AddBox(grate, new Vector3(0, 0.01f, 0), new Vector3(1.3f, 0.02f, 1.3f));
                    mb.AddCylinder(bark, Vector3.zero, 0.17f, trunkH, 8, false, 0.11f);
                    // Ветви и крона из нескольких эллипсоидов
                    mb.AddCylinder(bark, new Vector3(0, trunkH * 0.7f, 0), 0.07f, 1.4f, 5, false, 0.03f);
                    int blobs = variant == 2 ? 6 : 5;
                    for (int i = 0; i < blobs; i++)
                    {
                        float a = i * 2.4f + rng.Range(0f, 0.6f);
                        float r = variant == 1 ? 0.6f : 1.1f;
                        var c = new Vector3(Mathf.Cos(a) * r * rng.Range(0.4f, 1f), trunkH + rng.Range(0.3f, 1.8f), Mathf.Sin(a) * r * rng.Range(0.4f, 1f));
                        var size = variant == 1 ? new Vector3(1.1f, 1.8f, 1.1f) : new Vector3(1.6f, 1.3f, 1.6f) * rng.Range(0.85f, 1.15f);
                        mb.AddSphere(leaves, c, size, 5, 7);
                    }
                    mb.AddSphere(leaves, new Vector3(0, trunkH + (variant == 1 ? 2.6f : 1.6f), 0), variant == 1 ? new Vector3(1f, 2f, 1f) : new Vector3(1.7f, 1.4f, 1.7f), 5, 8);
                    model.ColliderCenter = new Vector3(0, 1.5f, 0); model.ColliderSize = new Vector3(0.4f, 3f, 0.4f);
                    model.MaxDistance = 300f;
                    break;
                }
                case PropKind.Bench:
                {
                    int wood = Sub(M.Wood), iron = Sub(M.MetalDark);
                    for (int i = 0; i < 3; i++) mb.AddBox(wood, new Vector3(0, 0.45f, -0.15f + i * 0.15f), new Vector3(1.8f, 0.04f, 0.11f));
                    for (int i = 0; i < 2; i++) mb.AddBox(wood, new Vector3(0, 0.65f + i * 0.17f, 0.24f), new Vector3(1.8f, 0.1f, 0.03f));
                    foreach (float x in new[] { -0.75f, 0.75f })
                    {
                        mb.AddBox(iron, new Vector3(x, 0.22f, -0.15f), new Vector3(0.05f, 0.44f, 0.05f));
                        mb.AddBox(iron, new Vector3(x, 0.45f, 0.24f), new Vector3(0.05f, 0.9f, 0.05f));
                        mb.AddBox(iron, new Vector3(x, 0.42f, 0.04f), new Vector3(0.05f, 0.04f, 0.45f));
                        mb.AddBox(iron, new Vector3(x, 0.62f, -0.05f), new Vector3(0.05f, 0.04f, 0.35f));
                    }
                    model.ColliderCenter = new Vector3(0, 0.45f, 0.05f); model.ColliderSize = new Vector3(1.8f, 0.9f, 0.55f);
                    break;
                }
                case PropKind.TrashBin:
                {
                    int body = Sub(M.Paint("bin", new Color(0.18f, 0.3f, 0.22f), null, 1f, 0.4f)), metal = Sub(M.MetalDark);
                    mb.AddCylinder(body, Vector3.zero, 0.24f, 0.85f, 12, false);
                    mb.AddCylinder(metal, new Vector3(0, 0.85f, 0), 0.27f, 0.08f, 12, true, 0.2f);
                    model.ColliderCenter = new Vector3(0, 0.45f, 0); model.ColliderSize = new Vector3(0.5f, 0.9f, 0.5f);
                    model.MaxDistance = 140f;
                    break;
                }
                case PropKind.Bollard:
                {
                    int body = Sub(M.Paint("bollard", new Color(0.15f, 0.15f, 0.16f), null, 1f, 0.5f)), stripe = Sub(M.RoadPaint);
                    mb.AddCylinder(body, Vector3.zero, 0.09f, 0.85f, 8, false);
                    mb.AddCylinder(stripe, new Vector3(0, 0.68f, 0), 0.095f, 0.07f, 8, false);
                    mb.AddSphere(body, new Vector3(0, 0.85f, 0), new Vector3(0.09f, 0.06f, 0.09f), 3, 8);
                    model.ColliderCenter = new Vector3(0, 0.45f, 0); model.ColliderSize = new Vector3(0.2f, 0.9f, 0.2f);
                    model.MaxDistance = 120f; model.Shadows = false;
                    break;
                }
                case PropKind.Planter:
                {
                    int box = Sub(M.Paint("planter", new Color(0.55f, 0.53f, 0.5f), "concrete", 1f, 0.2f)), soil = Sub(M.Soil), leaves = Sub(M.Leaves(1));
                    int flowers = Sub(M.Paint("flowers", variant == 1 ? new Color(0.95f, 0.85f, 0.3f) : new Color(0.85f, 0.25f, 0.35f), "leaves", 0.5f, 0.1f));
                    mb.AddBox(box, new Vector3(0, 0.3f, 0), new Vector3(1.2f, 0.6f, 0.6f));
                    mb.AddBox(soil, new Vector3(0, 0.6f, 0), new Vector3(1.1f, 0.02f, 0.5f));
                    for (int i = 0; i < 4; i++) mb.AddSphere(i % 2 == 0 ? leaves : flowers, new Vector3(-0.4f + i * 0.27f, 0.75f, 0), new Vector3(0.22f, 0.2f, 0.22f), 4, 6);
                    model.ColliderCenter = new Vector3(0, 0.4f, 0); model.ColliderSize = new Vector3(1.2f, 0.8f, 0.6f);
                    break;
                }
                case PropKind.Hydrant:
                {
                    int red = Sub(M.Paint("hydrant", new Color(0.75f, 0.1f, 0.08f), null, 1f, 0.5f));
                    mb.AddCylinder(red, Vector3.zero, 0.13f, 0.6f, 10);
                    mb.AddSphere(red, new Vector3(0, 0.6f, 0), new Vector3(0.13f, 0.1f, 0.13f), 3, 10);
                    mb.AddBox(red, new Vector3(0, 0.4f, 0), new Vector3(0.42f, 0.09f, 0.09f));
                    model.ColliderCenter = new Vector3(0, 0.35f, 0); model.ColliderSize = new Vector3(0.3f, 0.7f, 0.3f);
                    model.MaxDistance = 100f; model.Shadows = false;
                    break;
                }
                case PropKind.BikeRack:
                {
                    int metal = Sub(M.Aluminium);
                    for (int i = 0; i < 4; i++)
                    {
                        float x = -0.9f + i * 0.6f;
                        mb.AddBox(metal, new Vector3(x, 0.4f, -0.3f), new Vector3(0.05f, 0.8f, 0.05f));
                        mb.AddBox(metal, new Vector3(x, 0.4f, 0.3f), new Vector3(0.05f, 0.8f, 0.05f));
                        mb.AddBox(metal, new Vector3(x, 0.8f, 0f), new Vector3(0.05f, 0.05f, 0.65f));
                    }
                    model.ColliderCenter = new Vector3(0, 0.4f, 0); model.ColliderSize = new Vector3(2f, 0.8f, 0.7f);
                    model.MaxDistance = 120f;
                    break;
                }
                case PropKind.ParkingMeter:
                {
                    int metal = Sub(M.MetalDark), body = Sub(M.Paint("meter", new Color(0.2f, 0.35f, 0.6f), null, 1f, 0.5f));
                    mb.AddCylinder(metal, Vector3.zero, 0.05f, 1.1f, 6, false);
                    mb.AddBox(body, new Vector3(0, 1.35f, 0), new Vector3(0.3f, 0.5f, 0.22f));
                    mb.AddBox(Sub(M.Emission("meter_screen", new Color(0.4f, 0.8f, 1f), 0.8f)), new Vector3(0, 1.42f, -0.115f), new Vector3(0.18f, 0.1f, 0.01f));
                    model.ColliderCenter = new Vector3(0, 0.8f, 0); model.ColliderSize = new Vector3(0.3f, 1.6f, 0.3f);
                    model.MaxDistance = 100f; model.Shadows = false;
                    break;
                }
                case PropKind.Hedge:
                {
                    int leaves = Sub(M.Leaves(2));
                    mb.AddBox(leaves, new Vector3(0, 0.5f, 0), new Vector3(4.2f, 1f, 0.9f));
                    for (int i = 0; i < 5; i++) mb.AddSphere(leaves, new Vector3(-1.7f + i * 0.85f, 1f, 0), new Vector3(0.5f, 0.2f, 0.45f), 3, 6);
                    model.ColliderCenter = new Vector3(0, 0.5f, 0); model.ColliderSize = new Vector3(4.2f, 1f, 0.9f);
                    break;
                }
                case PropKind.FlowerBed:
                {
                    int soil = Sub(M.Soil), leaves = Sub(M.Leaves(1));
                    var rng = new DeterministicRandom((ulong)(variant + 3));
                    mb.AddBox(Sub(M.Curb), new Vector3(0, 0.1f, 0), new Vector3(2.6f, 0.2f, 1.4f));
                    mb.AddBox(soil, new Vector3(0, 0.2f, 0), new Vector3(2.4f, 0.02f, 1.2f));
                    var colors = new[] { new Color(0.9f, 0.2f, 0.3f), new Color(0.95f, 0.8f, 0.2f), new Color(0.6f, 0.3f, 0.8f), new Color(0.98f, 0.98f, 0.95f) };
                    for (int i = 0; i < 18; i++)
                    {
                        var p = new Vector3(rng.Range(-1.1f, 1.1f), 0.3f, rng.Range(-0.5f, 0.5f));
                        mb.AddSphere(i % 3 == 0 ? leaves : Sub(M.Paint("flowers", colors[rng.Range(0, colors.Length)], "leaves", 0.5f, 0.1f)), p, Vector3.one * rng.Range(0.1f, 0.18f), 3, 5);
                    }
                    model.ColliderCenter = new Vector3(0, 0.15f, 0); model.ColliderSize = new Vector3(2.6f, 0.3f, 1.4f);
                    model.MaxDistance = 150f; model.Shadows = false;
                    break;
                }
                case PropKind.Mailbox:
                {
                    int blue = Sub(M.Paint("mailbox", new Color(0.1f, 0.3f, 0.65f), null, 1f, 0.5f)), metal = Sub(M.MetalDark);
                    mb.AddCylinder(metal, Vector3.zero, 0.04f, 0.9f, 6, false);
                    mb.AddBox(blue, new Vector3(0, 1.15f, 0), new Vector3(0.45f, 0.55f, 0.3f));
                    mb.AddBox(metal, new Vector3(0, 1.3f, -0.155f), new Vector3(0.25f, 0.03f, 0.01f));
                    model.ColliderCenter = new Vector3(0, 0.7f, 0); model.ColliderSize = new Vector3(0.45f, 1.4f, 0.3f);
                    model.MaxDistance = 100f;
                    break;
                }
                default:
                    mb.AddBox(Sub(M.Concrete), new Vector3(0, 0.5f, 0), Vector3.one);
                    model.ColliderCenter = new Vector3(0, 0.5f, 0); model.ColliderSize = Vector3.one;
                    break;
            }
            model.Mesh = mb.Build("Prop_" + kind + "_" + variant);
            model.Materials = mats.ToArray();
            _cache[key] = model;
            return model;
        }

        // ---------- Уникальные объекты с текстом ----------

        GameObject MeshObject(Transform parent, string name, MeshBuilder mb, List<Material> mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build(name);
            go.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            return go;
        }

        public GameObject CreateUnique(PropSpec spec, Transform parent)
        {
            var root = new GameObject(spec.Kind.ToString());
            root.transform.SetParent(parent, false);
            root.transform.localPosition = spec.Position;
            root.transform.localRotation = Quaternion.Euler(0, spec.Yaw, 0);
            var mb = new MeshBuilder();
            var mats = new List<Material>();
            int Sub(Material m) { int i = mats.IndexOf(m); if (i >= 0) return i; mats.Add(m); return mats.Count - 1; }
            var col = root.AddComponent<BoxCollider>();
            switch (spec.Kind)
            {
                case PropKind.RoadSign:
                {
                    int metal = Sub(M.Aluminium);
                    bool parking = spec.Variant == 1;
                    mb.AddCylinder(metal, Vector3.zero, 0.04f, 2.8f, 6, false);
                    int plate = Sub(parking ? M.Paint("sign_blue", new Color(0.1f, 0.3f, 0.7f), null, 1f, 0.6f) : M.Paint("sign_street", new Color(0.12f, 0.22f, 0.45f), null, 1f, 0.6f));
                    if (parking) mb.AddBox(plate, new Vector3(0, 2.6f, 0), new Vector3(0.6f, 0.6f, 0.04f));
                    else mb.AddBox(plate, new Vector3(0, 2.75f, 0), new Vector3(1.6f, 0.36f, 0.04f));
                    WorldText.Create(root.transform, "Text", spec.Text, new Vector3(0, parking ? 2.6f : 2.75f, -0.03f), parking ? 0.4f : 0.17f, Color.white, parking ? 0.5f : 1.45f);
                    var back = WorldText.Create(root.transform, "TextBack", spec.Text, new Vector3(0, parking ? 2.6f : 2.75f, 0.03f), parking ? 0.4f : 0.17f, Color.white, parking ? 0.5f : 1.45f);
                    back.transform.localRotation = Quaternion.Euler(0, 180, 0);
                    col.center = new Vector3(0, 1.4f, 0); col.size = new Vector3(0.15f, 2.8f, 0.15f);
                    break;
                }
                case PropKind.Billboard:
                {
                    int metal = Sub(M.MetalDark);
                    mb.AddBox(metal, new Vector3(-2.5f, 2.5f, 0.3f), new Vector3(0.3f, 5f, 0.3f));
                    mb.AddBox(metal, new Vector3(2.5f, 2.5f, 0.3f), new Vector3(0.3f, 5f, 0.3f));
                    mb.AddBox(Sub(M.Paint("billboard_frame", new Color(0.9f, 0.9f, 0.88f), null, 1f, 0.4f)), new Vector3(0, 6.2f, 0.2f), new Vector3(8.4f, 3.4f, 0.3f));
                    mb.AddBox(Sub(M.Emission("billboard_face", new Color(0.12f, 0.35f, 0.55f), 0.35f)), new Vector3(0, 6.2f, 0.04f), new Vector3(8f, 3f, 0.02f));
                    for (int i = -1; i <= 1; i += 2)
                        mb.AddBox(Sub(M.LampLens), new Vector3(i * 2.5f, 4.4f, -0.6f), new Vector3(0.3f, 0.12f, 0.2f));
                    WorldText.Create(root.transform, "Text", spec.Text, new Vector3(0, 6.2f, 0.01f), 0.75f, new Color(1f, 0.95f, 0.75f), 7.4f);
                    col.center = new Vector3(0, 2.5f, 0.3f); col.size = new Vector3(5.4f, 5f, 0.4f);
                    break;
                }
                case PropKind.Kiosk:
                {
                    var awning = spec.Variant == 1 ? new Color(0.9f, 0.45f, 0.6f) : new Color(0.2f, 0.5f, 0.3f);
                    mb.AddBox(Sub(M.Paint("kiosk", new Color(0.92f, 0.9f, 0.85f), "plaster", 2f, 0.3f)), new Vector3(0, 1.2f, 0), new Vector3(2.6f, 2.4f, 2f));
                    mb.AddBox(Sub(M.StoreGlass), new Vector3(0, 1.4f, -1.01f), new Vector3(2f, 1f, 0.02f));
                    mb.AddBox(Sub(M.Paint("awning", awning, null, 1f, 0.05f)), new Vector3(0, 2.5f, -1.3f), new Vector3(2.9f, 0.08f, 0.8f));
                    mb.AddBox(Sub(M.Wood), new Vector3(0, 0.9f, -1.15f), new Vector3(2.4f, 0.05f, 0.35f));
                    int goods = Sub(M.Paint("flowers", spec.Variant == 1 ? new Color(0.95f, 0.95f, 0.9f) : new Color(0.9f, 0.25f, 0.35f), "leaves", 0.5f, 0.1f));
                    for (int i = 0; i < 8; i++) mb.AddSphere(goods, new Vector3(-1f + i * 0.28f, 1.0f, -1.15f), Vector3.one * 0.12f, 3, 5);
                    mb.AddBox(Sub(M.Paint("roof", new Color(0.3f, 0.3f, 0.32f), null, 1f, 0.3f)), new Vector3(0, 2.45f, 0), new Vector3(2.8f, 0.1f, 2.2f));
                    WorldText.Create(root.transform, "Text", spec.Text, new Vector3(0, 2.15f, -1.02f), 0.28f, new Color(0.15f, 0.15f, 0.15f), 2f);
                    col.center = new Vector3(0, 1.2f, 0); col.size = new Vector3(2.6f, 2.4f, 2f);
                    break;
                }
                case PropKind.PhoneBooth:
                {
                    mb.AddBox(Sub(M.Paint("booth", new Color(0.7f, 0.15f, 0.12f), null, 1f, 0.5f)), new Vector3(0, 2.25f, 0), new Vector3(1f, 0.25f, 1f));
                    foreach (float x in new[] { -0.45f, 0.45f }) foreach (float z in new[] { -0.45f, 0.45f })
                        mb.AddBox(Sub(M.Paint("booth", new Color(0.7f, 0.15f, 0.12f), null, 1f, 0.5f)), new Vector3(x, 1.1f, z), new Vector3(0.08f, 2.2f, 0.08f));
                    mb.AddBox(Sub(M.StoreGlass), new Vector3(0, 1.2f, 0.45f), new Vector3(0.85f, 1.8f, 0.02f));
                    mb.AddBox(Sub(M.MetalDark), new Vector3(0, 1.4f, 0.38f), new Vector3(0.3f, 0.45f, 0.1f));
                    col.center = new Vector3(0, 1.2f, 0); col.size = new Vector3(1f, 2.4f, 1f);
                    break;
                }
            }
            var mesh = MeshObject(root.transform, "Mesh", mb, mats);
            return root;
        }

        /// <summary>Павильон остановки: стекло, скамья, рекламный лайтбокс, табличка с названием и маршрутом.</summary>
        public GameObject CreateBusStop(BusStopSpec spec, Transform parent)
        {
            var root = new GameObject("BusStop_" + spec.Name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = spec.Position;
            root.transform.localRotation = Quaternion.Euler(0, spec.Yaw, 0);
            var mb = new MeshBuilder();
            var mats = new List<Material>();
            int Sub(Material m) { int i = mats.IndexOf(m); if (i >= 0) return i; mats.Add(m); return mats.Count - 1; }
            int frame = Sub(M.Paint("stop_frame", new Color(0.2f, 0.25f, 0.3f), null, 1f, 0.5f));
            int glass = Sub(M.StoreGlass), roof = Sub(M.Paint("stop_roof", new Color(0.85f, 0.87f, 0.9f), null, 1f, 0.5f));
            float w = 4.4f, d = 1.6f, h = 2.6f;
            // Открытая сторона — к дороге (-Z), задняя стенка — стекло
            foreach (float x in new[] { -w * 0.5f, w * 0.5f })
            {
                mb.AddBox(frame, new Vector3(x, h * 0.5f, d * 0.5f), new Vector3(0.08f, h, 0.08f));
                mb.AddBox(frame, new Vector3(x, h * 0.5f, -d * 0.4f), new Vector3(0.08f, h, 0.08f));
                mb.AddBox(glass, new Vector3(x, h * 0.55f, 0.05f), new Vector3(0.02f, h * 0.8f, d * 0.85f));
            }
            mb.AddBox(glass, new Vector3(-0.6f, h * 0.55f, d * 0.5f), new Vector3(w - 1.4f, h * 0.8f, 0.02f));
            mb.AddBox(Sub(M.Emission("ad_lightbox", new Color(0.95f, 0.85f, 0.6f), 0.9f)), new Vector3(w * 0.5f - 0.65f, 1.3f, d * 0.5f), new Vector3(1.2f, 1.8f, 0.12f));
            mb.AddBox(roof, new Vector3(0, h + 0.06f, 0.05f), new Vector3(w + 0.4f, 0.12f, d + 0.5f));
            mb.AddBox(Sub(M.Wood), new Vector3(-0.6f, 0.46f, d * 0.3f), new Vector3(2.4f, 0.06f, 0.4f));
            mb.AddBox(frame, new Vector3(-0.6f, 0.23f, d * 0.3f), new Vector3(2.2f, 0.46f, 0.05f));
            // Столб с табличкой «А» и названием
            mb.AddCylinder(frame, new Vector3(w * 0.5f + 0.6f, 0, -0.5f), 0.05f, 3f, 6, false);
            mb.AddBox(Sub(M.Paint("stop_sign", new Color(0.15f, 0.55f, 0.25f), null, 1f, 0.5f)), new Vector3(w * 0.5f + 0.6f, 2.8f, -0.5f), new Vector3(0.6f, 0.6f, 0.04f));
            MeshObject(root.transform, "Mesh", mb, mats);
            WorldText.Create(root.transform, "Letter", "А", new Vector3(w * 0.5f + 0.6f, 2.8f, -0.53f), 0.42f, Color.white, 0.5f);
            WorldText.Create(root.transform, "Name", "Остановка «" + spec.Name + "»\nАвтобус 14 · каждые 8 мин", new Vector3(0, h - 0.2f, -0.35f), 0.13f, Color.white, w - 0.6f);
            var c1 = root.AddComponent<BoxCollider>();
            c1.center = new Vector3(0, h * 0.5f, d * 0.5f); c1.size = new Vector3(w, h, 0.1f);
            var c2 = root.AddComponent<BoxCollider>();
            c2.center = new Vector3(0, h + 0.06f, 0.05f); c2.size = new Vector3(w + 0.4f, 0.12f, d + 0.5f);
            return root;
        }
    }
}
