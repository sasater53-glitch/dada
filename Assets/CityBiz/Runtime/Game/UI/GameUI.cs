using System.Collections.Generic;
using System.Linq;
using CityBiz.Core;
using UnityEngine;

namespace CityBiz.Game
{
    /// <summary>
    /// Интерфейс на IMGUI (не требует ассетов и пакетов UI): главное меню, HUD с прицелом, панели помещения и бизнеса
    /// (главное — развитие предприятия), вредительство, портфель, рейтинг, сделки, чат, поэтажная схема ТЦ, лифт,
    /// справка, титры с лицензиями и отладочная информация.
    /// Все действия отправляются как команды на сервер; UI только отображает реплицированное состояние.
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        enum Panel { None, Property, Portfolio, Map, Help, Pause, Sabotage, Elevator, Credits }
        enum PortfolioTab { Businesses, Assets, Deals, Rating, News }

        Panel _panel;
        PortfolioTab _tab;
        int _propertyId, _sabotageTarget;
        ElevatorPanel _elevator;
        MallInstance _mapMall;
        int _mapFloor;
        Vector2 _scroll, _scroll2;
        bool _debug, _chatOpen, _focusChat;
        int _chatOpenedFrame = -10;
        string _chat = "", _playerName = "Игрок", _host = "127.0.0.1", _port = "7777";
        string _bizName = "", _salePrice = "", _rentPrice = "", _offerPrice = "", _transfer = "1000";
        int _offerTarget, _transferTarget;
        float _priceSlider = 1f, _fadeUntil;
        int _priceBusiness = -1;
        readonly List<(string text, float until, bool ok)> _toasts = new List<(string, float, bool)>();

        GUIStyle _panelStyle, _barStyle, _h1, _h2, _label, _small, _button, _accentButton, _dangerButton, _prompt, _toast, _toastBad, _field, _tabOn, _tabOff, _mapLabel;
        Texture2D _white;
        float _scale = 1f;
        Vector2 _virtual;

        public bool CapturesInput
        {
            get
            {
                var g = CityGame.Instance;
                return g == null || !g.SessionStarted || !g.Ready || _panel != Panel.None || _chatOpen;
            }
        }

        public void Toast(string text, bool ok = true)
        {
            if (string.IsNullOrEmpty(text)) return;
            _toasts.Add((text, Time.time + 4.5f, ok));
            if (_toasts.Count > 5) _toasts.RemoveAt(0);
        }

        public void OpenProperty(int propertyId)
        {
            _propertyId = propertyId;
            ResetFields();
            SetPanel(Panel.Property, true);
        }

        public void OpenMap(MallInstance mall, int floor)
        {
            _mapMall = mall;
            _mapFloor = floor;
            SetPanel(Panel.Map);
        }

        public void OpenElevator(ElevatorPanel panel)
        {
            _elevator = panel;
            SetPanel(Panel.Elevator);
        }

        /// <summary>Затемнение экрана (поездка на лифте).</summary>
        public void Fade(float seconds) => _fadeUntil = Time.time + seconds;

        void SetPanel(Panel panel, bool force = false)
        {
            if (panel == _panel && !force) return;
            bool wasOpen = _panel != Panel.None;
            _panel = panel;
            _scroll = Vector2.zero;
            // Фокус ввода сбрасывается при каждой смене панели — иначе горячие клавиши «залипают» после ввода текста
            GUIUtility.keyboardControl = 0;
            if (panel != Panel.None) MallAudio.Instance?.Ui(UiSound.Open);
            else if (wasOpen) MallAudio.Instance?.Ui(UiSound.Close);
        }

        void Close() => SetPanel(Panel.None);
        void Toggle(Panel panel) => SetPanel(_panel == panel ? Panel.None : panel);

        void ResetFields()
        {
            _bizName = ""; _salePrice = ""; _rentPrice = ""; _offerPrice = ""; _priceBusiness = -1;
        }

        void OpenMapHere(CityGame g)
        {
            var mall = g.CurrentMall;
            int floor = mall != null ? mall.FloorOfWorld(g.ViewPosition) : 0;
            OpenMap(mall, floor);
        }

        void Update()
        {
            var g = CityGame.Instance;
            if (g == null || !g.SessionStarted || !g.Ready) return;
            // Пока идёт ввод текста, буквенные горячие клавиши не перехватываем. Esc обрабатывается в OnGUI
            // через Event и работает всегда, даже если поле ввода в фокусе.
            if (_chatOpen || GUIUtility.keyboardControl != 0) return;
            if (InputAdapter.Down(GameKey.Portfolio)) Toggle(Panel.Portfolio);
            if (InputAdapter.Down(GameKey.Map)) { if (_panel == Panel.Map) Close(); else OpenMapHere(g); }
            if (InputAdapter.Down(GameKey.Help)) Toggle(Panel.Help);
            if (InputAdapter.Down(GameKey.Debug)) _debug = !_debug;
            if (InputAdapter.Down(GameKey.Chat) && _panel == Panel.None)
            {
                _chatOpen = true;
                _focusChat = true;
                _chatOpenedFrame = Time.frameCount;
            }
        }

        // ---------------- Стили ----------------

        Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void EnsureStyles()
        {
            if (_panelStyle != null) return;
            _white = Tex(Color.white);
            var skin = GUI.skin;
            _panelStyle = new GUIStyle(skin.box) { padding = new RectOffset(18, 18, 14, 14) };
            _panelStyle.normal.background = Tex(new Color(0.07f, 0.08f, 0.1f, 0.93f));
            _barStyle = new GUIStyle(_panelStyle) { padding = new RectOffset(14, 14, 6, 6) };
            _barStyle.normal.background = Tex(new Color(0.05f, 0.06f, 0.08f, 0.85f));
            _h1 = new GUIStyle(skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, wordWrap = true };
            _h1.normal.textColor = Color.white;
            _h2 = new GUIStyle(skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = true, richText = true };
            _h2.normal.textColor = new Color(0.95f, 0.8f, 0.45f);
            _label = new GUIStyle(skin.label) { fontSize = 15, wordWrap = true, richText = true };
            _label.normal.textColor = new Color(0.9f, 0.92f, 0.95f);
            _small = new GUIStyle(_label) { fontSize = 13 };
            _small.normal.textColor = new Color(0.7f, 0.74f, 0.8f);
            _mapLabel = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            _mapLabel.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
            _button = new GUIStyle(skin.button) { fontSize = 15, padding = new RectOffset(10, 10, 6, 6), richText = true };
            _button.normal.background = Tex(new Color(0.2f, 0.23f, 0.28f));
            _button.hover.background = Tex(new Color(0.28f, 0.32f, 0.38f));
            _button.active.background = Tex(new Color(0.15f, 0.17f, 0.2f));
            _button.normal.textColor = _button.hover.textColor = _button.active.textColor = Color.white;
            _accentButton = new GUIStyle(_button);
            _accentButton.normal.background = Tex(new Color(0.12f, 0.45f, 0.3f));
            _accentButton.hover.background = Tex(new Color(0.16f, 0.55f, 0.36f));
            _dangerButton = new GUIStyle(_button);
            _dangerButton.normal.background = Tex(new Color(0.55f, 0.14f, 0.14f));
            _dangerButton.hover.background = Tex(new Color(0.65f, 0.18f, 0.18f));
            _prompt = new GUIStyle(_label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _toast = new GUIStyle(_panelStyle) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            _toast.normal.background = Tex(new Color(0.08f, 0.3f, 0.18f, 0.92f));
            _toast.normal.textColor = Color.white;
            _toastBad = new GUIStyle(_toast);
            _toastBad.normal.background = Tex(new Color(0.4f, 0.1f, 0.1f, 0.92f));
            _field = new GUIStyle(skin.textField) { fontSize = 15, padding = new RectOffset(6, 6, 5, 5) };
            _tabOn = new GUIStyle(_accentButton);
            _tabOff = new GUIStyle(_button);
        }

        // ---------------- Корень ----------------

        void OnGUI()
        {
            var g = CityGame.Instance;
            if (g == null) return;
            EnsureStyles();
            _scale = Screen.height / 1080f;
            _virtual = new Vector2(Screen.width / _scale, 1080f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_scale, _scale, 1f));

            if (!g.SessionStarted)
            {
                if (_panel == Panel.Credits) Window(980, 900, CreditsPanel);
                else MainMenu(g);
                return;
            }
            if (!g.Ready) { Center(460, 140, () => GUILayout.Label("Подключение к серверу…\n" + g.SessionInfo, _h2)); return; }

            HandleEscape();
            TopBar(g);
            News(g);
            Crosshair();
            Prompt();
            Toasts();
            Chat(g);
            switch (_panel)
            {
                case Panel.Property: Window(820, 940, () => PropertyPanel(g)); break;
                case Panel.Portfolio: Window(980, 900, () => PortfolioPanel(g)); break;
                case Panel.Map: MapPanel(g); break;
                case Panel.Help: Window(820, 820, HelpPanel); break;
                case Panel.Pause: Window(560, 720, () => PausePanel(g)); break;
                case Panel.Sabotage: Window(720, 740, () => SabotagePanel(g)); break;
                case Panel.Elevator: Window(520, 460, () => ElevatorWindow(g)); break;
                case Panel.Credits: Window(980, 900, CreditsPanel); break;
            }
            if (_debug) DebugPanel(g);
            FadeOverlay();
        }

        /// <summary>Esc ловится через Event до отрисовки полей ввода — работает, даже если текстовое поле в фокусе.</summary>
        void HandleEscape()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown || e.keyCode != KeyCode.Escape) return;
            if (_chatOpen) { _chatOpen = false; GUIUtility.keyboardControl = 0; }
            else if (_panel == Panel.Sabotage) SetPanel(Panel.Property);
            else if (_panel != Panel.None) Close();
            else SetPanel(Panel.Pause);
            e.Use();
        }

        void Center(float w, float h, System.Action body)
        {
            GUILayout.BeginArea(new Rect((_virtual.x - w) * 0.5f, (_virtual.y - h) * 0.5f, w, h), _panelStyle);
            body();
            GUILayout.EndArea();
        }

        void Window(float w, float h, System.Action body)
        {
            h = Mathf.Min(h, _virtual.y - 140f);
            w = Mathf.Min(w, _virtual.x - 40f);
            GUILayout.BeginArea(new Rect((_virtual.x - w) * 0.5f, 90f, w, h), _panelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕  Закрыть (Esc)", _button, GUILayout.Width(170))) Close();
            GUILayout.EndHorizontal();
            _scroll = GUILayout.BeginScrollView(_scroll);
            body();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        bool Btn(string text, GUIStyle style = null, float width = 0f)
        {
            bool clicked = width > 0f ? GUILayout.Button(text, style ?? _button, GUILayout.Width(width)) : GUILayout.Button(text, style ?? _button);
            if (clicked)
            {
                GUIUtility.keyboardControl = 0;
                MallAudio.Instance?.Ui(UiSound.Click);
            }
            return clicked;
        }

        static string M(long v) => EconomyCatalog.Money(v);
        static string M(float v) => EconomyCatalog.Money(v);

        static string Bar(float v, int len = 10)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v * len), 0, len);
            return new string('█', n) + new string('░', len - n);
        }

        static string Stars(float v)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v * 5f), 0, 5);
            return new string('★', n) + new string('☆', 5 - n);
        }

        static string Pips(int level, int min, int max) => new string('●', Mathf.Clamp(level - min, 0, max - min)) + new string('○', Mathf.Clamp(max - level, 0, max - min));

        // ---------------- Главное меню ----------------

        void MainMenu(CityGame g)
        {
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.DrawTexture(new Rect(0, 0, _virtual.x, _virtual.y), _white);
            GUI.color = Color.white;
            Center(660, 720, () =>
            {
                GUILayout.Label("CITY BIZ: ГАЛЕРЕЯ", _h1);
                GUILayout.Label("Бизнес от первого лица в огромном торговом центре", _h2);
                GUILayout.Label("Три этажа вдоль 140-метрового атриума, эскалаторы, панорамный лифт, островные киоски, фудкорт и якорный супермаркет. " +
                                "Арендуйте или покупайте помещения, открывайте магазины и кафе, нанимайте персонал и улучшайте свои предприятия — " +
                                "интерьер, оборудование, ассортимент, витрину и охрану. Конкуренты-ИИ и другие игроки не дремлют.", _label);
                GUILayout.Space(14);
                GUILayout.Label("Ваше имя", _small);
                _playerName = GUILayout.TextField(_playerName, 24, _field);
                GUILayout.Space(10);
                if (Btn("▶  Одиночная игра (с ИИ-конкурентами)", _accentButton)) { GUIUtility.keyboardControl = 0; g.StartSolo(_playerName); }
                GUILayout.Space(14);
                GUILayout.Label("Игра по локальной сети", _h2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Порт", _small, GUILayout.Width(50));
                _port = GUILayout.TextField(_port, 5, _field, GUILayout.Width(90));
                if (Btn("Создать сервер и играть")) { GUIUtility.keyboardControl = 0; g.StartHost(_playerName, ParsePort()); }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Адрес", _small, GUILayout.Width(50));
                _host = GUILayout.TextField(_host, 64, _field, GUILayout.Width(200));
                if (Btn("Подключиться")) { GUIUtility.keyboardControl = 0; g.Join(_playerName, _host, ParsePort()); }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
                GUILayout.Label("Управление: мышь — осмотр, WASD — ходьба, Shift — бег, Пробел — прыжок, Ctrl — присесть, E — взаимодействие, Tab — портфель, M — схема ТЦ, F1 — справка.", _small);
                if (!string.IsNullOrEmpty(g.SessionInfo)) GUILayout.Label(g.SessionInfo, _small);
                GUILayout.Space(6);
                if (Btn("Авторы и лицензии")) SetPanel(Panel.Credits);
            });
        }

        int ParsePort() => int.TryParse(_port, out int p) && p > 0 && p < 65536 ? p : 7777;

        // ---------------- HUD ----------------

        void TopBar(CityGame g)
        {
            var s = g.State;
            var me = g.Client.LocalPlayer;
            var mall = g.CurrentMall;
            GUILayout.BeginArea(new Rect(10, 10, _virtual.x - 20, 58), _barStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{s.Time}</b>", _label, GUILayout.Width(210));
            string mallState = DayNightCycle.MallOpen ? $"<color=#7CFC9A>ТЦ открыт до {EconomyCatalog.MallCloseHour}:00</color>" : $"<color=#FFB86B>ТЦ закрыт · откроется в {EconomyCatalog.MallOpenHour}:00</color>";
            GUILayout.Label(mallState, _label, GUILayout.Width(250));
            if (mall != null) GUILayout.Label($"{mall.FloorOfWorld(g.ViewPosition) + 1} этаж", _label, GUILayout.Width(80));
            GUILayout.Label(WeatherState.DisplayName(s.Weather.Kind), _label, GUILayout.Width(100));
            if (me != null)
            {
                GUILayout.Label($"<color=#7CFC9A><b>{M(me.Cash)}</b></color>", _label, GUILayout.Width(160));
                GUILayout.Label($"Капитал: {M(me.NetWorth)}", _label, GUILayout.Width(210));
                GUILayout.Label($"За вчера: {(me.YesterdayIncome >= 0 ? "<color=#7CFC9A>+" : "<color=#FF7B7B>")}{M(me.YesterdayIncome)}</color>", _label, GUILayout.Width(190));
            }
            GUILayout.FlexibleSpace();
            if (g.Client.IsHost)
            {
                foreach (var (label, scale) in new[] { ("⏸", 0f), ("×1", 1f), ("×3", 3f), ("×10", 10f), ("×30", 30f) })
                {
                    var style = Mathf.Approximately(s.TimeScale, scale) ? _tabOn : _tabOff;
                    if (GUILayout.Button(label, style, GUILayout.Width(52))) g.Send(Command.Make(CommandType.SetTimeScale, f: scale));
                }
            }
            if (Btn("Предприятия [Tab]", null, 170)) { _tab = PortfolioTab.Businesses; SetPanel(Panel.Portfolio); }
            if (Btn("Схема [M]", null, 110)) OpenMapHere(g);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void Crosshair()
        {
            if (_panel != Panel.None || _chatOpen) return;
            var player = FirstPersonController.Instance;
            bool focus = player != null && player.Focus != null;
            float size = focus ? 8f : 4f;
            GUI.color = focus ? new Color(1f, 0.85f, 0.35f, 0.95f) : new Color(1f, 1f, 1f, 0.7f);
            GUI.DrawTexture(new Rect(_virtual.x * 0.5f - size * 0.5f, _virtual.y * 0.5f - size * 0.5f, size, size), _white);
            GUI.color = Color.white;
        }

        void Prompt()
        {
            if (_panel != Panel.None) return;
            var player = FirstPersonController.Instance;
            var focus = player != null ? player.Focus : null;
            string text = focus != null ? focus.Prompt : null;
            if (string.IsNullOrEmpty(text)) return;
            var r = new Rect(_virtual.x * 0.5f - 400f, _virtual.y * 0.5f + 60f, 800f, 48f);
            GUI.Box(r, GUIContent.none, _barStyle);
            GUI.Label(r, text, _prompt);
        }

        void Toasts()
        {
            float y = 80f;
            for (int i = _toasts.Count - 1; i >= 0; i--)
                if (_toasts[i].until < Time.time) _toasts.RemoveAt(i);
            foreach (var t in _toasts)
            {
                GUI.Label(new Rect(_virtual.x - 490f, y, 470f, 54f), t.text, t.ok ? _toast : _toastBad);
                y += 62f;
            }
        }

        void News(CityGame g)
        {
            var news = g.Client.News;
            int count = Mathf.Min(6, news.Count);
            if (count == 0) return;
            GUILayout.BeginArea(new Rect(10, _virtual.y - 60 - count * 24, 680, count * 24 + 8));
            for (int i = news.Count - count; i < news.Count; i++)
            {
                var n = news[i];
                string color = n.Category == NewsCategory.Sabotage || n.Category == NewsCategory.Police ? "#FF9B7B" : n.Category == NewsCategory.Chat ? "#9BD3FF" : n.Category == NewsCategory.Business ? "#FFE08A" : "#D8DEE9";
                GUILayout.Label($"<color=#8892A0>{new GameTime(n.Minute).Clock}</color>  <color={color}>{n.Text}</color>", _small);
            }
            GUILayout.EndArea();
        }

        void Chat(CityGame g)
        {
            if (!_chatOpen) return;
            var r = new Rect(10, _virtual.y - 48, 680, 36);
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                // Тот же Enter, которым чат открыли в Update, приходит в OnGUI в этом же кадре — его пропускаем
                if (Time.frameCount - _chatOpenedFrame <= 1) { e.Use(); }
                else
                {
                    if (!string.IsNullOrWhiteSpace(_chat)) g.Send(Command.Make(CommandType.Chat, text: _chat));
                    _chat = "";
                    _chatOpen = false;
                    GUIUtility.keyboardControl = 0;
                    e.Use();
                    return;
                }
            }
            GUI.SetNextControlName("chat");
            _chat = GUI.TextField(r, _chat, 140, _field);
            if (_focusChat) { GUI.FocusControl("chat"); _focusChat = false; }
            GUI.Label(new Rect(r.x, r.y - 22, 600, 22), "Enter — отправить · Esc — отмена", _small);
        }

        void FadeOverlay()
        {
            float left = _fadeUntil - Time.time;
            if (left <= 0f) return;
            GUI.color = new Color(0, 0, 0, Mathf.Clamp01(left * 2f));
            GUI.DrawTexture(new Rect(0, 0, _virtual.x, _virtual.y), _white);
            GUI.color = Color.white;
        }

        // ---------------- Помещение ----------------

        void PropertyPanel(CityGame g)
        {
            var s = g.State;
            var prop = s.GetProperty(_propertyId);
            if (prop == null || prop.Def == null) { Close(); return; }
            int me = g.LocalPlayerId;
            var def = prop.Def;
            var b = s.GetBusiness(prop.BusinessId);

            GUILayout.Label(def.Name, _h1);
            GUILayout.Label($"{def.Address} · {EconomyCatalog.KindName(def.Kind)} · {def.Area:0} м² · проходимость {def.FootTraffic:0.00}", _small);
            GUILayout.Space(6);
            GUILayout.Label($"Владелец: <b>{s.PlayerName(prop.OwnerId)}</b>{(prop.OwnerId == me ? " (вы)" : "")}" +
                            (prop.TenantId != 0 ? $"   Арендатор: <b>{s.PlayerName(prop.TenantId)}</b>{(prop.TenantId == me ? " (вы)" : "")}" : ""), _label);
            GUILayout.Label($"Рыночная стоимость: {M(s.PropertyValue(prop))}   Аренда: {M(prop.RentPrice)}/день   Состояние: {Bar(prop.Condition)} {prop.Condition:P0}", _label);
            if (prop.DebtDays > 0) GUILayout.Label($"<color=#FF7B7B>Долг по аренде: {prop.DebtDays} дн. (выселение через {EconomyCatalog.EvictionDebtDays - prop.DebtDays})</color>", _label);
            GUILayout.Space(10);

            // Покупка / аренда
            GUILayout.BeginHorizontal();
            if (EconomyRules.CanBuy(me, prop) && Btn($"Купить за {M(EconomyRules.PurchasePrice(s, prop))}", _accentButton))
                g.Send(Command.Make(CommandType.BuyProperty, prop.Id));
            if (EconomyRules.CanRent(me, prop) && Btn($"Арендовать за {M(prop.RentPrice)}/день", _accentButton))
                g.Send(Command.Make(CommandType.RentProperty, prop.Id));
            if (prop.TenantId == me && Btn("Расторгнуть аренду", _dangerButton))
                g.Send(Command.Make(CommandType.EndLease, prop.Id));
            GUILayout.EndHorizontal();

            // Открытие бизнеса
            if (EconomyRules.CanOperate(me, prop) && b == null)
            {
                GUILayout.Space(10);
                GUILayout.Label("Открыть бизнес", _h2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Название:", _small, GUILayout.Width(90));
                _bizName = GUILayout.TextField(_bizName, 28, _field);
                GUILayout.EndHorizontal();
                foreach (var t in EconomyCatalog.All.Where(t => t.Allows(def.Kind)))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>{t.Name}</b> — {t.Description}\n<color=#AAB4C0>Запуск {M(t.SetupCost)} · персонал {t.MinStaff}–{t.MaxStaff} · {t.OpenHour:00}:00–{t.CloseHour % 24:00}:00</color>", _label);
                    if (Btn("Открыть", _accentButton, 110)) g.Send(Command.Make(CommandType.OpenBusiness, prop.Id, (int)t.Type, text: _bizName));
                    GUILayout.EndHorizontal();
                }
            }

            if (b != null)
            {
                GUILayout.Space(10);
                if (b.OwnerId == me) BusinessManagement(g, b, prop);
                else ForeignBusiness(g, b, prop);
            }

            if (prop.OwnerId == me)
            {
                GUILayout.Space(12);
                GUILayout.Label("Управление собственностью", _h2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Сдавать за /день:", _small, GUILayout.Width(190));
                _rentPrice = GUILayout.TextField(_rentPrice, 9, _field, GUILayout.Width(110));
                if (Btn("Сдавать")) g.Send(Command.Make(CommandType.ListForRent, prop.Id, amount: ParseMoney(_rentPrice, prop.RentPrice)));
                if (Btn("Снять с аренды")) g.Send(Command.Make(CommandType.ListForRent, prop.Id, amount: 0));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Продать за:", _small, GUILayout.Width(190));
                _salePrice = GUILayout.TextField(_salePrice, 10, _field, GUILayout.Width(110));
                if (Btn(prop.ForSale ? "Изменить цену" : "Выставить")) g.Send(Command.Make(CommandType.ListForSale, prop.Id, amount: ParseMoney(_salePrice, s.PropertyValue(prop))));
                if (prop.ForSale && Btn("Снять с продажи")) g.Send(Command.Make(CommandType.ListForSale, prop.Id, amount: 0));
                GUILayout.EndHorizontal();
                var others = s.Players.Values.Where(p => p.Id != me).ToList();
                if (others.Count > 0)
                {
                    _offerTarget = Mathf.Clamp(_offerTarget, 0, others.Count - 1);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Предложить игроку:", _small, GUILayout.Width(190));
                    if (Btn("◀", null, 34)) _offerTarget = (_offerTarget + others.Count - 1) % others.Count;
                    GUILayout.Label(others[_offerTarget].Name, _label, GUILayout.Width(200));
                    if (Btn("▶", null, 34)) _offerTarget = (_offerTarget + 1) % others.Count;
                    _offerPrice = GUILayout.TextField(_offerPrice, 10, _field, GUILayout.Width(110));
                    if (Btn("Отправить")) g.Send(Command.Make(CommandType.OfferPropertyToPlayer, prop.Id, others[_offerTarget].Id, ParseMoney(_offerPrice, s.PropertyValue(prop))));
                    GUILayout.EndHorizontal();
                }
                GUILayout.BeginHorizontal();
                if (Btn($"Продать управляющей компании ТЦ за {M((long)(s.PropertyValue(prop) * EconomyCatalog.SellBackRatio))}", _dangerButton))
                    g.Send(Command.Make(CommandType.SellPropertyToCity, prop.Id));
                GUILayout.EndHorizontal();
            }

            long repair = EconomyRules.RepairCost(s, prop);
            if ((prop.OwnerId == me || EconomyRules.CanOperate(me, prop)) && repair > 0)
                if (Btn($"Ремонт помещения и оборудования — {M(repair)}")) g.Send(Command.Make(CommandType.Repair, prop.Id));
        }

        static long ParseMoney(string text, long fallback)
        {
            var digits = new string((text ?? "").Where(char.IsDigit).ToArray());
            return long.TryParse(digits, out long v) && v > 0 ? v : fallback;
        }

        static string UpgradeEffect(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Interior: return "в зале: пол, свет, растения, мебель";
                case UpgradeKind.Marketing: return "у входа: световая вывеска, промо-стойка, LED-экран, баннер";
                case UpgradeKind.Equipment: return "больше касс и стоек, +1 место для персонала";
                case UpgradeKind.Assortment: return "больше стеллажей и товара, выше средний чек";
                default: return "антикражные ворота, камеры, охранник у входа";
            }
        }

        /// <summary>Развитие предприятия — основной игровой цикл: каждое улучшение видно в помещении.</summary>
        void UpgradePanel(CityGame g, BusinessState b, PropertyState prop)
        {
            var me = g.Client.LocalPlayer;
            GUILayout.Label($"Развитие предприятия   <color=#AAB4C0>{Bar(b.DevelopmentScore, 14)} {b.DevelopmentScore:P0}</color>", _h2);
            bool renovating = g.State.Time.WholeMinutes < b.ClosedUntilMinute;
            if (renovating) GUILayout.Label("<color=#FFB86B>Идёт ремонт — точка временно закрыта.</color>", _small);
            foreach (var u in EconomyCatalog.AllUpgrades)
            {
                int level = b.GetLevel(u.Kind);
                long cost = EconomyRules.NextUpgradeCost(b, prop, u.Kind);
                GUILayout.BeginHorizontal();
                string extra = (u.RenovationHours > 0 ? $" · ремонт {u.RenovationHours} ч" : "") + (u.DailyCostPerLevel > 0 ? $" · {M(u.DailyCostPerLevel)}/день за уровень" : "");
                GUILayout.Label($"<b>{u.Name}</b>  <color=#FFE08A>{Pips(level, u.MinLevel, u.MaxLevel)}</color> ур. {level}/{u.MaxLevel}\n<color=#AAB4C0>{u.Description} Видно: {UpgradeEffect(u.Kind)}{extra}</color>", _label);
                if (cost < 0) GUILayout.Label("<color=#7CFC9A>максимум</color>", _label, GUILayout.Width(170));
                else
                {
                    bool afford = me != null && me.Cash >= cost;
                    GUI.enabled = afford;
                    if (Btn($"Улучшить — {M(cost)}", afford ? _accentButton : _button, 170)) g.Send(Command.Make(CommandType.UpgradeBusiness, b.Id, (int)u.Kind));
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }
        }

        void BusinessManagement(CityGame g, BusinessState b, PropertyState prop)
        {
            var s = g.State;
            var def = b.Def;
            if (def == null) return;
            float quality = CitySimulation.QualityOf(b, prop);
            GUILayout.Label($"Ваш бизнес: «{b.Name}» ({def.Name})", _h2);
            string status = b.IsOpenNow ? "<color=#7CFC9A>ОТКРЫТО</color>" : b.Employees.Count < def.MinStaff ? $"<color=#FF7B7B>ЗАКРЫТО — нужно минимум {def.MinStaff} сотрудн.</color>" : "<color=#FFB86B>ЗАКРЫТО</color>";
            GUILayout.Label($"{status}   Режим: {def.OpenHour:00}:00–{def.CloseHour % 24:00}:00", _label);
            GUILayout.Label($"Сегодня: {b.TodayCustomers} клиентов · выручка {M(b.TodayRevenue)} · расходы {M(b.TodayCosts)}   В этот час: {b.LastHourCustomers}", _label);
            GUILayout.Label($"Вчера: {b.YesterdayCustomers} клиентов · выручка {M(b.YesterdayRevenue)} · прибыль {(b.YesterdayProfit >= 0 ? "<color=#7CFC9A>" : "<color=#FF7B7B>")}{M(b.YesterdayProfit)}</color> · всего {M(b.TotalProfit)}", _label);
            GUILayout.Label($"Репутация {Stars(b.Reputation)}  Удовлетворённость {Bar(b.Satisfaction)}  Качество {Bar(quality)}  Износ оборудования {Bar(b.EquipmentCondition)}", _label);
            GUILayout.Space(8);
            UpgradePanel(g, b, prop);
            GUILayout.Space(8);

            if (def.FootTraffic)
            {
                if (_priceBusiness != b.Id) { _priceBusiness = b.Id; _priceSlider = b.PriceLevel; }
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Цены: {_priceSlider:P0}", _label, GUILayout.Width(130));
                _priceSlider = GUILayout.HorizontalSlider(_priceSlider, 0.6f, 1.8f, GUILayout.Width(300));
                if (Btn("Применить", null, 120)) g.Send(Command.Make(CommandType.SetPrice, b.Id, f: _priceSlider));
                GUILayout.Label("<color=#AAB4C0>дешевле — больше клиентов, дороже — выше маржа</color>", _small);
                GUILayout.EndHorizontal();
            }

            if (def.UsesStock)
            {
                float unit = EconomyRules.StockUnitCost(s, b);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Товар на складе: <b>{b.Stock:0}</b> ед. · закупка {M(unit)}/ед.", _label, GUILayout.Width(340));
                if (Btn("+100", null, 70)) g.Send(Command.Make(CommandType.OrderStock, b.Id, amount: 100));
                if (Btn("+500", null, 70)) g.Send(Command.Make(CommandType.OrderStock, b.Id, amount: 500));
                bool auto = GUILayout.Toggle(b.AutoRestock, " Автозакупка", GUILayout.Width(140));
                if (auto != b.AutoRestock) g.Send(Command.Make(CommandType.SetAutoRestock, b.Id, auto ? 1 : 0));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label($"Персонал ({b.Employees.Count}/{b.MaxStaff}) · зарплаты {M(b.DailyWages)}/день", _h2);
            foreach (var e in b.Employees.ToList())
            {
                GUILayout.BeginHorizontal();
                bool fit = def.Roles != null && System.Array.IndexOf(def.Roles, e.Role) >= 0;
                GUILayout.Label($"{e.Name} · {EconomyCatalog.RoleName(e.Role)}{(fit ? "" : " <color=#FFB86B>(не по профилю)</color>")} · навык {Stars(e.Skill)} · {M(e.Wage)}/день · мотивация {Bar(e.Morale, 8)}", _label);
                if (Btn("+15%", null, 64)) g.Send(Command.Make(CommandType.RaiseWage, b.Id, e.Id, (long)(e.Wage * 1.15f)));
                if (Btn("Уволить", _dangerButton, 92)) g.Send(Command.Make(CommandType.FireEmployee, b.Id, e.Id));
                GUILayout.EndHorizontal();
            }
            if (b.Employees.Count < b.MaxStaff)
            {
                GUILayout.Label("Рынок труда (ставка — в день)", _small);
                var roles = def.Roles ?? new StaffRole[0];
                foreach (var c in s.Candidates.OrderByDescending(c => System.Array.IndexOf(roles, c.Role) >= 0).ThenByDescending(c => c.Skill).Take(6).ToList())
                {
                    GUILayout.BeginHorizontal();
                    bool fit = System.Array.IndexOf(roles, c.Role) >= 0;
                    GUILayout.Label($"{c.Name} · {(fit ? "<color=#7CFC9A>" : "<color=#AAB4C0>")}{EconomyCatalog.RoleName(c.Role)}</color> · {Stars(c.Skill)} · {M(c.Wage)}/день", _label);
                    if (Btn("Нанять", _accentButton, 100)) g.Send(Command.Make(CommandType.HireEmployee, b.Id, c.Id));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Space(6);
            if (Btn("Закрыть бизнес", _dangerButton)) g.Send(Command.Make(CommandType.CloseBusiness, b.Id));
        }

        void ForeignBusiness(CityGame g, BusinessState b, PropertyState prop)
        {
            var s = g.State;
            GUILayout.Label($"«{b.Name}» — {b.Def?.Name}", _h2);
            GUILayout.Label($"Владелец: {s.PlayerName(b.OwnerId)} · {(b.IsOpenNow ? "открыто" : "закрыто")} · цены {b.PriceLevel:P0} · репутация {Stars(b.Reputation)}", _label);
            GUILayout.Label($"Вчера: {b.YesterdayCustomers} клиентов, выручка ~{M(b.YesterdayRevenue)}", _label);
            var levels = string.Join("  ", EconomyCatalog.AllUpgrades.Select(u => $"{u.Name} {b.GetLevel(u.Kind)}/{u.MaxLevel}"));
            GUILayout.Label($"<color=#AAB4C0>Развитие конкурента: {levels}</color>", _small);
            if (Btn("Навредить конкуренту…", _dangerButton)) { _sabotageTarget = b.Id; SetPanel(Panel.Sabotage); }
        }

        // ---------------- Вредительство ----------------

        void SabotagePanel(CityGame g)
        {
            var s = g.State;
            var target = s.GetBusiness(_sabotageTarget);
            var me = g.Client.LocalPlayer;
            if (target == null || me == null) { Close(); return; }
            GUILayout.Label($"Против «{target.Name}»", _h1);
            GUILayout.Label("Грязные методы конкуренции. Если исполнителей поймают — штраф ×2.5, ваша деловая репутация и репутация ваших бизнесов пострадают, а имя окажется в новостях.", _label);
            int cd = EconomyRules.SabotageCooldownLeft(s, me);
            GUILayout.Label($"Подозрения полиции: {Bar(Mathf.Clamp01(me.Heat))}{(me.Heat >= 1f ? " <color=#FF7B7B>— затаитесь</color>" : "")}" + (cd > 0 ? $"   Перезарядка: {cd / 60}ч {cd % 60}мин" : ""), _label);
            GUILayout.Space(8);
            foreach (var def in SabotageCatalog.All)
            {
                GUILayout.BeginHorizontal();
                float success = EconomyRules.SabotageSuccess(def, target), detect = EconomyRules.SabotageDetection(def, target, me);
                GUILayout.Label($"<b>{def.Name}</b> — {def.Description}\n<color=#AAB4C0>Стоимость {M(def.Cost)} · успех ~{success:P0} · риск разоблачения ~{detect:P0}</color>", _label);
                GUI.enabled = cd == 0 && me.Heat < 1f && me.Cash >= def.Cost;
                if (Btn("Заказать", _dangerButton, 110)) g.Send(Command.Make(CommandType.Sabotage, target.Id, (int)def.Kind));
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (Btn("← Назад к помещению")) { _propertyId = target.PropertyId; SetPanel(Panel.Property); }
        }

        // ---------------- Лифт ----------------

        void ElevatorWindow(CityGame g)
        {
            var mall = _elevator != null ? _elevator.Mall : null;
            if (mall == null) { Close(); return; }
            GUILayout.Label("Панорамный лифт", _h1);
            GUILayout.Label("Выберите этаж:", _label);
            GUILayout.Space(6);
            foreach (var stop in mall.ElevatorStops.OrderByDescending(x => x.Floor))
            {
                bool here = stop.Floor == _elevator.Floor;
                string what = FloorDescription(mall, stop.Floor);
                GUI.enabled = !here;
                if (Btn($"{stop.Floor + 1} этаж{(string.IsNullOrEmpty(what) ? "" : " — " + what)}{(here ? "  (вы здесь)" : "")}", here ? _button : _accentButton))
                {
                    int floor = stop.Floor;
                    Close();
                    Fade(0.6f);
                    FirstPersonController.Instance?.RideElevator(mall, floor);
                }
                GUI.enabled = true;
            }
        }

        static string FloorDescription(MallInstance mall, int floor)
        {
            var parts = new List<string>();
            if (floor == 0) parts.Add("главный вход");
            foreach (var shape in mall.Map)
                if (shape.Floor == floor && shape.Kind == 4 && !string.IsNullOrEmpty(shape.Label)) parts.Add(shape.Label.ToLowerInvariant());
            int units = mall.Map.Count(x => x.Floor == floor && x.Kind == 1);
            parts.Add($"помещений: {units}");
            return string.Join(", ", parts);
        }

        // ---------------- Портфель ----------------

        void PortfolioPanel(CityGame g)
        {
            var s = g.State;
            int me = g.LocalPlayerId;
            GUILayout.BeginHorizontal();
            foreach (var (tab, name) in new[] { (PortfolioTab.Businesses, "Мои предприятия"), (PortfolioTab.Assets, "Помещения"), (PortfolioTab.Deals, "Сделки и переводы"), (PortfolioTab.Rating, "Рейтинг"), (PortfolioTab.News, "Новости") })
                if (GUILayout.Button(name, _tab == tab ? _tabOn : _tabOff)) { _tab = tab; MallAudio.Instance?.Ui(UiSound.Click); }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            switch (_tab)
            {
                case PortfolioTab.Businesses:
                {
                    bool any = false;
                    var mePlayer = g.Client.LocalPlayer;
                    foreach (var b in s.Businesses.Values.Where(b => b.OwnerId == me).ToList())
                    {
                        any = true;
                        var prop = s.GetProperty(b.PropertyId);
                        GUILayout.BeginVertical(_barStyle);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"<b>{b.Name}</b> ({b.Def?.Name}) · {prop?.Def?.Address} · {(b.IsOpenNow ? "<color=#7CFC9A>открыто</color>" : "<color=#FFB86B>закрыто</color>")}\n" +
                                        $"<color=#AAB4C0>вчера {b.YesterdayCustomers} кл., прибыль {M(b.YesterdayProfit)} · {Stars(b.Reputation)} · развитие {Bar(b.DevelopmentScore, 10)} {b.DevelopmentScore:P0}</color>", _label);
                        if (Btn("Управлять", null, 120)) OpenProperty(b.PropertyId);
                        GUILayout.EndHorizontal();
                        if (prop != null)
                        {
                            GUILayout.BeginHorizontal();
                            foreach (var u in EconomyCatalog.AllUpgrades)
                            {
                                long cost = EconomyRules.NextUpgradeCost(b, prop, u.Kind);
                                int level = b.GetLevel(u.Kind);
                                if (cost < 0) { GUILayout.Label($"<color=#7CFC9A>{u.Name} {level} ✓</color>", _small, GUILayout.Width(170)); continue; }
                                GUI.enabled = mePlayer != null && mePlayer.Cash >= cost;
                                if (Btn($"{u.Name} {level}→{level + 1}\n<size=12>{M(cost)}</size>", _accentButton, 170)) g.Send(Command.Make(CommandType.UpgradeBusiness, b.Id, (int)u.Kind));
                                GUI.enabled = true;
                            }
                            GUILayout.EndHorizontal();
                        }
                        GUILayout.EndVertical();
                        GUILayout.Space(4);
                    }
                    if (!any) GUILayout.Label("Пока нет. Найдите помещение с баннером «СДАЁТСЯ» (схема ТЦ — M, жёлтые помещения), арендуйте его, откройте бизнес и наймите персонал. Затем улучшайте предприятие — это главный способ обойти конкурентов.", _label);
                    break;
                }

                case PortfolioTab.Assets:
                {
                    bool any = false;
                    foreach (var p in s.Properties.Values.Where(p => p.OwnerId == me || p.TenantId == me).ToList())
                    {
                        any = true;
                        GUILayout.BeginHorizontal();
                        string rel = p.OwnerId == me ? "собственность" : $"аренда {M(p.RentPrice)}/день";
                        GUILayout.Label($"<b>{p.Def?.Name}</b> · {p.Def?.Address} · {rel} · стоимость {M(s.PropertyValue(p))}{(p.LastIncome > 0 ? $" · доход {M(p.LastIncome)}/день" : "")}", _label);
                        if (Btn("Открыть", null, 110)) OpenProperty(p.Id);
                        GUILayout.EndHorizontal();
                    }
                    if (!any) GUILayout.Label("Нет помещений.", _small);
                    break;
                }

                case PortfolioTab.Deals:
                {
                    GUILayout.Label("Входящие предложения", _h2);
                    var offers = s.Offers.Values.Where(o => o.ToPlayerId == me).ToList();
                    if (offers.Count == 0) GUILayout.Label("Нет предложений.", _small);
                    foreach (var o in offers)
                    {
                        var p = s.GetProperty(o.PropertyId);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{s.PlayerName(o.FromPlayerId)} продаёт «{p?.Def?.Name}» за <b>{M(o.Price)}</b> (рынок {M(p != null ? s.PropertyValue(p) : 0)})", _label);
                        if (Btn("Принять", _accentButton, 110)) g.Send(Command.Make(CommandType.RespondOffer, o.Id, 1));
                        if (Btn("Отклонить", _dangerButton, 120)) g.Send(Command.Make(CommandType.RespondOffer, o.Id, 0));
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.Label("Исходящие", _h2);
                    foreach (var o in s.Offers.Values.Where(o => o.FromPlayerId == me).ToList())
                        GUILayout.Label($"«{s.GetProperty(o.PropertyId)?.Def?.Name}» → {s.PlayerName(o.ToPlayerId)} за {M(o.Price)}", _label);
                    GUILayout.Label("Денежный перевод", _h2);
                    var others = s.Players.Values.Where(p => p.Id != me).ToList();
                    if (others.Count > 0)
                    {
                        _transferTarget = Mathf.Clamp(_transferTarget, 0, others.Count - 1);
                        GUILayout.BeginHorizontal();
                        if (Btn("◀", null, 34)) _transferTarget = (_transferTarget + others.Count - 1) % others.Count;
                        GUILayout.Label(others[_transferTarget].Name, _label, GUILayout.Width(240));
                        if (Btn("▶", null, 34)) _transferTarget = (_transferTarget + 1) % others.Count;
                        _transfer = GUILayout.TextField(_transfer, 10, _field, GUILayout.Width(120));
                        if (Btn("Перевести")) g.Send(Command.Make(CommandType.TransferMoney, others[_transferTarget].Id, amount: ParseMoney(_transfer, 0)));
                        GUILayout.EndHorizontal();
                    }
                    break;
                }

                case PortfolioTab.Rating:
                {
                    GUILayout.Label("Рейтинг предпринимателей ТЦ", _h2);
                    int rank = 1;
                    foreach (var p in s.Players.Values.OrderByDescending(p => p.NetWorth).ToList())
                    {
                        int biz = s.Businesses.Values.Count(b => b.OwnerId == p.Id);
                        int props = s.Properties.Values.Count(x => x.OwnerId == p.Id);
                        string col = MaterialLibrary.Hex(RemotePlayers.ColorFrom(p.Color));
                        GUILayout.Label($"{rank++}. <color=#{col}>■</color> <b>{p.Name}</b>{(p.Id == me ? " (вы)" : "")}{(p.IsAI ? " · ИИ" : p.Online ? " · онлайн" : " · офлайн")}\n<color=#AAB4C0>капитал {M(p.NetWorth)} · наличные {M(p.Cash)} · бизнесов {biz} · помещений {props} · деловая репутация {Stars(p.Reputation)}</color>", _label);
                    }
                    break;
                }

                case PortfolioTab.News:
                {
                    _scroll2 = GUILayout.BeginScrollView(_scroll2, GUILayout.Height(600));
                    for (int i = g.Client.News.Count - 1; i >= 0; i--)
                    {
                        var n = g.Client.News[i];
                        GUILayout.Label($"<color=#8892A0>{new GameTime(n.Minute)}</color>  {n.Text}", _label);
                    }
                    GUILayout.EndScrollView();
                    break;
                }
            }
        }

        // ---------------- Схема ТЦ ----------------

        void MapPanel(CityGame g)
        {
            var mall = _mapMall != null ? _mapMall : g.CurrentMall;
            if (mall == null) { Close(); return; }
            var s = g.State;
            int me = g.LocalPlayerId;
            var b = mall.Bounds;
            if (b.width <= 0f || b.height <= 0f) { Close(); return; }
            _mapFloor = Mathf.Clamp(_mapFloor, 0, Mathf.Max(0, mall.Floors - 1));
            float w = Mathf.Min(_virtual.x - 120f, 1640f), h = w * b.height / b.width;
            if (h > _virtual.y - 300f) { h = _virtual.y - 300f; w = h * b.width / b.height; }
            var area = new Rect((_virtual.x - w) * 0.5f, 170f, w, h);
            GUI.Box(new Rect(area.x - 16, area.y - 106, area.width + 32, area.height + 170), GUIContent.none, _panelStyle);
            GUI.Label(new Rect(area.x, area.y - 98, 900, 36), $"Схема: {mall.DisplayName} — {_mapFloor + 1} этаж", _h2);
            if (GUI.Button(new Rect(area.xMax - 170, area.y - 100, 170, 36), "✕  Закрыть (M)", _button)) Close();
            for (int f = 0; f < mall.Floors; f++)
                if (GUI.Button(new Rect(area.x + f * 140, area.y - 54, 130, 36), $"{f + 1} этаж", f == _mapFloor ? _tabOn : _tabOff))
                {
                    _mapFloor = f;
                    MallAudio.Instance?.Ui(UiSound.Click);
                }

            Vector2 ToScreen(Vector2 p) => new Vector2(area.x + (p.x - b.xMin) / b.width * w, area.y + (b.yMax - p.y) / b.height * h);
            Rect R(Vector2 min, Vector2 max)
            {
                var a = ToScreen(new Vector2(min.x, max.y));
                var c = ToScreen(new Vector2(max.x, min.y));
                return Rect.MinMaxRect(Mathf.Max(a.x, area.x), Mathf.Max(a.y, area.y), Mathf.Min(c.x, area.xMax), Mathf.Min(c.y, area.yMax));
            }
            GUI.color = new Color(0.12f, 0.13f, 0.15f);
            GUI.DrawTexture(area, _white);
            foreach (var kind in new[] { 0, 5, 4, 1, 2, 3 })
                foreach (var shape in mall.Map)
                {
                    if (shape == null || shape.Kind != kind || shape.Floor != _mapFloor) continue;
                    var r = R(shape.Min, shape.Max);
                    if (r.width <= 0 || r.height <= 0) continue;
                    Color c;
                    string label = shape.Label;
                    switch (kind)
                    {
                        case 0: c = new Color(0.36f, 0.37f, 0.4f); break;
                        case 5: c = new Color(0.16f, 0.2f, 0.26f); break;
                        case 4: c = new Color(0.45f, 0.38f, 0.28f); break;
                        case 2: c = new Color(0.3f, 0.55f, 0.75f); break;
                        case 3: c = new Color(0.55f, 0.4f, 0.75f); break;
                        default: c = UnitColor(s, mall, shape, me, out label); break;
                    }
                    GUI.color = c;
                    GUI.DrawTexture(r, _white);
                    GUI.color = Color.white;
                    if (kind == 1)
                    {
                        // Рамка между соседними помещениями
                        GUI.color = new Color(0.1f, 0.1f, 0.12f);
                        GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), _white);
                        GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), _white);
                        GUI.color = Color.white;
                        if (!string.IsNullOrEmpty(label) && r.width > 30f) GUI.Label(r, label, _mapLabel);
                        if (GUI.Button(r, GUIContent.none, GUIStyle.none)) OpenFromMap(g, mall, shape);
                    }
                    else if (!string.IsNullOrEmpty(label) && r.width > 24f) GUI.Label(r, label, _small);
                }
            GUI.color = Color.white;

            var player = FirstPersonController.Instance;
            if (player != null && mall.Contains(player.transform.position) && mall.FloorOfWorld(player.transform.position) == _mapFloor)
            {
                var local = mall.transform.InverseTransformPoint(player.transform.position);
                var sp = ToScreen(new Vector2(local.x, local.z));
                var fwd = mall.transform.InverseTransformDirection(Quaternion.Euler(0, player.Yaw, 0) * Vector3.forward);
                var tip = sp + new Vector2(fwd.x, -fwd.z) * 14f;
                GUI.color = new Color(0.2f, 0.85f, 1f);
                GUI.DrawTexture(new Rect(sp.x - 7, sp.y - 7, 14, 14), _white);
                GUI.DrawTexture(new Rect(tip.x - 3, tip.y - 3, 6, 6), _white);
                GUI.color = Color.white;
                GUI.Label(new Rect(sp.x + 10, sp.y - 10, 60, 20), "вы", _small);
            }
            float ly = area.yMax + 12;
            Legend(area.x, ly, new Color(0.25f, 0.75f, 0.35f), "ваше");
            Legend(area.x + 110, ly, new Color(0.95f, 0.8f, 0.25f), "сдаётся / продаётся");
            Legend(area.x + 320, ly, new Color(0.62f, 0.5f, 0.75f), "конкуренты");
            Legend(area.x + 470, ly, new Color(0.7f, 0.7f, 0.72f), "занято / свободно");
            Legend(area.x + 660, ly, new Color(0.3f, 0.55f, 0.75f), "эскалатор");
            Legend(area.x + 790, ly, new Color(0.55f, 0.4f, 0.75f), "лифт");
            GUI.Label(new Rect(area.x + 900, ly - 4, 700, 30), "Нажмите на помещение, чтобы открыть его карточку.", _small);
        }

        void Legend(float x, float y, Color c, string text)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y + 4, 16, 16), _white);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 22, y, 200, 26), text, _small);
        }

        static Color UnitColor(CityState s, MallInstance mall, MapShape shape, int me, out string label)
        {
            label = shape.LotKey;
            var def = !string.IsNullOrEmpty(shape.LotKey) ? s.World.FindByLot(mall.MallId, shape.LotKey) : null;
            var p = def != null ? s.GetProperty(def.Id) : null;
            if (p == null) return new Color(0.5f, 0.5f, 0.52f);
            var b = s.GetBusiness(p.BusinessId);
            if (b != null) label = shape.LotKey + "\n" + b.Name;
            if (p.OwnerId == me || p.TenantId == me) return new Color(0.25f, 0.75f, 0.35f);
            if (b == null && (p.ForSale || (p.ForRent && p.TenantId == 0))) return new Color(0.95f, 0.8f, 0.25f);
            if (b != null && b.OwnerId != 0) return new Color(0.62f, 0.5f, 0.75f);
            return new Color(0.7f, 0.7f, 0.72f);
        }

        void OpenFromMap(CityGame g, MallInstance mall, MapShape shape)
        {
            if (string.IsNullOrEmpty(shape.LotKey) || g.World == null) return;
            var def = g.World.FindByLot(mall.MallId, shape.LotKey);
            if (def != null) OpenProperty(def.Id);
        }

        // ---------------- Справка, пауза, титры, отладка ----------------

        void HelpPanel()
        {
            GUILayout.Label("Как играть", _h1);
            GUILayout.Label("<b>Цель</b> — стать самым успешным предпринимателем ТРЦ «Галерея» (вкладка «Рейтинг» в портфеле).", _label);
            GUILayout.Label("<b>Помещения.</b> Подойдите к витрине с баннером «СДАЁТСЯ», наведите прицел и нажмите E. Помещение можно арендовать (платёж каждый день) или купить. " +
                            "Бывают магазины вдоль галерей, островные киоски в атриуме, точки фудкорта на 3 этаже, склады и якорное помещение. Найти свободные — схема ТЦ (M) или стенды «Схема ТЦ».", _label);
            GUILayout.Label("<b>Бизнес.</b> Откройте магазин одежды, электроники, игрушек, косметики, книг, ювелирный, кафе, ресторан, фастфуд, мороженое, супермаркет или склад. Наймите минимум сотрудников — они встанут за стойки.", _label);
            GUILayout.Label("<b>Развитие — главное.</b> Улучшайте интерьер, оборудование, ассортимент, витрину/рекламу и охрану. Каждое улучшение видно: меняется зал, появляются кассы, стеллажи, световая вывеска, экраны, камеры и охранник. " +
                            "Улучшения повышают качество, средний чек и поток покупателей; ремонт ненадолго закрывает точку.", _label);
            GUILayout.Label("<b>Покупатели</b> выбирают между конкурентами по цене, качеству, репутации и проходимости места (у эскалаторов, входа и на фудкорте выше). ТЦ работает с " +
                            $"{EconomyCatalog.MallOpenHour}:00 до {EconomyCatalog.MallCloseHour}:00; в дождь посетителей больше, по выходным — ещё больше.", _label);
            GUILayout.Label("<b>Товар.</b> Магазинам нужен запас; собственный склад снижает закупочные цены на 30%.", _label);
            GUILayout.Label("<b>Конкуренция и вредительство.</b> Конкуренты (игроки и ИИ) могут портить репутацию, ломать оборудование, переманивать сотрудников. Защищайтесь охраной; отвечайте тем же — но за пойманных исполнителей отвечаете вы.", _label);
            GUILayout.Label("<b>Игроки.</b> Сдавайте помещения друг другу, продавайте их (карточка помещения), переводите деньги и общайтесь в чате (Enter).", _label);
            GUILayout.Space(8);
            GUILayout.Label("<b>Управление (от первого лица)</b>: мышь — осмотр · WASD — ходьба · Shift — бег · Пробел — прыжок · Ctrl — присесть · E — взаимодействие (прицел на витрину, киоск, стенд, кнопку лифта) · " +
                            "эскалаторы везут сами — просто встаньте на ленту · Tab — портфель и улучшения · M — схема этажей · Enter — чат · Esc — меню · F1 — справка · F3 — статистика производительности.", _label);
        }

        void CreditsPanel()
        {
            GUILayout.Label("Авторы и лицензии", _h1);
            GUILayout.Label("Модели, текстуры, звуки и музыка загружены из открытых источников. Спасибо авторам!", _label);
            GUILayout.Space(6);
            GUILayout.Label("Обязательная атрибуция (CC BY)", _h2);
            foreach (var line in Credits.RequiredAttributions) GUILayout.Label("• " + line, _label);
            foreach (var (title, body) in Credits.Sections)
            {
                GUILayout.Space(8);
                GUILayout.Label(title, _h2);
                GUILayout.Label(body, _small);
            }
            GUILayout.Space(8);
            GUILayout.Label("Полный список — в файле THIRD_PARTY_ASSETS.md в корне проекта.", _small);
        }

        void PausePanel(CityGame g)
        {
            GUILayout.Label("Пауза", _h1);
            GUILayout.Label(g.SessionInfo, _small);
            GUILayout.Space(8);
            var audio = MallAudio.Instance;
            if (audio != null)
            {
                GUILayout.Label($"Громкость: {audio.MasterVolume:P0}", _label);
                audio.MasterVolume = GUILayout.HorizontalSlider(audio.MasterVolume, 0f, 1f);
                GUILayout.Label($"Музыка ТЦ: {audio.MusicVolume:P0}", _label);
                audio.MusicVolume = GUILayout.HorizontalSlider(audio.MusicVolume, 0f, 1f);
            }
            GUILayout.Label($"Чувствительность мыши: {FirstPersonController.MouseSensitivity:0.00}", _label);
            FirstPersonController.MouseSensitivity = GUILayout.HorizontalSlider(FirstPersonController.MouseSensitivity, 0.03f, 0.5f);
            if (PedestrianSystem.Instance != null)
            {
                GUILayout.Label($"Плотность посетителей: {PedestrianSystem.Instance.DensityMultiplier:P0}", _label);
                PedestrianSystem.Instance.DensityMultiplier = GUILayout.HorizontalSlider(PedestrianSystem.Instance.DensityMultiplier, 0.3f, 1.5f);
            }
            GUILayout.Label($"Качество графики: {QualitySettings.names[QualitySettings.GetQualityLevel()]}", _label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (Btn(QualitySettings.names[i])) QualitySettings.SetQualityLevel(i, true);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            if (Btn("Продолжить", _accentButton)) Close();
            if (Btn("Справка (F1)")) SetPanel(Panel.Help);
            if (Btn("Авторы и лицензии")) SetPanel(Panel.Credits);
            if (Btn("Выйти из игры", _dangerButton)) Application.Quit();
        }

        float _fps, _fpsTimer;
        int _frames;

        void DebugPanel(CityGame g)
        {
            _frames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f) { _fps = _frames / _fpsTimer; _frames = 0; _fpsTimer = 0f; }
            GUILayout.BeginArea(new Rect(_virtual.x - 400, 80, 390, 240), _barStyle);
            GUILayout.Label($"FPS: {_fps:0}", _label);
            var peds = PedestrianSystem.Instance;
            GUILayout.Label($"Посетители: {(peds != null ? peds.ActiveCount : 0)} (видно {(peds != null ? peds.VisibleCount : 0)})", _small);
            GUILayout.Label($"Инстансинг: {(g.Instanced != null ? g.Instanced.DrawnBatches : 0)} батчей · {(g.Instanced != null ? g.Instanced.TotalInstances : 0)} объектов", _small);
            GUILayout.Label($"Интерьеры активны: {StorefrontView.All.Count(v => v != null && v.InteriorActive)}/{StorefrontView.All.Count}", _small);
            GUILayout.Label($"Реальных источников света: {(LightBudget.Instance != null ? LightBudget.Instance.ActiveLights : 0)}", _small);
            GUILayout.Label(g.NetworkStats, _small);
            GUILayout.EndArea();
        }
    }
}
