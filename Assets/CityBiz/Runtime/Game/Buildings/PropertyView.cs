using System.Collections.Generic;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Визуальное представление помещения: вывеска, баннер «Сдаётся», режим работы, интерьер под тип бизнеса,
    /// персонал за стойкой, летняя веранда, двери. Перестраивается только при изменении состояния.
    /// Интерьер активен лишь вблизи камеры — это главный способ держать сцену лёгкой.
    /// </summary>
    public sealed class PropertyView : MonoBehaviour, IInteractable
    {
        public static readonly List<PropertyView> All = new List<PropertyView>();
        const float InteriorVisibleDistance = 45f;

        [SerializeField] string _lotKey;
        [SerializeField] GroundUnitKind _kind;
        [SerializeField] Vector3 _size;
        [SerializeField] float _openingTop, _doorWidth;
        [SerializeField] AutoDoor _door;
        [SerializeField] MeshRenderer _signBoard;
        [SerializeField] TextMesh _sign, _banner, _hours;
        [SerializeField] Transform _terrace, _interior, _gate;
        [SerializeField] bool _hasTerrace;

        public int PropertyId { get; private set; }
        public string LotKey => _lotKey;
        public InteriorInfo Interior { get; private set; }
        public bool InteriorActive => _interior != null && _interior.gameObject.activeSelf && Interior != null;

        string _signature;
        string _interiorKey;
        float _refreshTimer, _cullTimer, _gateOpen;
        bool _terraceBuilt;
        BusinessType _type;
        bool _open;
        readonly List<NpcBody> _staff = new List<NpcBody>();

        public void Configure(string lotKey, GroundUnitKind kind, Vector3 size, float openingTop, float doorWidth, AutoDoor door,
            MeshRenderer signBoard, TextMesh sign, TextMesh banner, TextMesh hours, Transform terrace, Transform interior, bool hasTerrace)
        {
            _lotKey = lotKey; _kind = kind; _size = size; _openingTop = openingTop; _doorWidth = doorWidth; _door = door;
            _signBoard = signBoard; _sign = sign; _banner = banner; _hours = hours; _terrace = terrace; _interior = interior; _hasTerrace = hasTerrace;
        }

        public void SetGate(Transform gate) => _gate = gate;

        void OnEnable() { All.Add(this); Interaction.Register(this); }
        void OnDisable() { All.Remove(this); Interaction.Unregister(this); }

        bool Resolve()
        {
            if (PropertyId != 0) return true;
            var game = CityGame.Instance;
            var district = GetComponentInParent<DistrictInstance>();
            if (game == null || game.World == null || district == null) return false;
            var def = game.World.FindByLot(district.DistrictId, _lotKey);
            if (def == null) return false;
            PropertyId = def.Id;
            return true;
        }

        // ---------- Точки для NPC ----------

        bool IsDock => _kind == GroundUnitKind.LoadingDock;
        public Vector3 DoorOutside => transform.TransformPoint(IsDock ? new Vector3(0, -1.1f, -3.6f) : new Vector3(0, 0, -1.1f));
        public Vector3 DoorInside => transform.TransformPoint(new Vector3(0, 0, 1.3f));
        public bool AcceptsCustomers => _open && _type != BusinessType.None && EconomyCatalog.Get(_type).FootTraffic;
        public BusinessType BusinessType => _type;

        public Vector3 InteractPosition => transform.TransformPoint(IsDock ? new Vector3(0, -0.1f, -3.4f) : new Vector3(0, 1f, -0.9f));
        public bool CanInteract => PropertyId != 0 && CityGame.Instance != null && CityGame.Instance.Ready;
        public void Interact() => CityGame.Instance.UI.OpenProperty(PropertyId);

        public string Prompt
        {
            get
            {
                var state = CityGame.Instance.State;
                var prop = state.GetProperty(PropertyId);
                var b = state.GetBusiness(prop.BusinessId);
                string status;
                if (b != null) status = $"«{b.Name}» — {(b.IsOpenNow ? "открыто" : "закрыто")}";
                else if (prop.ForRent && prop.TenantId == 0) status = $"сдаётся за {EconomyCatalog.Money(prop.RentPrice)}/день";
                else if (prop.OwnerId == 0 || prop.ForSale) status = "продаётся";
                else status = "свободно";
                return $"[E] {prop.Def.Name}: {status}";
            }
        }

        // ---------- Обновление ----------

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
                bool visible = (cam - transform.position).sqrMagnitude < InteriorVisibleDistance * InteriorVisibleDistance;
                if (_interior.gameObject.activeSelf != visible) _interior.gameObject.SetActive(visible);
                if (visible) EnsureInterior(game);
            }

            if (_gate != null)
            {
                float target = _open ? 1f : 0f;
                _gateOpen = Mathf.MoveTowards(_gateOpen, target, Time.deltaTime * 0.4f);
                _gate.localPosition = new Vector3(0, 2.15f + _gateOpen * 3.9f, 0.05f);
                _gate.localScale = new Vector3(_gate.localScale.x, 4.3f - _gateOpen * 3.6f, _gate.localScale.z);
            }
        }

        bool IsFood => _type == BusinessType.Cafe || _type == BusinessType.Restaurant;

        void Refresh(CityGame game)
        {
            var state = game.State;
            var prop = state.GetProperty(PropertyId);
            if (prop == null) return;
            var b = state.GetBusiness(prop.BusinessId);
            bool night = DayNightCycle.IsDark;
            string sig = $"{b?.Id}|{b?.Type}|{b?.Name}|{b?.Level}|{b?.IsOpenNow}|{prop.ForRent}|{prop.ForSale}|{prop.TenantId}|{prop.OwnerId}|{night}|{b?.Employees.Count}";
            if (sig == _signature) return;
            _signature = sig;

            _type = b?.Type ?? BusinessType.None;
            _open = b != null && b.IsOpenNow;
            var M = MaterialLibrary.Instance;
            var def = b?.Def;

            // Вывеска
            if (b != null)
            {
                WorldText.SetText(_sign, b.Name);
                var color = BoardColor(b.Type);
                _signBoard.sharedMaterial = night && _open ? M.Emissive("signlit_" + b.Type, color, color * 1.4f) : M.Paint("signboard", color);
                _sign.color = TextColor(b.Type);
            }
            else
            {
                WorldText.SetText(_sign, IsDock ? "СКЛАД СВОБОДЕН" : "СВОБОДНО");
                _signBoard.sharedMaterial = M.Paint("signboard", new Color(0.9f, 0.9f, 0.88f));
                _sign.color = new Color(0.75f, 0.1f, 0.1f);
            }

            // Баннер на стекле
            string banner = null;
            if (b == null && prop.TenantId == 0 && prop.ForRent) banner = $"СДАЁТСЯ\n{EconomyCatalog.Money(prop.RentPrice)}/день";
            if (prop.ForSale || (prop.OwnerId == 0 && b == null)) banner = banner == null ? "ПРОДАЁТСЯ" : banner + "\nили продажа";
            if (b != null && !b.IsOpenNow && b.Employees.Count < def.MinStaff) banner = "ИЩЕМ\nСОТРУДНИКОВ";
            if (_banner != null)
            {
                var root = _banner.transform.parent != transform ? _banner.transform.parent.gameObject : _banner.gameObject;
                root.SetActive(banner != null);
                if (banner != null) WorldText.SetText(_banner, banner);
            }

            if (_hours != null)
                WorldText.SetText(_hours, def != null ? $"{(def.CloseHour >= 24 && def.OpenHour == 0 ? "24 часа" : $"{def.OpenHour:00}:00–{def.CloseHour % 24:00}:00")}\n{(_open ? "ОТКРЫТО" : "ЗАКРЫТО")}" : "");

            if (_door != null) _door.Locked = !_open;

            // Летняя веранда — только у кафе и ресторанов
            if (_terrace != null)
            {
                bool show = _hasTerrace && IsFood;
                if (show && !_terraceBuilt) { InteriorBuilder.BuildTerrace(M, _terrace, _size.x, _type); _terraceBuilt = true; }
                _terrace.gameObject.SetActive(show);
            }

            if (_interior.gameObject.activeSelf) EnsureInterior(game);
            SyncStaff(b);
            if (Interior != null && Interior.Light != null)
                Interior.Light.enabled = _open || (b == null && night);
        }

        void EnsureInterior(CityGame game)
        {
            var prop = game.State.GetProperty(PropertyId);
            var b = prop != null ? game.State.GetBusiness(prop.BusinessId) : null;
            string key = $"{_type}|{b?.Level ?? 0}";
            if (key == _interiorKey && Interior != null) return;
            _interiorKey = key;
            foreach (Transform child in _interior) Destroy(child.gameObject);
            _staff.Clear();
            Interior = InteriorBuilder.Build(MaterialLibrary.Instance, _interior, _type, b?.Level ?? 1, _size, IsDock ? 0.35f : 0.32f, _openingTop, PropertyId);
            if (Interior.Light != null) Interior.Light.enabled = _open || b == null;
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
                var body = NpcBody.Create(Interior.transform, NpcLook.Staff(e.Seed, e.Female, UniformColor(b.Type)));
                var p = Interior.WorkPoints[i];
                body.transform.localPosition = new Vector3(p.x, 0, p.z);
                body.transform.localRotation = Quaternion.Euler(0, p.w, 0);
                body.gameObject.AddComponent<IdleAnimator>().Body = body;
                _staff.Add(body);
            }
        }

        public static Color BoardColor(BusinessType t)
        {
            switch (t)
            {
                case BusinessType.Cafe: return new Color(0.12f, 0.3f, 0.2f);
                case BusinessType.Restaurant: return new Color(0.35f, 0.07f, 0.1f);
                case BusinessType.ClothingStore: return new Color(0.06f, 0.06f, 0.07f);
                case BusinessType.Supermarket: return new Color(0.1f, 0.45f, 0.22f);
                case BusinessType.Warehouse: return new Color(0.1f, 0.18f, 0.35f);
                default: return new Color(0.2f, 0.22f, 0.26f);
            }
        }

        static Color TextColor(BusinessType t)
        {
            switch (t)
            {
                case BusinessType.Cafe: return new Color(0.98f, 0.92f, 0.78f);
                case BusinessType.Restaurant: return new Color(0.95f, 0.8f, 0.45f);
                case BusinessType.Supermarket: return new Color(1f, 0.95f, 0.4f);
                default: return Color.white;
            }
        }

        public static Color UniformColor(BusinessType t)
        {
            switch (t)
            {
                case BusinessType.Cafe: return new Color(0.35f, 0.22f, 0.14f);
                case BusinessType.Restaurant: return new Color(0.92f, 0.92f, 0.9f);
                case BusinessType.ClothingStore: return new Color(0.1f, 0.1f, 0.12f);
                case BusinessType.Supermarket: return new Color(0.15f, 0.5f, 0.25f);
                case BusinessType.Warehouse: return new Color(0.95f, 0.55f, 0.1f);
                default: return new Color(0.25f, 0.3f, 0.4f);
            }
        }
    }
}
