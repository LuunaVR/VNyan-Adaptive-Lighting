using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using VNyanAdaptiveLighting.Configuration;
using VNyanAdaptiveLighting.Plugin;
using VNyanInterface;

namespace VNyanAdaptiveLighting.UI
{
    /// <summary>
    /// Settings window built with VNyan's code-driven UI API (IUIInterface): a tab bar plus one scrollable page
    /// per tab, with VNyan content-panel "cards" grouping the controls. Every control writes straight into the
    /// settings and notifies, so changes apply live with no Apply button.
    /// </summary>
    public sealed class LightingSettingsWindow
    {
        private const int WindowW = 430;
        private const int WindowH = 540;
        private const int Pad = 10;
        private const int ScrollbarW = 12;
        private const int SideMargin = 16;
        private const int RowH = 22;
        private const int SliderRowH = 54;
        private const int HeaderH = 30;
        private const int CardGap = 8;
        private const int TabBarH = 40;
        private const float DiagInterval = 0.25f;

        // VNyan's UI coordinate convention is undocumented, so it is measured per panel at runtime:
        //   topOffset  = TopA  + SignY * y + TopK  * h   (distance from the panel's top edge, downward)
        //   leftOffset = LeftA + SignX * x + LeftK * w
        private struct Mapping
        {
            public float SignY, TopA, TopK, SignX, LeftA, LeftK;
            public static Mapping Default => new Mapping { SignY = 1f, SignX = 1f };
            public int Y(int top, int h) => Mathf.RoundToInt(SignY * (top - TopA - TopK * h));
            public int X(int left, int w) => Mathf.RoundToInt(SignX * (left - LeftA - LeftK * w));
        }

        private sealed class Card
        {
            public IVNyanUIContentPanel Panel;
            public Mapping Map = Mapping.Default;
            public bool Dry;
            public int Y;
            public int W;
        }

        private sealed class Page
        {
            public string Name;
            public List<KeyValuePair<string, Action<Card>>> Sections;
            public IVNyanUIContentPanel Panel;
            public RectTransform Rt;
            public IVNyanUIButton Tab;
            public Image TabImage;
            public Color TabColor;
        }

        private readonly AdaptiveLightingHost _host;
        private readonly SettingsManager _mgr;
        private IVNyanUIWindow _window;
        private RectTransform _windowRt, _viewportRt;
        private ScrollRect _scroll;
        private readonly List<Page> _pages = new List<Page>();
        private int _activePage;
        private bool _syncing;
        private bool _visible;
        private float _nextDiag;

        private readonly List<Action> _refreshers = new List<Action>();

        private IVNyanUILabel _statusLine, _lumLabel, _brightLabel, _rateLabel, 
            _perfLabel;
        
        private IVNyanUIButton _monoButton, _colorButton;
        private Graphic _statusGraphic;
        private Color _statusBase;
        private static readonly Color ErrorColor = new Color(1f, 0.35f, 0.35f, 1f);

        private AdaptiveLightingSettings S => _mgr.Current;

        public LightingSettingsWindow(AdaptiveLightingHost host, SettingsManager mgr)
        {
            _host = host;
            _mgr = mgr;
        }

        public bool Visible => _visible;

        public void Toggle() => SetVisible(!_visible);

        public void SetVisible(bool visible)
        {
            EnsureBuilt();
            if (_window == null) return;
            _visible = visible;
            _window.setVisible(visible);
            if (visible)
            {
                _window.bringToFront();
                RefreshFromSettings();
            }
        }

        public void Destroy()
        {
            try { _window?.destroyWindow(); } catch { }
            _window = null;
            _pages.Clear();
            _visible = false;
        }

        private void EnsureBuilt()
        {
            if (_window != null) return;
            var ui = VNyanInterface.VNyanInterface.VNyanUI;
            _window = ui.createDraggableWindow(WindowW, WindowH, true, true);
            if (_window == null) { Debug.LogError("[VNyanAdaptiveLighting] VNyan could not create the settings window."); return; }

            try
            {
                var windowPanel = _window.getContentPanel();
                _windowRt = ((GameObject)windowPanel.getPanelGameObject()).GetComponent<RectTransform>();
                BuildScrollArea();
                DefinePages();
                BuildPages();
                BuildTabBar(windowPanel);
                SelectPage(0);
                _mgr.SettingsChanged += OnExternalSettingsChanged;
                RefreshFromSettings();
            }
            catch (Exception ex)
            {
                Debug.LogError("[VNyanAdaptiveLighting] Could not build settings window: " + ex);
            }
        }

        // ---- scroll area -------------------------------------------------------------------------------

        private static Color ThemeColor(ThemeComponent component, Color fallback)
        {
            try
            {
                var s = VNyanInterface.VNyanInterface.VNyanUI.getCurrentThemeColor(component);
                if (string.IsNullOrEmpty(s)) return fallback;
                if (!s.StartsWith("#")) s = "#" + s;
                return ColorUtility.TryParseHtmlString(s, out var c) ? c : fallback;
            }
            catch { return fallback; }
        }

        private void BuildScrollArea()
        {
            var viewportGo = new GameObject("VAL_Viewport", typeof(RectTransform));
            _viewportRt = viewportGo.GetComponent<RectTransform>();
            _viewportRt.SetParent(_windowRt, false);
            _viewportRt.anchorMin = Vector2.zero;
            _viewportRt.anchorMax = Vector2.one;
            _viewportRt.pivot = new Vector2(0f, 1f);
            _viewportRt.offsetMin = new Vector2(0f, 4f);
            _viewportRt.offsetMax = new Vector2(0f, -(TabBarH + 6));
            viewportGo.AddComponent<RectMask2D>();
            var hit = viewportGo.AddComponent<Image>(); // transparent, only receives scroll/drag events
            hit.color = new Color(0f, 0f, 0f, 0f);

            // Slim themed scrollbar on the right, below the tab bar so it never covers the close button.
            var sbGo = new GameObject("VAL_Scrollbar", typeof(RectTransform));
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.SetParent(_windowRt, false);
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 0.5f);
            sbRt.offsetMin = new Vector2(-(ScrollbarW + 4), 8f);
            sbRt.offsetMax = new Vector2(-4f, -(TabBarH + 6));
            var sbBg = sbGo.AddComponent<Image>();
            sbBg.color = ThemeColor(ThemeComponent.PanelComponent, new Color(0.16f, 0.13f, 0.21f, 1f));

            var areaGo = new GameObject("Sliding Area", typeof(RectTransform));
            var areaRt = areaGo.GetComponent<RectTransform>();
            areaRt.SetParent(sbRt, false);
            areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = Vector2.zero; areaRt.offsetMax = Vector2.zero;

            var handleGo = new GameObject("Handle", typeof(RectTransform));
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.SetParent(areaRt, false);
            handleRt.offsetMin = Vector2.zero; handleRt.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = ThemeColor(ThemeComponent.Button, new Color(0.97f, 0.72f, 0.92f, 1f));

            var scrollbar = sbGo.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleImg;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            _scroll = viewportGo.AddComponent<ScrollRect>();
            _scroll.viewport = _viewportRt;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        // ---- coordinate measurement --------------------------------------------------------------------

        private static void Probe(IVNyanUIContentPanel panel, RectTransform rt, int x, int y, int w, int h, out float top, out float left)
        {
            int before = rt.childCount;
            VNyanInterface.VNyanInterface.VNyanUI.createLabel(panel, "", x, y, w, h);
            top = left = 0f;
            if (rt.childCount <= before) return;
            var probe = rt.GetChild(rt.childCount - 1) as RectTransform;
            if (probe == null) return;
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            probe.GetWorldCorners(corners); // 0 = bottom-left, 1 = top-left
            var local = rt.InverseTransformPoint(corners[1]);
            var rect = rt.rect;
            top = rect.yMax - local.y;
            left = local.x - rect.xMin;
            UnityEngine.Object.Destroy(probe.gameObject);
        }

        private static Mapping Measure(IVNyanUIContentPanel panel, RectTransform rt)
        {
            try
            {
                Probe(panel, rt, 10, 10, 50, 20, out var tA, out var lA);
                Probe(panel, rt, 10, 40, 50, 20, out var tB, out _);
                Probe(panel, rt, 10, 10, 50, 40, out var tC, out _);
                Probe(panel, rt, 40, 10, 50, 20, out _, out var lD);
                Probe(panel, rt, 10, 10, 100, 20, out _, out var lE);

                var m = new Mapping
                {
                    SignY = tB >= tA ? 1f : -1f,
                    TopK = (tC - tA) / 20f,
                    SignX = lD >= lA ? 1f : -1f,
                    LeftK = (lE - lA) / 50f
                };
                m.TopA = tA - m.SignY * 10f - m.TopK * 20f;
                m.LeftA = lA - m.SignX * 10f - m.LeftK * 50f;
                return m;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VNyanAdaptiveLighting] UI measurement failed, assuming top-left: " + ex.Message);
                return Mapping.Default;
            }
        }

        // ---- pages and tabs ---------------------------------------------------------------------------

        private static KeyValuePair<string, Action<Card>> Sec(string title, Action<Card> body)
            => new KeyValuePair<string, Action<Card>>(title, body);

        private void DefinePages()
        {
            _pages.Add(new Page { Name = "Lighting", Sections = new List<KeyValuePair<string, Action<Card>>> {
                Sec("", BuildLighting) } });
            _pages.Add(new Page { Name = "Source", Sections = new List<KeyValuePair<string, Action<Card>>> {
                Sec("", BuildSource) } });
            _pages.Add(new Page { Name = "Debug", Sections = new List<KeyValuePair<string, Action<Card>>> {
                Sec("", BuildStats), Sec("Test", BuildTest), Sec("", BuildSettings) } });
        }

        private void BuildPages()
        {
            var ui = VNyanInterface.VNyanInterface.VNyanUI;
            var windowPanel = _window.getContentPanel();
            Canvas.ForceUpdateCanvases();
            int viewW = Mathf.RoundToInt(_viewportRt.rect.width);
            int pageW = viewW > 0 ? viewW : WindowW - 6;
            int cardW = Mathf.Max(280, pageW - SideMargin * 2);

            foreach (var page in _pages)
            {
                // Dry pass: card heights, so every panel is created at its final size.
                var heights = new List<int>();
                int total = Pad;
                foreach (var s in page.Sections)
                {
                    int headerH = string.IsNullOrEmpty(s.Key) ? 8 : HeaderH;
                    var dry = new Card { Dry = true, Y = headerH, W = cardW - Pad * 2 };
                    s.Value(dry);
                    heights.Add(dry.Y + Pad);
                    total += dry.Y + Pad + CardGap;
                }

                page.Panel = ui.createContentPanel(windowPanel, 0, 0, pageW, total + Pad);
                var go = (GameObject)page.Panel.getPanelGameObject();
                page.Rt = go.GetComponent<RectTransform>();
                // The page itself is invisible; the cards provide the visible panels.
                foreach (var g in go.GetComponentsInChildren<Graphic>(true)) g.enabled = false;
                page.Rt.SetParent(_viewportRt, false);
                page.Rt.anchorMin = new Vector2(0f, 1f);
                page.Rt.anchorMax = new Vector2(1f, 1f);
                page.Rt.pivot = new Vector2(0f, 1f);
                page.Rt.anchoredPosition = Vector2.zero;
                page.Rt.sizeDelta = new Vector2(0f, total + Pad);

                var pageMap = Measure(page.Panel, page.Rt);
                int top = Pad;
                for (int i = 0; i < page.Sections.Count; i++)
                {
                    int h = heights[i];
                    var panel = ui.createContentPanel(page.Panel, pageMap.X(SideMargin, cardW), pageMap.Y(top, h), cardW, h);
                    var rt = ((GameObject)panel.getPanelGameObject()).GetComponent<RectTransform>();
                    bool titled = !string.IsNullOrEmpty(page.Sections[i].Key);
                    var card = new Card { Panel = panel, W = cardW - Pad * 2, Y = titled ? HeaderH : 8 };
                    card.Map = Measure(panel, rt);
                    if (titled) CardLabel(card, page.Sections[i].Key, Pad, 6, card.W);
                    page.Sections[i].Value(card);
                    top += h + CardGap;
                }
            }
        }

        private void BuildTabBar(IVNyanUIContentPanel windowPanel)
        {
            var ui = VNyanInterface.VNyanInterface.VNyanUI;
            var map = Measure(windowPanel, _windowRt);
            // Leave room at the right for the window's close button.
            int avail = WindowW - SideMargin - 40;
            int gap = 4;
            int w = (avail - gap * (_pages.Count - 1)) / _pages.Count;
            for (int i = 0; i < _pages.Count; i++)
            {
                int index = i;
                var page = _pages[i];
                page.Tab = ui.createButton(windowPanel, page.Name, map.X(SideMargin + i * (w + gap), w), map.Y(8, 28), w, 28);
                page.Tab.onButtonClicked += () => SelectPage(index);
                RegisterButton(page.Tab, windowPanel, 28);
            }
        }

        private void SelectPage(int index)
        {
            _activePage = Mathf.Clamp(index, 0, _pages.Count - 1);
            for (int i = 0; i < _pages.Count; i++)
            {
                var p = _pages[i];
                bool active = i == _activePage;
                p.Rt.gameObject.SetActive(active);
                SetButtonActive(p.Tab, active);
            }
            _scroll.content = _pages[_activePage].Rt;
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f;
            FitWindowHeight();
        }

        private float _maxViewportH;
        private int _lastWindowH;

        // Shrinks the window to the active tab when it fits; otherwise uses the full height and scrolls.
        private void FitWindowHeight()
        {
            try
            {
                if (_window == null || _viewportRt == null) return;
                if (_maxViewportH <= 0f) _maxViewportH = _viewportRt.rect.height;
                if (_maxViewportH <= 0f) return;
                float content = _pages[_activePage].Rt.sizeDelta.y;
                float desired = Mathf.Min(content, _maxViewportH);
                int h = Mathf.RoundToInt(WindowH - (_maxViewportH - desired));
                if (h == _lastWindowH) return;
                _lastWindowH = h;

                var root = ((GameObject)_window.getWindowGameObject()).GetComponent<RectTransform>();
                var corners = new Vector3[4];
                root.GetWorldCorners(corners);
                float topBefore = corners[1].y;
                _window.setSize(WindowW, h);
                Canvas.ForceUpdateCanvases();
                root.GetWorldCorners(corners);
                float drift = topBefore - corners[1].y;
                if (Mathf.Abs(drift) > 0.01f) root.position += new Vector3(0f, drift, 0f);
            }
            catch (Exception ex) { Debug.LogWarning("[VNyanAdaptiveLighting] Could not resize window: " + ex.Message); }
        }

        // ---- controls --------------------------------------------------------------------------------

        private static IVNyanUILabel CardLabel(Card c, string text, int left, int top, int w, int h = RowH)
        {
            if (c.Dry) return null;
            return VNyanInterface.VNyanInterface.VNyanUI.createLabel(c.Panel, text, c.Map.X(left, w), c.Map.Y(top, h), w, h);
        }

        private sealed class ButtonStyle
        {
            public Image Image;
            public Color Base;
        }

        private readonly Dictionary<IVNyanUIButton, ButtonStyle> _buttonStyles = new Dictionary<IVNyanUIButton, ButtonStyle>();

        private static Transform LastChild(IVNyanUIContentPanel panel)
        {
            var t = ((GameObject)panel.getPanelGameObject()).transform;
            return t.childCount > 0 ? t.GetChild(t.childCount - 1) : null;
        }

        // The stock button sprite is stretched as a whole, which squashes its rounded ends on wide buttons.
        // Slicing it with capsule-sized borders keeps the ends round.
        private static void FixRounding(Image img, float height)
        {
            var sprite = img.sprite;
            if (sprite == null) return;
            if (sprite.border == Vector4.zero)
            {
                float r = sprite.rect.height * 0.5f;
                var border = new Vector4(r, r - 1f, r, r - 1f);
                sprite = Sprite.Create(sprite.texture, sprite.rect, new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
                img.sprite = sprite;
            }
            img.type = Image.Type.Sliced;
            float borderPx = Mathf.Max(sprite.border.x, 1f);
            img.pixelsPerUnitMultiplier = Mathf.Max(0.01f, borderPx * 100f / (sprite.pixelsPerUnit * (height * 0.5f)));
        }

        private void RegisterButton(IVNyanUIButton b, IVNyanUIContentPanel panel, float height)
        {
            var img = LastChild(panel)?.GetComponent<Image>();
            if (img == null) return;
            FixRounding(img, height);
            _buttonStyles[b] = new ButtonStyle { Image = img, Base = img.color };
        }

        private void SetButtonActive(IVNyanUIButton b, bool active)
        {
            if (b == null || !_buttonStyles.TryGetValue(b, out var st)) return;
            var c = st.Base;
            st.Image.color = active ? c : new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, c.a);
        }

        private IVNyanUIButton CardButton(Card c, string text, int left, int top, int w, Action onClick, int h = 28)
        {
            if (c.Dry) return null;
            var b = VNyanInterface.VNyanInterface.VNyanUI.createButton(c.Panel, text, c.Map.X(left, w), c.Map.Y(top, h), w, h);
            b.onButtonClicked += onClick;
            RegisterButton(b, c.Panel, h);
            return b;
        }

        private void CardToggle(Card c, string text, Func<bool> get, Action<bool> set)
        {
            int top = c.Y;
            c.Y += RowH + 6;
            if (c.Dry) return;
            var t = VNyanInterface.VNyanInterface.VNyanUI.createToggle(c.Panel, text, c.Map.X(Pad, c.W), c.Map.Y(top, RowH), c.W, RowH, get());
            t.onToggleValueChanged += () =>
            {
                if (_syncing) return;
                set(t.getIsOn());
                _mgr.NotifyChanged(Time.unscaledTime);
            };
            _refreshers.Add(() => t.setIsOn(get()));
        }

        private void CardSlider(Card c, string name, float min, float max, Func<float> get, Action<float> set, string fmt = "0.00")
        {
            int top = c.Y;
            c.Y += SliderRowH;
            if (c.Dry) return;
            var label = CardLabel(c, name, Pad, top, c.W);
            var slider = VNyanInterface.VNyanInterface.VNyanUI.createSlider(c.Panel, c.Map.X(Pad, c.W), c.Map.Y(top + RowH, 18), c.W, 18, min, max, get(), false);
            Action updateText = () => { };
            slider.onSliderValueChanged += () =>
            {
                if (_syncing) return;
                set(slider.getValue());
                _mgr.NotifyChanged(Time.unscaledTime);
                updateText();
            };
            _refreshers.Add(() => { slider.setValue(get()); updateText(); });
        }

        // ---- sections --------------------------------------------------------------------------------

        private void BuildLighting(Card c)
        {
            CardToggle(c, "Enable Adaptive Lighting", () => S.PluginEnabled, v => S.PluginEnabled = v);
            CardLabel(c, "Mode", Pad, c.Y, c.W); c.Y += RowH + 2;
            int half = (c.W - 10) / 2;
            _monoButton = CardButton(c, "Monochromatic", Pad, c.Y, half, () => SetMode(LightingMode.Monochromatic));
            _colorButton = CardButton(c, "Colored", Pad + half + 10, c.Y, half, () => SetMode(LightingMode.Colored));
            if (!c.Dry) _refreshers.Add(UpdateModeButtons);
            c.Y += 38;
            CardSlider(c, "Minimum", 0f, 1f, () => S.MinBrightness, v => S.MinBrightness = v);
            CardSlider(c, "Normal", 0f, 1f, () => S.NeutralBrightness, v => S.NeutralBrightness = v);
            CardSlider(c, "Maximum", 0f, 1f, () => S.MaxBrightness, v => S.MaxBrightness = v);
            CardSlider(c, "Smoothness", 0f, 1f, () => S.DarkeningTime / SmoothnessSeconds, v => { S.DarkeningTime = v * SmoothnessSeconds; S.BrighteningTime = v * SmoothnessSeconds; });

            var panelTf = c.Dry ? null : ((GameObject)c.Panel.getPanelGameObject()).transform;
            int before = panelTf != null ? panelTf.childCount : 0;
            CardSlider(c, "Color influence", 0f, 1f, () => S.ColorInfluence, v => S.ColorInfluence = v);
            if (panelTf != null)
            {
                _colorSliderObjs.Clear();
                for (int i = before; i < panelTf.childCount; i++) _colorSliderObjs.Add(panelTf.GetChild(i).gameObject);
                _lightingCardRt = panelTf as RectTransform;
                _refreshers.Add(UpdateColorSlider);
            }
        }

        private const float SmoothnessSeconds = 2f;
        private readonly List<GameObject> _colorSliderObjs = new List<GameObject>();
        private RectTransform _lightingCardRt;
        private float _cardBaseH, _pageBaseH;
        private bool _colorSliderShown = true;

        // The colour slider only exists in Colored mode, so the card and tab shrink when it is hidden.
        private void UpdateColorSlider()
        {
            if (_lightingCardRt == null || _pages.Count == 0) return;
            bool show = S.Mode == LightingMode.Colored;
            var pageRt = _pages[0].Rt;
            if (_cardBaseH <= 0f) { _cardBaseH = _lightingCardRt.sizeDelta.y; _pageBaseH = pageRt.sizeDelta.y; }
            if (show == _colorSliderShown && _cardBaseH > 0f && Mathf.Approximately(pageRt.sizeDelta.y, _pageBaseH - (show ? 0f : SliderRowH))) return;
            _colorSliderShown = show;
            foreach (var go in _colorSliderObjs) if (go != null) go.SetActive(show);

            float cut = show ? 0f : SliderRowH;
            var corners = new Vector3[4];
            _lightingCardRt.GetWorldCorners(corners);
            float top = corners[1].y;
            _lightingCardRt.sizeDelta = new Vector2(_lightingCardRt.sizeDelta.x, _cardBaseH - cut);
            _lightingCardRt.GetWorldCorners(corners);
            float drift = top - corners[1].y;
            if (Mathf.Abs(drift) > 0.01f) _lightingCardRt.position += new Vector3(0f, drift, 0f);
            pageRt.sizeDelta = new Vector2(pageRt.sizeDelta.x, _pageBaseH - cut);
            if (_activePage == 0) FitWindowHeight();
        }

        private void SetMode(LightingMode m)
        {
            S.Mode = m;
            _mgr.NotifyChanged(Time.unscaledTime);
            UpdateModeButtons();
        }

        private void UpdateModeButtons()
        {
            SetButtonActive(_monoButton, S.Mode == LightingMode.Monochromatic);
            SetButtonActive(_colorButton, S.Mode == LightingMode.Colored);
            UpdateColorSlider();
        }
        private IVNyanUIDropdown _senderDropdown;
        private List<string> _ddOptions = new List<string>();
        private string _ddKey;
        private bool _ddUpdating;

        private void RefreshSenders(bool force)
        {
            if (_senderDropdown == null) return;
            var opts = new List<string>(_host.Feed.KnownSenders);
            string cur = S.SpoutSenderName;
            if (!string.IsNullOrEmpty(cur) && !opts.Contains(cur)) opts.Insert(0, cur);
            string key = string.Join("|", opts) + "#" + cur;
            if (!force && key == _ddKey) return;
            _ddKey = key;
            _ddOptions = opts;
            _ddUpdating = true;
            try
            {
                _senderDropdown.setOptions(opts);
                _senderDropdown.setSelectedIndex(Math.Max(0, opts.IndexOf(cur)));
            }
            finally { _ddUpdating = false; }
        }

        private void BuildSource(Card c)
        {
            _statusLine = CardLabel(c, "Checking", Pad, c.Y, c.W); c.Y += RowH + 4;
            if (!c.Dry)
            {
                _statusGraphic = LastChild(c.Panel)?.GetComponentInChildren<Graphic>();
                if (_statusGraphic != null) _statusBase = _statusGraphic.color;
            }
            CardLabel(c, "Sender", Pad, c.Y, c.W); c.Y += RowH;
            if (!c.Dry)
            {
                _senderDropdown = VNyanInterface.VNyanInterface.VNyanUI.createDropdown(c.Panel, c.Map.X(Pad, c.W), c.Map.Y(c.Y, 28), c.W, 28, new List<string> { S.SpoutSenderName }, 0);
                _senderDropdown.onDropdownValueChanged += () =>
                {
                    if (_ddUpdating) return;
                    int i = _senderDropdown.getSelectedIndex();
                    if (i < 0 || i >= _ddOptions.Count || _ddOptions[i] == S.SpoutSenderName) return;
                    S.SpoutSenderName = _ddOptions[i];
                    _mgr.NotifyChanged(Time.unscaledTime);
                };
                _refreshers.Add(() => RefreshSenders(true));
            }
            c.Y += 34;
            CardSlider(c, "Sampling frequency (Hz)", 2f, 30f, () => S.SampleRateHz, v => S.SampleRateHz = Mathf.Round(v), "0");
        }

        private void BuildStats(Card c)
        {
            _lumLabel = CardLabel(c, "Screen luminance: Inactive", Pad, c.Y, c.W); c.Y += RowH;
            _brightLabel = CardLabel(c, "Brightness: Inactive", Pad, c.Y, c.W); c.Y += RowH;
            _rateLabel = CardLabel(c, "Sampling: Inactive", Pad, c.Y, c.W); c.Y += RowH;
            
            _perfLabel = CardLabel(c, "Processing: Inactive", Pad, c.Y, c.W); c.Y += RowH;
        }

        private void BuildTest(Card c)
        {
            int half = (c.W - 10) / 2;
            int right = Pad + half + 10;
            _testButtons[(int)TestMode.Darkness] = CardButton(c, "Darkness", Pad, c.Y, half, () => SetTest(TestMode.Darkness));
            _testButtons[(int)TestMode.Normal] = CardButton(c, "Normal", right, c.Y, half, () => SetTest(TestMode.Normal)); c.Y += 34;
            _testButtons[(int)TestMode.Maximum] = CardButton(c, "Maximum", Pad, c.Y, half, () => SetTest(TestMode.Maximum));
            _testButtons[(int)TestMode.Colored] = CardButton(c, "Colored", right, c.Y, half, () => SetTest(TestMode.Colored)); c.Y += 34;
            _testButtons[(int)TestMode.Live] = CardButton(c, "Reset", Pad, c.Y, c.W, () => SetTest(TestMode.Live)); c.Y += 34;
            if (!c.Dry) _refreshers.Add(UpdateTestButtons);
        }

        private void BuildSettings(Card c)
        {
            CardButton(c, "Reset to defaults", Pad, c.Y, c.W, () => { _mgr.ResetToDefaults(Time.unscaledTime); RefreshFromSettings(); }); c.Y += 34;

        }

        private readonly IVNyanUIButton[] _testButtons = new IVNyanUIButton[5];

        private void SetTest(TestMode m)
        {
            _host.SetTestMode(m);
            UpdateTestButtons();
        }

        private void UpdateTestButtons()
        {
            for (int i = 0; i < _testButtons.Length; i++) SetButtonActive(_testButtons[i], i == (int)_host.Test);
        }

        // ---- refresh / diagnostics --------------------------------------------------------------------

        private void OnExternalSettingsChanged()
        {
            if (_syncing || !_visible) return;
            RefreshFromSettings();
        }

        public void RefreshFromSettings()
        {
            if (_window == null) return;
            _syncing = true;
            try { foreach (var r in _refreshers) r(); }
            finally { _syncing = false; }
        }

        /// <summary>Called every frame by the host; cheap unless the window is visible.</summary>
        public void Tick(float now)
        {
            if (!_visible || _window == null || _statusLine == null || now < _nextDiag) return;
            _nextDiag = now + DiagInterval;

            var f = _host.Feed;
            _statusLine.setText(f.Status);
            if (_statusGraphic != null)
            {
                string st = f.Status ?? "";
                bool bad = st.StartsWith("Inactive") || st.StartsWith("Error") || st.Contains("unavailable");
                _statusGraphic.color = bad ? ErrorColor : _statusBase;
            }

            // Only the visible tab is updated.
            if (_activePage == 1) RefreshSenders(false);
            else if (_activePage == 2)
            {
                bool live = _host.Test == TestMode.Live && f.Connected;
                _lumLabel.setText("Screen luminance: " + (live ? (_host.ScreenLuminance * 100f).ToString("0.0") + "%" : "Inactive"));
                _brightLabel.setText("Brightness: target " + _host.TargetBrightness.ToString("0.00") + "  current " + _host.CurrentBrightness.ToString("0.00"));
                _rateLabel.setText("Sampling: " + (live ? _host.Perf.MeasuredSampleRate.ToString("0.0") + " Hz (target " + S.SampleRateHz.ToString("0") + ")" : "Inactive"));
                _perfLabel.setText("Frame " + _host.Perf.Frame.AverageMs.ToString("0.000") + " ms | sample " +
                                   _host.Perf.Sample.AverageMs.ToString("0.000") + " ms");

            }
        }
    }
}
