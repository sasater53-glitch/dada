using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// Помещение в ТЦ: оболочка (пол, стены, потолок), витрина с автоматической дверью, вывеска,
    /// рольставни на закрытие, баннеры «СДАЁТСЯ / РЕМОНТ», визуальные признаки развития
    /// (подсветка вывески, промо-стойки, LED-экраны, антикражные ворота, камеры, охранник).
    /// Интерьер собирается из моделей и пересобирается только при изменении типа бизнеса или уровней;
    /// он активен лишь вблизи камеры — это главный способ держать сцену лёгкой.
    /// </summary>
    public sealed class StorefrontView : MonoBehaviour, IInteractable
    {
        public static readonly List<StorefrontView> All = new List<StorefrontView>();
        const float InteriorVisibleDistance = 48f;

        [SerializeField] string _lotKey;
        [SerializeField] PropertyKind _kind;
        [SerializeField] float _width, _depth, _height, _storefrontTop;
        [SerializeField] AutoDoor _door;
        [SerializeField] MeshRenderer _signBoard;
        [SerializeField] TextMesh _sign, _banner, _hours;
        [SerializeField] GameObject _bannerRoot;
        [SerializeField] Transform _shutter, _interior, _extras;
        [SerializeField] Light _light;
        [SerializeField] BoxCollider _doorBlocker;

        public int PropertyId { get; private set; }
        public string LotKey => _lotKey;
        public PropertyKind Kind => _kind;
        public InteriorInfo Interior { get; private set; }
        public bool InteriorActive => _interior != null && _interior.gameObject.activeSelf && Interior != null;

        string _signature, _interiorKey, _extrasKey;
        float _refreshTimer, _cullTimer, _shutterPos = 1f;
        bool _open, _mallOpen;
        BusinessType _type;
        readonly List<CharacterRig> _staff = new List<CharacterRig>();

        // ---------------- Построение ----------------

        public static StorefrontView Create(MallKit kit, Transform parent, PropertyDef def, Vector3 localPos, float yaw)
        {
            var go = new GameObject("Unit_" + def.LotKey);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var v = go.AddComponent<StorefrontView>();
            v._lotKey = def.LotKey;
            v._kind = def.Kind;
            v._width = def.Width;
            v._depth = def.Depth;
            v._height = def.Kind == PropertyKind.Anchor ? 5.4f : def.Kind == PropertyKind.Kiosk ? 3.2f : 4.8f;
            v._storefrontTop = def.Kind == PropertyKind.Anchor ? 4.2f : 4.2f;
            v.BuildShell(kit.M);
            return v;
        }

        void BuildShell(MaterialLibrary M)
        {
            var shell = new GameObject("Shell");
            shell.transform.SetParent(transform, false);
            float hw = _width * 0.5f;
            var mb = new MeshBuilder();
            var mats = new List<Material> { M.ServiceFloor, M.Wall, M.Ceiling, M.CeilingLight, M.Aluminium, M.StoreGlass, M.MetalPanel };
            const int floor = 0, wall = 1, ceil = 2, light = 3, alu = 4;

            _extras = new GameObject("Extras").transform;
            _extras.SetParent(transform, false);
            _interior = new GameObject("Interior").transform;
            _interior.SetParent(transform, false);

            if (_kind == PropertyKind.Kiosk)
            {
                // Островной киоск: подиум, четыре опоры и навес с вывеской
                mb.AddBox(alu, new Vector3(0, 0.03f, 0), new Vector3(_width, 0.06f, _depth));
                foreach (float x in new[] { -hw + 0.1f, hw - 0.1f })
                    foreach (float z in new[] { -hw + 0.1f, hw - 0.1f })
                        mb.AddBox(alu, new Vector3(x, 1.6f, z), new Vector3(0.08f, 3.2f, 0.08f));
                mb.AddBox(wall, new Vector3(0, 3.25f, 0), new Vector3(_width + 0.6f, 0.5f, _depth + 0.6f));
                mb.AddBox(light, new Vector3(0, 2.99f, 0), new Vector3(_width - 0.4f, 0.02f, _depth - 0.4f));
                CreateMesh(shell.transform, "KioskShell", mb, mats);
                CreateSign(M, new Vector3(0, 3.25f, -_depth * 0.5f - 0.31f), _width, 0.42f, 0f);
                var back = CreateSign(M, new Vector3(0, 3.25f, _depth * 0.5f + 0.31f), _width, 0.42f, 180f, false);
                back.transform.SetParent(_extras, true);
                _shutter = null;
                CreateInteriorLight(new Vector3(0, 2.8f, 0), 7f);
                CreateBanner(M, new Vector3(0, 1.4f, -_depth * 0.5f - 0.1f), 1.8f);
                return;
            }

            float d = _depth, h = _height;
            // Пол, стены и потолок — смотрят внутрь помещения
            mb.AddQuad(floor, new Vector3(-hw, 0.002f, 0), new Vector3(-hw, 0.002f, d), new Vector3(hw, 0.002f, d), new Vector3(hw, 0.002f, 0),
                new Vector2(-hw, 0), new Vector2(-hw, d), new Vector2(hw, d), new Vector2(hw, 0));
            mb.AddQuad(wall, new Vector3(-hw + 0.1f, 0, d - 0.1f), new Vector3(-hw + 0.1f, h, d - 0.1f), new Vector3(hw - 0.1f, h, d - 0.1f), new Vector3(hw - 0.1f, 0, d - 0.1f));
            mb.AddQuad(wall, new Vector3(-hw + 0.1f, 0, 0.3f), new Vector3(-hw + 0.1f, h, 0.3f), new Vector3(-hw + 0.1f, h, d - 0.1f), new Vector3(-hw + 0.1f, 0, d - 0.1f));
            mb.AddQuad(wall, new Vector3(hw - 0.1f, 0, d - 0.1f), new Vector3(hw - 0.1f, h, d - 0.1f), new Vector3(hw - 0.1f, h, 0.3f), new Vector3(hw - 0.1f, 0, 0.3f));
            mb.AddQuad(ceil, new Vector3(-hw, h, d), new Vector3(-hw, h, 0.3f), new Vector3(hw, h, 0.3f), new Vector3(hw, h, d),
                new Vector2(-hw, d), new Vector2(-hw, 0), new Vector2(hw, 0), new Vector2(hw, d));
            // Стена над витриной изнутри
            mb.AddQuad(wall, new Vector3(hw - 0.1f, _storefrontTop, 0.3f), new Vector3(hw - 0.1f, h, 0.3f), new Vector3(-hw + 0.1f, h, 0.3f), new Vector3(-hw + 0.1f, _storefrontTop, 0.3f));
            for (float z = 2f; z < d - 1f; z += 3f)
                for (float x = -hw + 2f; x < hw - 1f; x += 3.5f)
                    mb.AddBox(light, new Vector3(x, h - 0.02f, z), new Vector3(1.2f, 0.04f, 0.6f));

            bool openFront = _kind == PropertyKind.FoodStall;
            bool gate = _kind == PropertyKind.Storage;
            float doorW = _kind == PropertyKind.Anchor ? 4f : 2f;
            if (_kind == PropertyKind.FoodStall)
            {
                // Короб над открытым прилавком (точки фудкорта не на линии фасада галереи)
                mb.AddQuad(wall, new Vector3(-hw, 3.2f, 0), new Vector3(-hw, 5.5f, 0), new Vector3(hw, 5.5f, 0), new Vector3(hw, 3.2f, 0));
            }
            else if (!gate)
            {
                // Витрина: цоколь, импосты, стекло (стекло — отдельный прозрачный меш)
                mb.AddBox(alu, new Vector3(0, 0.12f, 0.15f), new Vector3(_width - 0.4f, 0.24f, 0.12f));
                foreach (float x in new[] { -hw + 0.2f, -doorW * 0.5f - 0.05f, doorW * 0.5f + 0.05f, hw - 0.2f })
                    mb.AddBox(alu, new Vector3(x, _storefrontTop * 0.5f, 0.15f), new Vector3(0.08f, _storefrontTop, 0.1f));
                for (float x = -hw + 2.4f; x < -doorW * 0.5f - 0.8f; x += 2.4f) mb.AddBox(alu, new Vector3(x, _storefrontTop * 0.5f, 0.15f), new Vector3(0.05f, _storefrontTop, 0.08f));
                for (float x = hw - 2.4f; x > doorW * 0.5f + 0.8f; x -= 2.4f) mb.AddBox(alu, new Vector3(x, _storefrontTop * 0.5f, 0.15f), new Vector3(0.05f, _storefrontTop, 0.08f));
                mb.AddBox(alu, new Vector3(0, 2.75f, 0.15f), new Vector3(_width - 0.4f, 0.08f, 0.1f));
            }
            CreateMesh(shell.transform, "Shell", mb, mats);

            if (!openFront && !gate)
            {
                var glass = new MeshBuilder();
                float y0 = 0.24f;
                glass.AddQuadUnit(0, new Vector3(-hw + 0.2f, y0, 0.15f), new Vector3(-hw + 0.2f, _storefrontTop, 0.15f), new Vector3(-doorW * 0.5f - 0.05f, _storefrontTop, 0.15f), new Vector3(-doorW * 0.5f - 0.05f, y0, 0.15f));
                glass.AddQuadUnit(0, new Vector3(doorW * 0.5f + 0.05f, y0, 0.15f), new Vector3(doorW * 0.5f + 0.05f, _storefrontTop, 0.15f), new Vector3(hw - 0.2f, _storefrontTop, 0.15f), new Vector3(hw - 0.2f, y0, 0.15f));
                glass.AddQuadUnit(0, new Vector3(-doorW * 0.5f, 2.8f, 0.15f), new Vector3(-doorW * 0.5f, _storefrontTop, 0.15f), new Vector3(doorW * 0.5f, _storefrontTop, 0.15f), new Vector3(doorW * 0.5f, 2.8f, 0.15f));
                var gm = CreateMesh(shell.transform, "Glass", glass, new List<Material> { M.StoreGlass });
                gm.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                _door = CreateSlidingDoor(M, transform, new Vector3(0, 0, 0.15f), doorW, 2.7f, 0f).GetComponent<AutoDoor>();
            }

            // Коллайдеры: пол, стены, витрина с проёмом двери
            Col(new Vector3(0, -0.15f, d * 0.5f), new Vector3(_width, 0.3f, d));
            Col(new Vector3(0, h * 0.5f, d), new Vector3(_width, h, 0.2f));
            Col(new Vector3(-hw, h * 0.5f, d * 0.5f), new Vector3(0.2f, h, d));
            Col(new Vector3(hw, h * 0.5f, d * 0.5f), new Vector3(0.2f, h, d));
            if (openFront)
            {
                // Прилавок не даёт пройти внутрь — покупатели обслуживаются через стойку
                Col(new Vector3(0, 0.55f, 0.6f), new Vector3(_width - 0.4f, 1.1f, 0.6f));
            }
            else
            {
                float side = hw - doorW * 0.5f;
                Col(new Vector3(-hw + side * 0.5f, _storefrontTop * 0.5f, 0.15f), new Vector3(side, _storefrontTop, 0.2f));
                Col(new Vector3(hw - side * 0.5f, _storefrontTop * 0.5f, 0.15f), new Vector3(side, _storefrontTop, 0.2f));
                Col(new Vector3(0, (_storefrontTop + 2.8f) * 0.5f, 0.15f), new Vector3(doorW, _storefrontTop - 2.8f, 0.2f));
                var blocker = new GameObject("DoorBlocker");
                blocker.transform.SetParent(transform, false);
                _doorBlocker = blocker.AddComponent<BoxCollider>();
                _doorBlocker.center = new Vector3(0, 1.4f, 0.15f);
                _doorBlocker.size = new Vector3(doorW, 2.8f, 0.2f);
            }

            float signY = _kind == PropertyKind.FoodStall ? 4.0f : _storefrontTop + 0.65f;
            CreateSign(M, new Vector3(0, signY, -0.12f), Mathf.Min(_width - 0.8f, 10f), 0.75f, 0f);
            CreateShutter(M, openFront ? 3.2f : _storefrontTop, gate);
            CreateBanner(M, new Vector3(-hw * 0.45f, 1.7f, 0.35f), Mathf.Max(1.4f, hw - 1.6f));
            _hours = WorldText.Create(transform, "Hours", "", new Vector3(doorW * 0.5f + 0.45f, 1.55f, 0.1f), 0.08f, Color.white, 0.6f, TextAnchor.MiddleLeft);
            CreateInteriorLight(new Vector3(0, h - 0.4f, d * 0.5f), Mathf.Max(_width, d) * 1.1f);
        }

        GameObject CreateMesh(Transform parent, string name, MeshBuilder mb, List<Material> mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            GeneratedMesh.Assign(go, mb.Build(name + "_" + _lotKey));
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats.ToArray();
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            return go;
        }

        void Col(Vector3 center, Vector3 size)
        {
            var bc = gameObject.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = size;
        }

        GameObject CreateSign(MaterialLibrary M, Vector3 pos, float width, float height, float yaw, bool primary = true)
        {
            var root = new GameObject(primary ? "Sign" : "SignBack");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.name = "Board";
            board.transform.SetParent(root.transform, false);
            board.transform.localScale = new Vector3(width, height, 0.16f);
            board.GetComponent<MeshRenderer>().sharedMaterial = M.Paint("signboard", new Color(0.12f, 0.12f, 0.13f));
            var text = WorldText.Create(root.transform, "Text", "", new Vector3(0, 0, -0.09f), height * 0.5f, Color.white, width - 0.3f);
            if (primary) { _signBoard = board.GetComponent<MeshRenderer>(); _sign = text; }
            else root.AddComponent<SignMirror>().Init(this, board.GetComponent<MeshRenderer>(), text);
            return root;
        }

        void CreateShutter(MaterialLibrary M, float height, bool industrial)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "Shutter";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, height * 0.5f, 0.02f);
            go.transform.localScale = new Vector3(_width - 0.3f, height, 0.05f);
            go.GetComponent<MeshRenderer>().sharedMaterial = industrial ? M.Surface("metal_plate", 1f, new Color(0.7f, 0.72f, 0.74f), 0.4f, 0.7f) : M.Surface("metal_plate", 0.6f, new Color(0.55f, 0.57f, 0.6f), 0.5f, 0.8f);
            _shutter = go.transform;
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
        }

        void CreateBanner(MaterialLibrary M, Vector3 pos, float width)
        {
            _bannerRoot = new GameObject("Banner");
            _bannerRoot.transform.SetParent(transform, false);
            _bannerRoot.transform.localPosition = pos;
            var paper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(paper.GetComponent<Collider>());
            paper.transform.SetParent(_bannerRoot.transform, false);
            paper.transform.localPosition = new Vector3(0, 0, 0.02f);
            paper.transform.localScale = new Vector3(width, 0.95f, 0.02f);
            paper.GetComponent<MeshRenderer>().sharedMaterial = M.Paper;
            _banner = WorldText.Create(_bannerRoot.transform, "Text", "", Vector3.zero, 0.26f, new Color(0.85f, 0.1f, 0.1f), width - 0.2f);
        }

        void CreateInteriorLight(Vector3 pos, float range)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = range;
            _light.intensity = 1.6f;
            _light.color = new Color(1f, 0.96f, 0.9f);
            _light.shadows = LightShadows.None;
            _light.enabled = false;
        }

        /// <summary>Двустворчатая раздвижная дверь (магазины и главный вход). yaw — поворот проёма.</summary>
        public static GameObject CreateSlidingDoor(MaterialLibrary M, Transform parent, Vector3 localPos, float width, float height, float yaw)
        {
            var root = new GameObject("Door");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            root.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var leaves = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var leaf = new GameObject(i == 0 ? "Left" : "Right");
                leaf.transform.SetParent(root.transform, false);
                float sign = i == 0 ? -1f : 1f;
                leaf.transform.localPosition = new Vector3(sign * width * 0.25f, 0, 0);
                var mb = new MeshBuilder();
                mb.AddFrame(0, new Vector3(0, height * 0.5f, 0), width * 0.5f, height, 0.06f, 0.05f);
                mb.AddQuadUnit(1, new Vector3(-width * 0.25f + 0.06f, 0.06f, 0), new Vector3(-width * 0.25f + 0.06f, height - 0.06f, 0),
                    new Vector3(width * 0.25f - 0.06f, height - 0.06f, 0), new Vector3(width * 0.25f - 0.06f, 0.06f, 0));
                mb.AddQuadUnit(1, new Vector3(width * 0.25f - 0.06f, 0.06f, 0), new Vector3(width * 0.25f - 0.06f, height - 0.06f, 0),
                    new Vector3(-width * 0.25f + 0.06f, height - 0.06f, 0), new Vector3(-width * 0.25f + 0.06f, 0.06f, 0));
                GeneratedMesh.Assign(leaf, mb.Build("DoorLeaf"));
                var mr = leaf.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { M.Aluminium, M.StoreGlass };
                mr.shadowCastingMode = ShadowCastingMode.Off;
                leaves[i] = leaf.transform;
            }
            root.AddComponent<AutoDoor>().Configure(leaves[0], leaves[1], width * 0.5f - 0.05f);
            return root;
        }

        // ---------------- Жизненный цикл ----------------

        void OnEnable() { All.Add(this); Interaction.Register(this); }
        void OnDisable() { All.Remove(this); Interaction.Unregister(this); }

        bool Resolve()
        {
            if (PropertyId != 0) return true;
            var game = CityGame.Instance;
            var mall = GetComponentInParent<MallInstance>();
            if (game == null || game.World == null || mall == null) return false;
            var def = game.World.FindByLot(mall.MallId, _lotKey);
            if (def == null) return false;
            PropertyId = def.Id;
            return true;
        }

        // Точки для NPC
        public Vector3 DoorOutside => transform.TransformPoint(_kind == PropertyKind.Kiosk ? new Vector3(0, 0, -_depth * 0.5f - 1f) : new Vector3(0, 0, -1.2f));
        public Vector3 DoorInside => transform.TransformPoint(_kind == PropertyKind.Kiosk ? new Vector3(0, 0, -_depth * 0.5f - 0.5f) : _kind == PropertyKind.FoodStall ? new Vector3(0, 0, -0.5f) : new Vector3(0, 0, 1.4f));
        public bool CustomersStayOutside => _kind == PropertyKind.Kiosk || _kind == PropertyKind.FoodStall;
        public bool AcceptsCustomers => _open && _type != BusinessType.None && EconomyCatalog.Get(_type).FootTraffic;
        public BusinessType BusinessType => _type;

        public Vector3 InteractPosition => transform.TransformPoint(_kind == PropertyKind.Kiosk ? new Vector3(0, 1.3f, -_depth * 0.5f - 0.2f) : new Vector3(0, 1.6f, 0.1f));
        public bool CanInteract => PropertyId != 0 && CityGame.Instance != null && CityGame.Instance.Ready;
        public void Interact() => CityGame.Instance.UI.OpenProperty(PropertyId);

        public string Prompt
        {
            get
            {
                var state = CityGame.Instance != null ? CityGame.Instance.State : null;
                var prop = state?.GetProperty(PropertyId);
                if (prop == null) return "[E] " + _lotKey;
                var b = state.GetBusiness(prop.BusinessId);
                string status;
                if (b != null)
                {
                    bool mine = b.OwnerId == CityGame.Instance.LocalPlayerId;
                    status = $"«{b.Name}» {(mine ? "(ваш) " : "")}— {(b.IsOpenNow ? "открыто" : "закрыто")}";
                }
                else if (prop.ForRent && prop.TenantId == 0) status = $"сдаётся за {EconomyCatalog.Money(prop.RentPrice)}/день";
                else if (prop.OwnerId == 0 || prop.ForSale) status = "продаётся";
                else status = "свободно";
                return $"[E] {prop.Def.LotKey} · {status}";
            }
        }

        void Update()
        {
            var game = CityGame.Instance;
            if (game == null || !game.Ready || !Resolve()) return;

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.5f;
                Refresh(game);
            }

            _cullTimer -= Time.deltaTime;
            if (_cullTimer <= 0f)
            {
                _cullTimer = 0.3f;
                var cam = game.ViewPosition;
                var center = transform.TransformPoint(new Vector3(0, 2f, _kind == PropertyKind.Kiosk ? 0f : _depth * 0.5f));
                bool visible = (cam - center).sqrMagnitude < InteriorVisibleDistance * InteriorVisibleDistance && Mathf.Abs(cam.y - center.y) < 9f;
                if (_interior.gameObject.activeSelf != visible) _interior.gameObject.SetActive(visible);
                if (visible) EnsureInterior(game);
                // Ночью, когда ТЦ закрыт, пустые помещения тоже гаснут
                if (_light != null) _light.enabled = visible && (_open || (_type == BusinessType.None && _mallOpen)) && LightBudget.AllowShopLight(this);
            }

            if (_shutter != null)
            {
                // Рольставни опускаются, когда точка закрыта, а ТЦ уже/ещё не работает
                bool down = !_open && !_mallOpen;
                _shutterPos = Mathf.MoveTowards(_shutterPos, down ? 0f : 1f, Time.deltaTime * 0.6f);
                float full = _kind == PropertyKind.FoodStall ? 3.2f : _storefrontTop;
                _shutter.localPosition = new Vector3(0, full * 0.5f + _shutterPos * (full - 0.15f), 0.02f);
                _shutter.gameObject.SetActive(_shutterPos < 0.999f);
            }
        }

        void Refresh(CityGame game)
        {
            var state = game.State;
            var prop = state.GetProperty(PropertyId);
            if (prop == null) return;
            var b = state.GetBusiness(prop.BusinessId);
            int hour = state.Time.Hour;
            _mallOpen = hour >= EconomyCatalog.MallOpenHour && hour < EconomyCatalog.MallCloseHour;
            bool renovating = b != null && state.Time.WholeMinutes < b.ClosedUntilMinute;
            bool lit = DayNightCycle.SignsLit;
            string sig = $"{b?.Id}|{b?.Type}|{b?.Name}|{b?.IsOpenNow}|{prop.ForRent}|{prop.ForSale}|{prop.TenantId}|{prop.OwnerId}|{b?.Employees.Count}|{b?.Marketing}|{b?.Security}|{b?.Interior}|{renovating}|{lit}|{game.LocalPlayerId}";
            if (sig == _signature) return;
            _signature = sig;

            _type = b?.Type ?? BusinessType.None;
            _open = b != null && b.IsOpenNow;
            var M = MaterialLibrary.Instance;
            var def = b?.Def;

            if (b != null)
            {
                WorldText.SetText(_sign, b.Name);
                var color = BoardColor(b.Type, b.Interior);
                bool glow = b.Marketing >= 1 && (lit || b.Marketing >= 3);
                _signBoard.sharedMaterial = glow ? M.Emissive("signlit_" + b.Type + b.Interior, color, color * (0.8f + 0.3f * b.Marketing)) : M.Paint("signboard", color);
                _sign.color = TextColor(b.Type);
            }
            else
            {
                WorldText.SetText(_sign, _kind == PropertyKind.Storage ? "СКЛАД СВОБОДЕН" : "СВОБОДНО");
                _signBoard.sharedMaterial = M.Paint("signboard", new Color(0.92f, 0.92f, 0.9f));
                _sign.color = new Color(0.75f, 0.1f, 0.1f);
            }

            string banner = null;
            if (b == null && prop.TenantId == 0 && prop.ForRent) banner = $"СДАЁТСЯ\n{EconomyCatalog.Money(prop.RentPrice)}/день";
            if (b == null && (prop.ForSale || prop.OwnerId == 0)) banner = banner == null ? "ПРОДАЁТСЯ" : banner + " · продажа";
            if (renovating) banner = "РЕМОНТ\nскоро открытие";
            else if (b != null && !b.IsOpenNow && b.Employees.Count < def.MinStaff) banner = "ИЩЕМ\nСОТРУДНИКОВ";
            if (_bannerRoot != null)
            {
                _bannerRoot.SetActive(banner != null);
                if (banner != null) WorldText.SetText(_banner, banner);
            }

            if (_hours != null)
                WorldText.SetText(_hours, def != null ? $"{def.OpenHour:00}:00–{def.CloseHour % 24:00}:00\n{(_open ? "ОТКРЫТО" : "ЗАКРЫТО")}" : "");

            // Своя закрытая точка всё равно открывается для владельца
            bool operatorHere = prop.OperatorId == game.LocalPlayerId;
            bool locked = !_open && !operatorHere;
            if (_door != null) _door.Locked = locked;
            if (_doorBlocker != null) _doorBlocker.enabled = locked;

            if (_interior.gameObject.activeSelf) EnsureInterior(game);
            SyncExtras(b, M);
            SyncStaff(b);
        }

        void EnsureInterior(CityGame game)
        {
            var prop = game.State.GetProperty(PropertyId);
            var b = prop != null ? game.State.GetBusiness(prop.BusinessId) : null;
            string key = b == null ? "vacant" : $"{b.Type}|{b.Interior}|{b.EquipmentLevel}|{b.Assortment}";
            if (key == _interiorKey && Interior != null) return;
            _interiorKey = key;
            foreach (Transform child in _interior) Destroy(child.gameObject);
            _staff.Clear();
            var spec = new ShopInteriorBuilder.Spec
            {
                Kind = _kind, Type = _type, Width = _width, Depth = _depth, Height = _height,
                Interior = b?.Interior ?? 1, Equipment = b?.EquipmentLevel ?? 1, Assortment = b?.Assortment ?? 1, Seed = PropertyId,
            };
            Interior = ShopInteriorBuilder.Build(_interior, spec);
            SyncStaff(b);
        }

        void SyncStaff(BusinessState b)
        {
            if (Interior == null) return;
            int want = b != null && b.IsOpenNow ? Mathf.Min(b.Employees.Count, Interior.WorkPoints.Count) : 0;
            while (_staff.Count > want)
            {
                var last = _staff[_staff.Count - 1];
                _staff.RemoveAt(_staff.Count - 1);
                if (last != null) Destroy(last.gameObject);
            }
            for (int i = _staff.Count; i < want; i++)
            {
                var e = b.Employees[i];
                var rig = CharacterRig.Create(Interior.transform, CharacterRig.RandomModel(e.Seed, e.Female));
                var p = Interior.WorkPoints[i];
                rig.transform.localPosition = new Vector3(p.x, p.y, p.z);
                rig.transform.localRotation = Quaternion.Euler(0, p.w, 0);
                rig.gameObject.AddComponent<StaffAnimator>().Rig = rig;
                _staff.Add(rig);
            }
        }

        /// <summary>Витринные признаки развития: реклама и охрана видны снаружи.</summary>
        void SyncExtras(BusinessState b, MaterialLibrary M)
        {
            string key = b == null ? "-" : $"{b.Marketing}|{b.Security}|{b.Type}|{b.Name}";
            if (key == _extrasKey) return;
            _extrasKey = key;
            foreach (Transform child in _extras)
                if (child.GetComponent<SignMirror>() == null) Destroy(child.gameObject);
            if (b == null || _kind == PropertyKind.Kiosk) return;
            float hw = _width * 0.5f;
            bool food = b.Def.IsFood;

            if (b.Marketing >= 2)
            {
                // Промо-стойка у входа
                var pos = new Vector3(hw - 1.2f, 0f, -1.1f);
                if (food) AssetCatalog.Spawn(AssetCatalog.PolyHaven("standing_chalkboard_01"), _extras, pos, 200f, 1.4f, FitAxis.Height, true);
                else
                {
                    var stand = new GameObject("Promo");
                    stand.transform.SetParent(_extras, false);
                    stand.transform.localPosition = pos;
                    var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.transform.SetParent(stand.transform, false);
                    box.transform.localPosition = new Vector3(0, 0.95f, 0);
                    box.transform.localScale = new Vector3(0.7f, 1.9f, 0.08f);
                    box.GetComponent<MeshRenderer>().sharedMaterial = M.Emissive("promo_" + b.Type, BoardColor(b.Type, 3), BoardColor(b.Type, 3) * 0.9f);
                    WorldText.Create(stand.transform, "Text", "АКЦИЯ\n−20%", new Vector3(0, 1.2f, -0.05f), 0.16f, Color.white, 0.6f);
                }
            }
            if (b.Marketing >= 3 && _kind != PropertyKind.FoodStall)
            {
                // LED-экран в витрине
                var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(screen.GetComponent<Collider>());
                screen.transform.SetParent(_extras, false);
                screen.transform.localPosition = new Vector3(hw * 0.5f + 0.3f, 2.0f, 0.45f);
                screen.transform.localScale = new Vector3(Mathf.Min(2.4f, hw - 1.4f), 1.35f, 0.06f);
                screen.GetComponent<MeshRenderer>().sharedMaterial = M.Emissive("led_" + b.Type, new Color(0.1f, 0.1f, 0.15f), BoardColor(b.Type, 4) * 1.6f + new Color(0.2f, 0.2f, 0.3f));
                WorldText.Create(_extras, "LedText", b.Name + "\nНОВАЯ КОЛЛЕКЦИЯ", new Vector3(hw * 0.5f + 0.3f, 2.0f, 0.4f), 0.13f, Color.white, Mathf.Min(2.2f, hw - 1.6f));
            }
            if (b.Marketing >= 4)
            {
                // Вертикальный баннер на коробе над витриной
                var banner = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(banner.GetComponent<Collider>());
                banner.transform.SetParent(_extras, false);
                banner.transform.localPosition = new Vector3(-hw + 0.7f, 4.4f, -0.5f);
                banner.transform.localScale = new Vector3(0.05f, 1.8f, 0.8f);
                banner.GetComponent<MeshRenderer>().sharedMaterial = M.Emissive("vbanner_" + b.Type, BoardColor(b.Type, 4), BoardColor(b.Type, 4) * 0.8f);
            }
            if (b.Security >= 1 && _kind != PropertyKind.FoodStall)
            {
                // Антикражные ворота
                foreach (float x in new[] { -1.15f, 1.15f })
                {
                    var gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    gate.transform.SetParent(_extras, false);
                    gate.transform.localPosition = new Vector3(x, 0.75f, 0.9f);
                    gate.transform.localScale = new Vector3(0.08f, 1.5f, 0.5f);
                    gate.GetComponent<MeshRenderer>().sharedMaterial = M.Aluminium;
                }
            }
            if (b.Security >= 2)
            {
                foreach (var p in new[] { new Vector3(-hw + 0.6f, _height - 0.15f, 0.8f), new Vector3(hw - 0.6f, _height - 0.15f, _depth - 0.8f) })
                {
                    var cam = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(cam.GetComponent<Collider>());
                    cam.transform.SetParent(_extras, false);
                    cam.transform.localPosition = p;
                    cam.transform.localScale = new Vector3(0.22f, 0.14f, 0.22f);
                    cam.GetComponent<MeshRenderer>().sharedMaterial = M.Dark;
                }
            }
            if (b.Security >= 3 && _kind != PropertyKind.FoodStall)
            {
                var guard = CharacterRig.Create(_extras, CharacterRig.Worker);
                guard.transform.localPosition = new Vector3(-1.9f, 0f, 1.2f);
                guard.transform.localRotation = Quaternion.Euler(0, 160f, 0);
                guard.gameObject.AddComponent<StaffAnimator>().Rig = guard;
            }
        }

        public static Color BoardColor(BusinessType t, int interior)
        {
            Color c;
            switch (t)
            {
                case BusinessType.Cafe: c = new Color(0.14f, 0.3f, 0.22f); break;
                case BusinessType.Restaurant: c = new Color(0.36f, 0.08f, 0.1f); break;
                case BusinessType.FastFood: c = new Color(0.8f, 0.2f, 0.1f); break;
                case BusinessType.IceCream: c = new Color(0.9f, 0.45f, 0.65f); break;
                case BusinessType.ClothingStore: c = new Color(0.06f, 0.06f, 0.07f); break;
                case BusinessType.Electronics: c = new Color(0.08f, 0.3f, 0.6f); break;
                case BusinessType.Toys: c = new Color(0.95f, 0.65f, 0.1f); break;
                case BusinessType.Cosmetics: c = new Color(0.75f, 0.35f, 0.55f); break;
                case BusinessType.Bookstore: c = new Color(0.25f, 0.18f, 0.12f); break;
                case BusinessType.Jewelry: c = new Color(0.12f, 0.1f, 0.06f); break;
                case BusinessType.Supermarket: c = new Color(0.1f, 0.45f, 0.22f); break;
                case BusinessType.Warehouse: c = new Color(0.1f, 0.18f, 0.35f); break;
                default: c = new Color(0.2f, 0.22f, 0.26f); break;
            }
            return Color.Lerp(c, c * 1.25f, (interior - 1) / 4f);
        }

        static Color TextColor(BusinessType t)
        {
            switch (t)
            {
                case BusinessType.Cafe: return new Color(0.98f, 0.92f, 0.78f);
                case BusinessType.Restaurant: case BusinessType.Jewelry: return new Color(0.95f, 0.8f, 0.45f);
                case BusinessType.Supermarket: return new Color(1f, 0.95f, 0.4f);
                case BusinessType.Toys: return new Color(0.15f, 0.1f, 0.4f);
                default: return Color.white;
            }
        }
    }

    /// <summary>Обратная сторона вывески киоска повторяет основную.</summary>
    public sealed class SignMirror : MonoBehaviour
    {
        [SerializeField] StorefrontView _view;
        [SerializeField] MeshRenderer _board;
        [SerializeField] TextMesh _text;
        [SerializeField] MeshRenderer _sourceBoard;
        [SerializeField] TextMesh _sourceText;
        float _timer;

        public void Init(StorefrontView view, MeshRenderer board, TextMesh text)
        {
            _view = view; _board = board; _text = text;
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f || _view == null) return;
            _timer = 0.7f;
            if (_sourceText == null)
            {
                var sign = _view.transform.Find("Sign");
                if (sign == null) return;
                _sourceBoard = sign.Find("Board").GetComponent<MeshRenderer>();
                _sourceText = sign.Find("Text").GetComponent<TextMesh>();
            }
            _board.sharedMaterial = _sourceBoard.sharedMaterial;
            _text.color = _sourceText.color;
            WorldText.SetText(_text, _sourceText.text);
        }
    }
}
