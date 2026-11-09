using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Превращает DistrictLayout в сцену. Используется и во время игры (быстрая генерация),
    /// и в редакторе (запекание статики + Occlusion Culling).
    /// </summary>
    public sealed class DistrictBuilder
    {
        const float Walk = 0.15f;
        readonly MaterialLibrary M;
        readonly BuildingFactory _buildings;
        readonly PropFactory _props;
        readonly WorldDefinition _world;

        public DistrictBuilder(MaterialLibrary materials, WorldDefinition world)
        {
            M = materials;
            _world = world;
            _buildings = new BuildingFactory(materials);
            _props = new PropFactory(materials);
        }

        public DistrictInstance Build(DistrictBlueprint blueprint, Transform parent)
        {
            var layout = blueprint.CreateLayout();
            var root = new GameObject("District_" + layout.DistrictId);
            root.transform.SetParent(parent, false);
            root.transform.position = blueprint.WorldOffset;
            var inst = root.AddComponent<DistrictInstance>();
            inst.DistrictId = layout.DistrictId;
            inst.DisplayName = layout.DisplayName;
            inst.Bounds = layout.Bounds;
            inst.PlayerSpawn = layout.PlayerSpawn;
            inst.PlayerSpawnYaw = layout.PlayerSpawnYaw;
            inst.Pedestrians = layout.Pedestrians;
            inst.Lanes = layout.Lanes;

            var statics = new GameObject("Static").transform;
            statics.SetParent(root.transform, false);
            BuildGround(layout, statics);
            BuildStreets(layout, statics, inst);

            var buildings = new GameObject("Buildings").transform;
            buildings.SetParent(statics, false);
            foreach (var b in layout.Buildings)
            {
                _buildings.Build(b, buildings);
                AddMapBuilding(inst, b, 2);
            }
            var backdrop = new GameObject("Backdrop").transform;
            backdrop.SetParent(statics, false);
            foreach (var b in layout.BackdropBuildings)
            {
                _buildings.Build(b, backdrop);
                AddMapBuilding(inst, b, 5);
            }

            var instanced = root.AddComponent<InstancedRenderer>();
            var propColliders = new GameObject("PropColliders").transform;
            propColliders.SetParent(statics, false);
            var unique = new GameObject("Props").transform;
            unique.SetParent(statics, false);

            foreach (var lot in layout.ParkingLots) BuildParkingLot(lot, statics, instanced, propColliders, inst);
            foreach (var park in layout.Parks) BuildPark(park, statics, instanced, propColliders, inst);
            foreach (var p in layout.Props) PlaceProp(p, instanced, propColliders, unique, inst);
            foreach (var stop in layout.BusStops)
            {
                _props.CreateBusStop(stop, unique);
                inst.BusStops.Add(new BusStopRef { Lane = stop.Lane, Distance = stop.LaneDistance, Position = stop.Position, Name = stop.Name });
            }
            foreach (var tl in layout.TrafficLights) inst.TrafficLights.Add(TrafficLightView.Create(M, unique, tl));
            BuildParkedCars(layout, root.transform);
            BuildBoundary(layout, root.transform);
            LinkDoors(inst);

            foreach (var kv in M.AllMaterials) inst.Materials.Add(new MaterialEntry { Key = kv.Key, Material = kv.Value });
            return inst;
        }

        // ---------- Земля, кварталы ----------

        GameObject Surface(Transform parent, string name, MeshBuilder mb, List<Material> mats, bool shadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.Build(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats.ToArray();
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        static void SlabCollider(GameObject go, Rect r, float top, float thickness = 1f)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(r.center.x, top - thickness * 0.5f, r.center.y);
            bc.size = new Vector3(r.width, thickness, r.height);
        }

        static void TopQuad(MeshBuilder mb, int sub, Rect r, float y) =>
            mb.AddQuad(sub, new Vector3(r.xMin, y, r.yMin), new Vector3(r.xMin, y, r.yMax), new Vector3(r.xMax, y, r.yMax), new Vector3(r.xMax, y, r.yMin));

        void BuildGround(DistrictLayout L, Transform parent)
        {
            var mb = new MeshBuilder();
            var mats = new List<Material> { M.Paint("field", new Color(0.4f, 0.5f, 0.3f), "grass", 8f, 0.05f), M.Paint("yard", new Color(0.58f, 0.57f, 0.55f), "concrete", 5f, 0.1f) };
            TopQuad(mb, 0, L.GroundBounds, -0.03f);
            foreach (var r in L.PlainGround) mb.AddBox(1, new Vector3(r.center.x, Walk * 0.5f, r.center.y), new Vector3(r.width, Walk, r.height));
            var go = Surface(parent, "Ground", mb, mats);
            SlabCollider(go, L.GroundBounds, -0.03f);
            foreach (var r in L.PlainGround) SlabCollider(go, r, Walk);
        }

        // ---------- Дороги ----------

        void BuildStreets(DistrictLayout L, Transform parent, DistrictInstance inst)
        {
            var mb = new MeshBuilder();
            var mats = new List<Material> { M.Asphalt, M.Sidewalk, M.Curb, M.RoadPaint, M.RoadPaintYellow };
            const int asphalt = 0, sidewalk = 1, curb = 2, paint = 3;
            var go = new GameObject("Streets");
            go.transform.SetParent(parent, false);

            foreach (var road in L.Roads)
            {
                var dir = road.Direction;
                var right = road.Right;
                float len = road.Length, half = road.CarriageWidth * 0.5f;
                var basis = Matrix4x4.TRS(road.Start, Quaternion.LookRotation(dir, Vector3.up), Vector3.one);
                // Локальные координаты дороги: x — поперёк (вправо), z — вдоль
                mb.Transform = basis;
                foreach (var seg in Subtract(0, len, road.CarriageGaps))
                {
                    mb.AddQuad(asphalt, new Vector3(-half, 0, seg.x), new Vector3(-half, 0, seg.y), new Vector3(half, 0, seg.y), new Vector3(half, 0, seg.x));
                    var c = go.AddComponent<BoxCollider>();
                    var center = road.Start + dir * ((seg.x + seg.y) * 0.5f) + Vector3.down * 0.5f;
                    c.center = center;
                    c.size = Abs(Rotate(dir, new Vector3(road.CarriageWidth, 1f, seg.y - seg.x)));
                }
                foreach (float side in new[] { -1f, 1f })
                {
                    if ((side < 0 && !road.SidewalkLeft) || (side > 0 && !road.SidewalkRight)) continue;
                    float x0 = side * half, x1 = side * (half + road.SidewalkWidth);
                    foreach (var seg in Subtract(0, len, road.SidewalkGaps))
                    {
                        float a = Mathf.Min(x0, x1), b = Mathf.Max(x0, x1);
                        mb.AddBox(sidewalk, new Vector3((a + b) * 0.5f, Walk * 0.5f, (seg.x + seg.y) * 0.5f), new Vector3(b - a, Walk, seg.y - seg.x));
                        mb.AddBox(curb, new Vector3(x0 + side * 0.1f, Walk * 0.5f + 0.01f, (seg.x + seg.y) * 0.5f), new Vector3(0.2f, Walk + 0.02f, seg.y - seg.x));
                        var c = go.AddComponent<BoxCollider>();
                        var center = road.Start + dir * ((seg.x + seg.y) * 0.5f) + right * ((a + b) * 0.5f) + Vector3.up * (Walk - 0.5f);
                        c.center = center;
                        c.size = Abs(Rotate(dir, new Vector3(b - a, 1f, seg.y - seg.x)));
                    }
                }

                // Разметка: осевая прерывистая, края парковочных полос, «ёлочка» парковочных мест
                foreach (var seg in Subtract(0, len, road.CarriageGaps))
                {
                    for (float z = seg.x + 2f; z < seg.y - 3f; z += 9f)
                    {
                        if (NearIntersection(L, road.Start + dir * z, 12f)) continue;
                        mb.AddBox(paint, new Vector3(0, 0.005f, z + 1.5f), new Vector3(0.12f, 0.01f, 3f));
                    }
                    if (road.ParkingLanes)
                        foreach (float side in new[] { -1f, 1f })
                            for (float z = seg.x; z < seg.y - 6f; z += 6f)
                            {
                                if (NearIntersection(L, road.Start + dir * z, 14f)) continue;
                                mb.AddBox(paint, new Vector3(side * (half - 2.6f), 0.005f, z + 3f), new Vector3(0.1f, 0.01f, 6f));
                                mb.AddBox(paint, new Vector3(side * (half - 1.3f), 0.005f, z), new Vector3(2.5f, 0.01f, 0.1f));
                            }
                }
                mb.Transform = Matrix4x4.identity;
            }

            // Пешеходные переходы («зебра») на всех подходах к перекрёсткам
            foreach (var x in L.Intersections)
            {
                float hx = x.Size.x * 0.5f, hz = x.Size.y * 0.5f, off = x.CrosswalkOffset, w = x.CrosswalkWidth;
                foreach (float s in new[] { -1f, 1f })
                {
                    float cx = x.Center.x + s * (hx + off);
                    for (float z = -hz + 0.4f; z < hz - 0.2f; z += 1f)
                        mb.AddBox(paint, new Vector3(cx, 0.006f, x.Center.z + z + 0.25f), new Vector3(w, 0.012f, 0.5f));
                    float cz = x.Center.z + s * (hz + off);
                    for (float xx = -hx + 0.4f; xx < hx - 0.2f; xx += 1f)
                        mb.AddBox(paint, new Vector3(x.Center.x + xx + 0.25f, 0.006f, cz), new Vector3(0.5f, 0.012f, w));
                }
            }

            // Стоп-линии по полосам
            foreach (var lane in L.Lanes)
                foreach (var stop in lane.Stops)
                {
                    var p = lane.Evaluate(stop.Distance, out var fwd);
                    float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                    mb.AddBox(paint, p + new Vector3(0, 0.006f, 0), new Vector3(3.3f, 0.012f, 0.4f), yaw);
                }

            // Люки на проезжей части — мелкая деталь, убирающая «стерильность»
            var manhole = mats.Count;
            mats.Add(M.Paint("manhole", new Color(0.22f, 0.22f, 0.23f), "corrugated", 0.5f, 0.4f));
            for (float x = -100; x <= 100; x += 37) mb.AddCylinder(manhole, new Vector3(x, 0.002f, (x % 2 == 0) ? 1.0f : -1.2f), 0.35f, 0.006f, 12);

            go.AddComponent<MeshFilter>().sharedMesh = mb.Build("Streets");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats.ToArray();
            mr.shadowCastingMode = ShadowCastingMode.Off;

            foreach (var road in L.Roads)
            {
                inst.Map.Add(RectShape(road, 0, road.CarriageWidth * 0.5f));
                inst.Map.Add(RectShape(road, 1, road.CarriageWidth * 0.5f + road.SidewalkWidth));
            }
        }

        static MapShape RectShape(RoadSpec road, int kind, float half)
        {
            var a = road.Start - road.Right * half;
            var b = road.End + road.Right * half;
            return new MapShape { Kind = kind, Min = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z)), Max = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)), Label = kind == 0 ? road.Name : null };
        }

        static bool NearIntersection(DistrictLayout L, Vector3 p, float radius)
        {
            foreach (var x in L.Intersections)
                if (Mathf.Abs(p.x - x.Center.x) < x.Size.x * 0.5f + radius && Mathf.Abs(p.z - x.Center.z) < x.Size.y * 0.5f + radius) return true;
            return false;
        }

        static Vector3 Rotate(Vector3 dir, Vector3 localSize) => Quaternion.LookRotation(dir, Vector3.up) * localSize;
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static List<Vector2> Subtract(float from, float to, List<Vector2> gaps)
        {
            var result = new List<Vector2>();
            float cur = from;
            var sorted = new List<Vector2>(gaps);
            sorted.Sort((a, b) => a.x.CompareTo(b.x));
            foreach (var g in sorted)
            {
                if (g.x > cur) result.Add(new Vector2(cur, Mathf.Min(g.x, to)));
                cur = Mathf.Max(cur, g.y);
            }
            if (cur < to) result.Add(new Vector2(cur, to));
            return result;
        }

        // ---------- Парковки ----------

        void BuildParkingLot(ParkingLotSpec lot, Transform parent, InstancedRenderer inst, Transform colliders, DistrictInstance district)
        {
            var mb = new MeshBuilder();
            var mats = new List<Material> { M.AsphaltParking, M.RoadPaint, M.Curb, M.Paint("wheelstop", new Color(0.85f, 0.75f, 0.2f), null, 1f, 0.3f) };
            var r = lot.Area;
            float y = Walk + 0.03f;
            TopQuad(mb, 0, r, y);
            // Бортовой камень по периметру (кроме стороны к улице)
            mb.AddBox(2, new Vector3(r.center.x, y + 0.05f, r.yMax), new Vector3(r.width, 0.1f, 0.2f));
            mb.AddBox(2, new Vector3(r.xMin, y + 0.05f, r.center.y), new Vector3(0.2f, 0.1f, r.height));
            mb.AddBox(2, new Vector3(r.xMax, y + 0.05f, r.center.y), new Vector3(0.2f, 0.1f, r.height));
            // Два ряда мест вдоль X, проезд посередине
            float bayW = 2.6f, bayD = 5f;
            var rng = new DeterministicRandom((ulong)(r.x * 13 + r.y * 7));
            foreach (float rowZ in new[] { r.yMin + bayD * 0.5f + 0.3f, r.yMax - bayD * 0.5f - 0.3f })
            {
                bool nearSide = rowZ < r.center.y;
                for (float x = r.xMin + 1f; x + bayW < r.xMax - 1f; x += bayW)
                {
                    mb.AddBox(1, new Vector3(x, y + 0.004f, rowZ), new Vector3(0.1f, 0.008f, bayD));
                    mb.AddBox(3, new Vector3(x + bayW * 0.5f, y + 0.06f, rowZ + (nearSide ? -1f : 1f) * (bayD * 0.5f - 0.4f)), new Vector3(1.6f, 0.12f, 0.15f));
                    if (rng.Chance(lot.Occupancy))
                    {
                        float yaw = nearSide ? 180f : 0f;
                        CarFactory.CreateParked(parent, new Vector3(x + bayW * 0.5f, y, rowZ), yaw + rng.Range(-3f, 3f), rng.Range(0, 1 << 20));
                    }
                }
            }
            var go = Surface(parent, "Parking_" + lot.LotKey, mb, mats);
            SlabCollider(go, r, y, 0.2f);

            // Паркомат-терминал — точка взаимодействия с объектом «парковка»
            var terminal = new GameObject("PayStation_" + lot.LotKey);
            terminal.transform.SetParent(go.transform, false);
            terminal.transform.localPosition = new Vector3(r.xMin + 1.2f, y, r.yMin + 0.6f);
            var model = _props.Get(PropKind.ParkingMeter, 0);
            var tm = new GameObject("Mesh");
            tm.transform.SetParent(terminal.transform, false);
            tm.transform.localScale = new Vector3(1.6f, 1.2f, 1.6f);
            tm.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            tm.AddComponent<MeshRenderer>().sharedMaterials = model.Materials;
            if (!string.IsNullOrEmpty(lot.Sign))
                WorldText.Create(terminal.transform, "Sign", lot.Sign, new Vector3(0, 2.3f, 0), 0.25f, Color.white, 2.4f);
            var dir = terminal.AddComponent<DirectoryPoint>();
            dir.Configure(lot.Sign ?? "Парковка", "Терминал парковки", new[] { lot.LotKey }, new Vector3(0, 1f, -0.8f));
            district.Map.Add(new MapShape { Kind = 4, Min = r.min, Max = r.max, Label = "P", LotKeys = new[] { lot.LotKey } });
        }

        // ---------- Скверы ----------

        void BuildPark(ParkSpec park, Transform parent, InstancedRenderer inst, Transform colliders, DistrictInstance district)
        {
            var mb = new MeshBuilder();
            var mats = new List<Material> { M.Grass, M.Pavers, M.Curb, M.Paint("water", new Color(0.25f, 0.45f, 0.55f), null, 1f, 0.95f), M.Concrete, M.Wood, M.MetalDark, M.Paint("sand", new Color(0.85f, 0.78f, 0.6f), "gravel", 1f, 0.05f), M.Paint("play", new Color(0.9f, 0.35f, 0.2f), null, 1f, 0.4f) };
            var r = park.Area;
            float y = Walk + 0.03f;
            var g = district.Pedestrians;
            var center = new Vector3(r.center.x, y + 0.02f, r.center.y);
            bool small = !park.Fountain;

            if (small)
            {
                // Мощёная площадка с деревьями
                TopQuad(mb, 1, r, y);
                for (float x = r.xMin + 3f; x < r.xMax - 2f; x += 7f)
                {
                    AddInstance(inst, colliders, PropKind.Tree, new Vector3(x, y, r.yMin + 4f), x * 11f, 0);
                    AddInstance(inst, colliders, PropKind.Bench, new Vector3(x + 2.5f, y, r.yMin + 4f), 180f, 0);
                }
                var entry = g.AddPolyline(new[] { new Vector3(r.center.x, Walk, r.yMax + 3.7f), new Vector3(r.center.x, y, r.yMin + 7f) });
                g.LinkToNearest(entry[0], 6f);
                g.AddPolyline(new[] { new Vector3(r.xMin + 2f, y, r.yMin + 7f), new Vector3(r.xMax - 2f, y, r.yMin + 7f) });
            }
            else
            {
                TopQuad(mb, 0, r, y);
                // Бордюр по периметру
                mb.AddBox(2, new Vector3(r.center.x, y + 0.06f, r.yMin), new Vector3(r.width, 0.12f, 0.25f));
                mb.AddBox(2, new Vector3(r.center.x, y + 0.06f, r.yMax), new Vector3(r.width, 0.12f, 0.25f));
                mb.AddBox(2, new Vector3(r.xMin, y + 0.06f, r.center.y), new Vector3(0.25f, 0.12f, r.height));
                mb.AddBox(2, new Vector3(r.xMax, y + 0.06f, r.center.y), new Vector3(0.25f, 0.12f, r.height));
                // Дорожки: крест + кольцо вокруг фонтана
                mb.AddBox(1, new Vector3(r.center.x, y + 0.01f, r.center.y), new Vector3(r.width - 0.5f, 0.02f, 3f));
                mb.AddBox(1, new Vector3(r.center.x, y + 0.01f, r.center.y), new Vector3(3f, 0.02f, r.height - 0.5f));
                mb.AddCylinder(1, center + Vector3.down * 0.015f, 6.5f, 0.02f, 24);
                // Фонтан
                mb.AddCylinder(4, center, 3.3f, 0.55f, 24);
                mb.AddCylinder(3, center + new Vector3(0, 0.45f, 0), 3.0f, 0.12f, 24);
                mb.AddCylinder(4, center, 0.6f, 1.2f, 12);
                mb.AddCylinder(4, center + new Vector3(0, 1.2f, 0), 1.2f, 0.15f, 16, true, 0.9f);
                var col = new GameObject("FountainCollider");
                col.transform.SetParent(colliders, false);
                var cc = col.AddComponent<CapsuleCollider>();
                cc.center = center + new Vector3(0, 0.5f, 0); cc.radius = 3.3f; cc.height = 1f;
                FountainSpray(parent, center + new Vector3(0, 1.4f, 0));

                // Деревья по периметру, скамейки вокруг фонтана, клумбы
                for (float x = r.xMin + 3f; x < r.xMax - 2f; x += 7f)
                    foreach (float z in new[] { r.yMin + 3f, r.yMax - 3f })
                        if (Mathf.Abs(x - r.center.x) > 3f) AddInstance(inst, colliders, PropKind.Tree, new Vector3(x, y, z), x * 17f + z, (int)(x + z) & 1 + 1);
                for (float z = r.yMin + 9f; z < r.yMax - 8f; z += 7f)
                    foreach (float x in new[] { r.xMin + 3f, r.xMax - 3f })
                        if (Mathf.Abs(z - r.center.y) > 3f) AddInstance(inst, colliders, PropKind.Tree, new Vector3(x, y, z), z * 7f, 0);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI * 2f / 8f + Mathf.PI / 8f;
                    var p = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 7.6f;
                    p.y = y;
                    float yaw = Mathf.Atan2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Rad2Deg;
                    AddInstance(inst, colliders, PropKind.Bench, p, yaw, 0);
                    if (i % 2 == 0) AddInstance(inst, colliders, PropKind.TrashBin, p + new Vector3(Mathf.Cos(a + 0.25f), 0, Mathf.Sin(a + 0.25f)) * 1.4f, 0, 0);
                    int idle = g.AddNode(center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 6.6f, PedNodeKind.Idle, -1, yaw + 180f);
                    g.LinkToNearest(idle, 4f);
                }
                foreach (var off in new[] { new Vector3(-9f, 0, -9f), new Vector3(9f, 0, 9f), new Vector3(-9f, 0, 9f), new Vector3(9f, 0, -9f) })
                    AddInstance(inst, colliders, PropKind.FlowerBed, new Vector3(r.center.x, y, r.center.y) + off, 0, (int)off.x & 3);
                // Фонари вдоль дорожек
                foreach (var off in new[] { new Vector3(-12f, 0, 1.8f), new Vector3(12f, 0, -1.8f), new Vector3(1.8f, 0, 12f), new Vector3(-1.8f, 0, -12f) })
                {
                    var p = new Vector3(r.center.x, y, r.center.y) + off;
                    float yaw = Mathf.Atan2(-off.x, -off.z) * Mathf.Rad2Deg;
                    AddInstance(inst, colliders, PropKind.StreetLamp, p, yaw, 0);
                    district.LampHeads.Add(p + Quaternion.Euler(0, yaw, 0) * PropFactory.LampHeadLocal);
                }
                if (park.Playground) Playground(mb, colliders, new Vector3(r.xMax - 7f, y, r.yMin + 8f));

                // Граф дорожек: крест и кольцо, выходы на тротуары
                var ring = new List<Vector3>();
                for (int i = 0; i <= 12; i++)
                {
                    float a = i * Mathf.PI * 2f / 12f;
                    ring.Add(center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 5.5f);
                }
                g.AddPolyline(ring, 3f);
                var north = new Vector3(r.center.x, Walk, r.yMax + 3.7f);
                var south = new Vector3(r.center.x, y, r.yMin + 1f);
                var west = new Vector3(r.xMin - 3.0f, Walk, r.center.y);
                g.AddPolyline(new[] { north, new Vector3(r.center.x, y, r.yMax - 1f), center + new Vector3(0, 0, 5.5f) });
                g.AddPolyline(new[] { center + new Vector3(0, 0, -5.5f), south });
                g.AddPolyline(new[] { west, new Vector3(r.xMin + 1f, y, r.center.y), center + new Vector3(-5.5f, 0, 0) });
                g.AddPolyline(new[] { center + new Vector3(5.5f, 0, 0), new Vector3(r.xMax - 1f, y, r.center.y) });
                foreach (var p in new[] { north, west }) g.LinkToNearest(g.AddNode(p), 6f);
            }

            var go = Surface(parent, "Park_" + park.Name, mb, mats, true);
            SlabCollider(go, r, y, 0.2f);
            var label = new GameObject("ParkSign");
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(r.center.x + 2.4f, y, r.yMax - 0.6f);
            WorldText.Create(label.transform, "Text", park.Name, new Vector3(0, 1.2f, 0), 0.3f, new Color(0.95f, 0.92f, 0.8f), 3f, TextAnchor.MiddleCenter, 180f);
            district.Map.Add(new MapShape { Kind = 3, Min = r.min, Max = r.max, Label = park.Name });
        }

        void Playground(MeshBuilder mb, Transform colliders, Vector3 p)
        {
            // Песочница, горка, качели
            mb.AddBox(2, p + new Vector3(-2f, 0.1f, 0), new Vector3(3f, 0.2f, 3f));
            mb.AddBox(7, p + new Vector3(-2f, 0.19f, 0), new Vector3(2.7f, 0.02f, 2.7f));
            mb.AddBox(8, p + new Vector3(2f, 0.75f, 0), new Vector3(1.2f, 1.5f, 1.2f));
            mb.AddQuad(8, p + new Vector3(1.5f, 0.05f, 3.2f), p + new Vector3(1.5f, 1.5f, 0.6f), p + new Vector3(2.5f, 1.5f, 0.6f), p + new Vector3(2.5f, 0.05f, 3.2f));
            mb.AddQuad(8, p + new Vector3(2.5f, 0.05f, 3.2f), p + new Vector3(2.5f, 1.5f, 0.6f), p + new Vector3(1.5f, 1.5f, 0.6f), p + new Vector3(1.5f, 0.05f, 3.2f));
            foreach (float x in new[] { -1.2f, 1.2f })
            {
                mb.AddBox(6, p + new Vector3(x - 0.3f, 1.1f, -4f), new Vector3(0.08f, 2.2f, 0.08f));
                mb.AddBox(6, p + new Vector3(x + 0.3f, 1.1f, -4f), new Vector3(0.08f, 2.2f, 0.08f));
            }
            mb.AddBox(6, p + new Vector3(0, 2.2f, -4f), new Vector3(3.2f, 0.08f, 0.08f));
            mb.AddBox(5, p + new Vector3(-0.6f, 0.5f, -4f), new Vector3(0.5f, 0.05f, 0.25f));
            mb.AddBox(5, p + new Vector3(0.6f, 0.5f, -4f), new Vector3(0.5f, 0.05f, 0.25f));
            var go = new GameObject("PlaygroundCollider");
            go.transform.SetParent(colliders, false);
            var c = go.AddComponent<BoxCollider>();
            c.center = p + new Vector3(2f, 0.75f, 0); c.size = new Vector3(1.2f, 1.5f, 1.2f);
        }

        void FountainSpray(Transform parent, Vector3 pos)
        {
            var go = new GameObject("FountainSpray");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSpeed = 4.5f;
            main.startLifetime = 1.1f;
            main.startSize = 0.12f;
            main.gravityModifier = 0.9f;
            main.maxParticles = 400;
            main.startColor = new Color(0.85f, 0.92f, 1f, 0.55f);
            var emission = ps.emission;
            emission.rateOverTime = 260f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.1f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = M.Particles("water_spray", new Color(0.85f, 0.92f, 1f, 0.55f));
        }

        // ---------- Пропсы ----------

        void AddInstance(InstancedRenderer inst, Transform colliders, PropKind kind, Vector3 pos, float yaw, int variant)
        {
            var model = _props.Get(kind, variant);
            inst.Add(model.Mesh, model.Materials, inst.transform.localToWorldMatrix * Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), Vector3.one), model.Shadows, model.MaxDistance);
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(colliders, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var bc = go.AddComponent<BoxCollider>();
            bc.center = model.ColliderCenter;
            bc.size = model.ColliderSize;
        }

        void PlaceProp(PropSpec p, InstancedRenderer inst, Transform colliders, Transform unique, DistrictInstance district)
        {
            if (!PropFactory.IsInstanced(p.Kind))
            {
                _props.CreateUnique(p, unique);
                return;
            }
            AddInstance(inst, colliders, p.Kind, p.Position, p.Yaw, p.Variant);
            if (p.Kind == PropKind.StreetLamp)
                district.LampHeads.Add(p.Position + Quaternion.Euler(0, p.Yaw, 0) * PropFactory.LampHeadLocal);
        }

        void BuildParkedCars(DistrictLayout L, Transform parent)
        {
            var root = new GameObject("ParkedCars").transform;
            root.SetParent(parent, false);
            var rng = new DeterministicRandom(4242);
            foreach (var spot in L.ParkingLaneSpots)
            {
                if (!rng.Chance(0.62)) continue;
                // Машины стоят по направлению движения своей стороны дороги
                float yaw = spot.center.y > 0 ? -90f : 90f;
                CarFactory.CreateParked(root, new Vector3(spot.center.x + rng.Range(-0.3f, 0.3f), 0f, spot.center.y), yaw, rng.Range(0, 1 << 20));
            }
        }

        void BuildBoundary(DistrictLayout L, Transform parent)
        {
            var go = new GameObject("Boundary");
            go.transform.SetParent(parent, false);
            var b = L.Bounds;
            void Wall(Vector3 c, Vector3 s) { var bc = go.AddComponent<BoxCollider>(); bc.center = c; bc.size = s; }
            Wall(new Vector3(b.center.x, 5f, b.yMin - 0.5f), new Vector3(b.width + 2f, 10f, 1f));
            Wall(new Vector3(b.center.x, 5f, b.yMax + 0.5f), new Vector3(b.width + 2f, 10f, 1f));
            Wall(new Vector3(b.xMin - 0.5f, 5f, b.center.y), new Vector3(1f, 10f, b.height + 2f));
            Wall(new Vector3(b.xMax + 0.5f, 5f, b.center.y), new Vector3(1f, 10f, b.height + 2f));
        }

        void AddMapBuilding(DistrictInstance inst, BuildingSpec b, int kind)
        {
            var m = b.LocalToWorld;
            var p0 = m.MultiplyPoint3x4(Vector3.zero);
            var p1 = m.MultiplyPoint3x4(new Vector3(b.Width, 0, b.Depth));
            var keys = new List<string>();
            foreach (var u in b.Units)
            {
                if (u.LotKey != null) keys.Add(u.LotKey);
                keys.AddRange(u.ApartmentLots);
                keys.AddRange(u.OfficeLots);
            }
            inst.Map.Add(new MapShape
            {
                Kind = kind, Min = new Vector2(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.z, p1.z)), Max = new Vector2(Mathf.Max(p0.x, p1.x), Mathf.Max(p0.z, p1.z)),
                Label = b.Backdrop ? null : b.HouseNumber, LotKeys = keys.ToArray(),
            });
        }

        /// <summary>Двери магазинов и подъездов становятся узлами пешеходного графа.</summary>
        void LinkDoors(DistrictInstance inst)
        {
            var g = inst.Pedestrians;
            foreach (var view in inst.GetComponentsInChildren<PropertyView>(true))
            {
                var def = _world.FindByLot(inst.DistrictId, view.LotKey);
                if (def == null) continue;
                var p = inst.transform.InverseTransformPoint(view.DoorOutside);
                int node = g.AddNode(new Vector3(p.x, Walk, p.z), PedNodeKind.Door, def.Id, 0f, false);
                g.LinkToNearest(node, 25f);
            }
            foreach (var dir in inst.GetComponentsInChildren<DirectoryPoint>(true))
            {
                if (dir.GetComponentInParent<BuildingLights>() == null) continue; // только подъезды и лобби
                var p = inst.transform.InverseTransformPoint(dir.InteractPosition);
                int node = g.AddNode(new Vector3(p.x, Walk, p.z), PedNodeKind.Door, -2, 0f, false);
                g.LinkToNearest(node, 12f);
            }
        }
    }
}
