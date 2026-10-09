using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Metro.UI
{
    public enum ToastKind { Info, Error, Good, Achievement }

    /// <summary>
    /// Owns the UI Toolkit document, fonts and layers, plus shared overlays
    /// (tooltip, toasts, cursor hint) used by every screen.
    /// </summary>
    public sealed class UIRoot
    {
        public static UIRoot I { get; private set; }
        public static StyleFontDefinition FontRegular, FontMedium, FontSemiBold, FontBold, FontDisplay;

        public readonly UIDocument doc;
        public readonly PanelSettings settings;
        public VisualElement Root => doc.rootVisualElement;
        public readonly VisualElement labelsLayer, hudLayer, windowLayer, modalLayer, overlayLayer;

        readonly VisualElement tooltip, tipHead, tipBars;
        readonly Label tipTitle, tipKey, tipBody, tipFoot;
        readonly List<(VisualElement row, Label label, VisualElement fill, Label value)> tipRows = new List<(VisualElement, Label, VisualElement, Label)>();
        VisualElement tooltipOwner;
        static readonly Regex KeySuffix = new Regex(@"\s*\(([^()\s]{1,10}|[A-Z][a-z]+ [A-Za-z0-9]+)\)\s*$");

        readonly VisualElement cursorHint, hintHead, hintDot, hintRows, hintTotal, hintKeys;
        readonly ReceiptPaper hintPaper;
        readonly IconElement hintIcon;
        readonly Label hintTitle, hintBody, hintHelp, hintTotalLabel, hintTotalValue;
        readonly List<(VisualElement row, Label label, Label value)> hintRowPool = new List<(VisualElement, Label, Label)>();
        readonly List<(VisualElement pair, Label key, Label action)> hintKeyPool = new List<(VisualElement, Label, Label)>();
        readonly HintReceipt receipt = new HintReceipt();
        int hintMode = -1;
        readonly VisualElement toastHost;
        readonly List<(VisualElement el, float until, string text)> toasts = new List<(VisualElement, float, string)>();
        float time;

        public UIRoot(Transform parent)
        {
            I = this;
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "MetroPanelSettings";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/MetroTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 10;

            var go = new GameObject("UI");
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            doc = go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            go.SetActive(true);

            LoadFonts();
            var sheet = Resources.Load<StyleSheet>("UI/Metro");
            if (sheet != null) Root.styleSheets.Add(sheet);
            Root.AddToClassList("root");
            Root.style.unityFontDefinition = FontRegular;
            Root.pickingMode = PickingMode.Ignore;

            labelsLayer = Layer("labels");
            hudLayer = Layer("hud");
            windowLayer = Layer("windows");
            modalLayer = Layer("modals");
            overlayLayer = Layer("overlay");

            toastHost = UIK.Div(overlayLayer, "toast-host");
            toastHost.pickingMode = PickingMode.Ignore;

            // The cursor hint is a paper receipt while building and an ink bubble while hovering the map.
            cursorHint = UIK.Div(overlayLayer, "cursor-hint");
            hintPaper = new ReceiptPaper();
            cursorHint.Add(hintPaper);
            hintHead = UIK.Row(cursorHint, "hint-head");
            hintDot = UIK.Div(hintHead, "hint-dot");
            hintIcon = new IconElement(IconKind.Warning, 22f);
            hintIcon.style.marginRight = 7f;
            hintHead.Add(hintIcon);
            hintTitle = UIK.Text(hintHead, "", "cursor-hint-title");
            hintBody = UIK.Text(cursorHint, "", "hint-body");
            hintRows = UIK.Col(cursorHint, "hint-rows");
            hintTotal = UIK.Row(cursorHint, "hint-total");
            hintTotalLabel = UIK.Text(hintTotal, "", "hint-total-label");
            UIK.Spacer(hintTotal);
            hintTotalValue = UIK.Text(hintTotal, "", "hint-total-value");
            UIK.Display(hintTotalValue);
            hintHelp = UIK.Text(cursorHint, "", "hint-help");
            hintKeys = UIK.Row(cursorHint, "hint-keys");
            NoPick(cursorHint);
            UIK.Show(cursorHint, false);

            tooltip = UIK.Div(overlayLayer, "panel-dark", "tooltip");
            tipHead = UIK.Row(tooltip, "tip-head");
            tipTitle = UIK.Text(tipHead, "", "tip-title");
            tipKey = UIK.Text(tipHead, "", "tip-key");
            tipBody = UIK.Text(tooltip, "", "tip-body");
            tipBars = UIK.Col(tooltip, "tip-bars");
            tipFoot = UIK.Text(tooltip, "", "tip-foot");
            tipTitle.style.whiteSpace = WhiteSpace.Normal;
            tipBody.style.whiteSpace = WhiteSpace.Normal;
            tipFoot.style.whiteSpace = WhiteSpace.Normal;
            NoPick(tooltip);
            UIK.Show(tooltip, false);
        }

        static void NoPick(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var c in e.Children()) NoPick(c);
        }

        VisualElement Layer(string name)
        {
            var e = UIK.Div(Root, "layer");
            e.name = name;
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        /// <summary>
        /// Doodle Metro type: Shantell Sans (hand-lettered) for all text, Luckiest Guy for headlines,
        /// numbers and stickers, and Inter behind both for any glyph the hand-drawn faces lack (arrows...).
        /// </summary>
        static void LoadFonts()
        {
            if (FontRegular.keyword == StyleKeyword.Undefined && FontRegular.value.fontAsset != null) return;
            var inter = MakeFont("Fonts/Inter-SemiBold");
            var interBold = MakeFont("Fonts/Inter-Bold") ?? inter;
            var text = MakeFont("Fonts/ShantellSans-SemiBold") ?? inter;
            var heavy = MakeFont("Fonts/ShantellSans-ExtraBold") ?? interBold ?? text;
            var display = MakeFont("Fonts/LuckiestGuy-Regular") ?? heavy;
            AddFallback(text, inter);
            AddFallback(heavy, interBold);
            AddFallback(display, heavy);
            AddFallback(display, interBold);
            // Map USS "bold" onto the ExtraBold face so -unity-font-style: bold uses a real typeface.
            TryLinkBold(text, heavy);
            FontRegular = Def(text);
            FontMedium = FontRegular;
            FontSemiBold = Def(heavy);
            FontBold = Def(heavy);
            FontDisplay = Def(display);
        }

        static void AddFallback(FontAsset fa, FontAsset fallback)
        {
            if (fa == null || fallback == null || fa == fallback) return;
            if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new List<FontAsset>();
            if (!fa.fallbackFontAssetTable.Contains(fallback)) fa.fallbackFontAssetTable.Add(fallback);
        }

        static FontAsset MakeFont(string path)
        {
            var f = Resources.Load<Font>(path);
            if (f == null) return null;
            try
            {
                var fa = FontAsset.CreateFontAsset(f);
                if (fa != null) fa.name = f.name + " SDF";
                return fa;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Metro] Could not create font asset for " + path + ": " + e.Message);
                return null;
            }
        }

        static void TryLinkBold(FontAsset regular, FontAsset bold)
        {
            if (regular == null || bold == null) return;
            var table = regular.fontWeightTable;
            if (table != null && table.Length > 7) table[7].regularTypeface = bold;
        }

        static StyleFontDefinition Def(FontAsset fa) =>
            fa != null ? new StyleFontDefinition(FontDefinition.FromSDFFont(fa)) : new StyleFontDefinition(StyleKeyword.Null);

        // ------------------------------------------------------------------ coordinates & picking

        public Vector2 ScreenToPanel(Vector2 screen)
        {
            if (Root.panel == null) return screen;
            return RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screen.x, Screen.height - screen.y));
        }

        public Vector2 WorldToPanel(Camera cam, Vector2 world)
        {
            if (Root.panel == null) return Vector2.zero;
            return RuntimePanelUtils.CameraTransformWorldToPanel(Root.panel, new Vector3(world.x, world.y, 0f), cam);
        }

        public float PanelScale
        {
            get
            {
                float w = Root.layout.width;
                if (float.IsNaN(w) || w <= 1f) return 1f;
                return w / Mathf.Max(1, Screen.width);
            }
        }

        public bool IsPointerOverUI(Vector2 screen)
        {
            if (Root.panel == null) return false;
            var picked = Root.panel.Pick(ScreenToPanel(screen));
            return picked != null;
        }

        // ------------------------------------------------------------------ tooltip

        /// <summary>
        /// An ink tooltip. "Title (K)\nBody" becomes a bold title with a key-cap chip over the body;
        /// a short phrase is a title on its own, and a plain sentence is just body text.
        /// </summary>
        public void ShowTooltip(string text, VisualElement owner)
        {
            if (string.IsNullOrEmpty(text)) return;
            string title = null, body = text;
            int nl = text.IndexOf('\n');
            if (nl > 0)
            {
                title = text.Substring(0, nl);
                body = text.Substring(nl + 1);
            }
            else if (text.Length <= 44 && !text.EndsWith(".") && !text.EndsWith("!") && !text.EndsWith("?"))
            {
                title = text;
                body = null;
            }
            string key = null;
            if (title != null)
            {
                var m = KeySuffix.Match(title);
                if (m.Success && m.Index > 0)
                {
                    key = m.Groups[1].Value;
                    title = title.Substring(0, m.Index);
                }
            }
            FillTooltip(title, key, body, null, null);
            PresentTooltip(owner);
        }

        /// <summary>A tooltip with a title, a few labelled 0..1 bars (coloured by mood) and a footnote.</summary>
        public void ShowTooltipBars(string title, (string label, float value)[] rows, string foot, VisualElement owner)
        {
            if (string.IsNullOrEmpty(title) && (rows == null || rows.Length == 0)) return;
            FillTooltip(title, null, null, rows, foot);
            PresentTooltip(owner);
        }

        void FillTooltip(string title, string key, string body, (string label, float value)[] rows, string foot)
        {
            tipTitle.text = title ?? "";
            tipKey.text = key ?? "";
            UIK.Show(tipHead, !string.IsNullOrEmpty(title));
            UIK.Show(tipKey, !string.IsNullOrEmpty(key));
            tipBody.text = body ?? "";
            UIK.Show(tipBody, !string.IsNullOrEmpty(body));
            int n = rows?.Length ?? 0;
            while (tipRows.Count < n)
            {
                var row = UIK.Row(tipBars, "tip-bar-row");
                var label = UIK.Text(row, "", "tip-bar-label");
                var track = UIK.Div(row, "tip-bar-track");
                var fill = UIK.Div(track, "tip-bar-fill");
                var value = UIK.Text(row, "", "tip-bar-value");
                NoPick(row);
                tipRows.Add((row, label, fill, value));
            }
            int worst = -1;
            for (int i = 0; i < n; i++)
                if (worst < 0 || rows[i].value < rows[worst].value) worst = i;
            for (int i = 0; i < tipRows.Count; i++)
            {
                var (row, label, fill, value) = tipRows[i];
                UIK.Show(row, i < n);
                if (i >= n) continue;
                float v = Mathf.Clamp01(rows[i].value);
                label.text = rows[i].label;
                value.text = Mathf.RoundToInt(v * 100f) + "%";
                fill.style.width = Length.Percent(Mathf.Max(4f, v * 100f));
                fill.style.backgroundColor = v >= 0.7f ? Doodle.MoodGood : v >= 0.55f ? Doodle.MoodOk : v >= 0.4f ? Doodle.MoodMeh : Doodle.MoodBad;
                row.EnableInClassList("tip-bar-worst", i == worst && v < 0.85f);
            }
            UIK.Show(tipBars, n > 0);
            tipFoot.text = foot ?? "";
            UIK.Show(tipFoot, !string.IsNullOrEmpty(foot));
            tooltip.EnableInClassList("tooltip-wide", n > 0);
        }

        void PresentTooltip(VisualElement owner)
        {
            tooltipOwner = owner;
            UIK.Show(tooltip, true);
            PositionTooltip();
        }

        public void HideTooltip(VisualElement owner)
        {
            if (tooltipOwner != owner) return;
            tooltipOwner = null;
            UIK.Show(tooltip, false);
        }

        void PositionTooltip()
        {
            if (tooltipOwner == null) return;
            if (tooltipOwner.panel == null)
            {
                HideTooltip(tooltipOwner);
                return;
            }
            var wb = tooltipOwner.worldBound;
            float w = tooltip.resolvedStyle.width;
            float h = tooltip.resolvedStyle.height;
            if (float.IsNaN(w)) w = 160f;
            if (float.IsNaN(h)) h = 28f;
            float rw = Root.layout.width, rh = Root.layout.height;
            float x = wb.xMax + 8f, y = wb.center.y - h * 0.5f;
            if (x + w > rw - 8f) x = wb.xMin - w - 8f;
            if (wb.yMin < 70f && wb.width > 120f)
            {
                x = wb.center.x - w * 0.5f;
                y = wb.yMax + 6f;
            }
            x = Mathf.Clamp(x, 6f, Mathf.Max(6f, rw - w - 6f));
            y = Mathf.Clamp(y, 6f, Mathf.Max(6f, rh - h - 6f));
            tooltip.style.left = x;
            tooltip.style.top = y;
        }

        // ------------------------------------------------------------------ cursor hint

        /// <summary>A simple cursor receipt: a title and a line of text. Bad ones turn pink and get a warning sign.</summary>
        public void SetCursorHint(string title, string body, bool bad)
        {
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))
            {
                HideCursorHint();
                return;
            }
            var r = Receipt();
            r.title = title;
            r.note = body;
            if (bad)
            {
                r.paper = Doodle.TomatoL;
                r.icon = IconKind.Warning;
            }
            ShowReceipt();
        }

        /// <summary>Starts a fresh cursor receipt; fill it in, then call <see cref="ShowReceipt"/>.</summary>
        public HintReceipt Receipt()
        {
            receipt.Clear();
            return receipt;
        }

        public void ShowReceipt() => PresentHint(false, null);

        /// <summary>Hovering things on the map: an ink bubble with a colour dot, like the button tooltips.</summary>
        public void SetCursorBubble(string title, string body, Color dot)
        {
            receipt.Clear();
            receipt.title = title;
            receipt.note = body;
            PresentHint(true, dot);
        }

        void PresentHint(bool bubble, Color? dot)
        {
            var r = receipt;
            int mode = bubble ? 1 : 0;
            if (mode != hintMode)
            {
                hintMode = mode;
                cursorHint.EnableInClassList("cursor-bubble", bubble);
                UIK.Show(hintPaper, !bubble);
                hintTitle.style.unityFontDefinition = bubble ? FontBold : FontDisplay;
            }
            if (!bubble) hintPaper.Fill = r.paper;
            UIK.Show(hintDot, dot.HasValue);
            if (dot.HasValue) hintDot.style.backgroundColor = dot.Value;
            bool icon = r.icon.HasValue && !bubble;
            UIK.Show(hintIcon, icon);
            if (icon && hintIcon.Kind != r.icon.Value)
            {
                hintIcon.Kind = r.icon.Value;
                hintIcon.ResetColor();
            }
            hintTitle.text = r.title ?? "";
            UIK.Show(hintHead, !string.IsNullOrEmpty(r.title));
            hintBody.text = r.note ?? "";
            UIK.Show(hintBody, !string.IsNullOrEmpty(r.note));
            hintBody.EnableInClassList("hint-body-bad", r.noteBad);

            while (hintRowPool.Count < r.rows.Count)
            {
                var row = UIK.Row(hintRows, "hint-row");
                var label = UIK.Text(row, "", "hint-row-label");
                row.Add(new DottedLeader());
                var value = UIK.Text(row, "", "hint-row-value");
                UIK.Display(value);
                NoPick(row);
                hintRowPool.Add((row, label, value));
            }
            for (int i = 0; i < hintRowPool.Count; i++)
            {
                bool on = i < r.rows.Count;
                UIK.Show(hintRowPool[i].row, on);
                if (!on) continue;
                hintRowPool[i].label.text = r.rows[i].label;
                hintRowPool[i].value.text = r.rows[i].value;
            }
            UIK.Show(hintRows, r.rows.Count > 0);

            bool total = !string.IsNullOrEmpty(r.totalValue);
            UIK.Show(hintTotal, total);
            if (total)
            {
                hintTotalLabel.text = r.totalLabel ?? "";
                hintTotalValue.text = r.totalValue;
                hintTotalValue.EnableInClassList("bad", r.totalBad);
            }
            hintHelp.text = r.help ?? "";
            UIK.Show(hintHelp, !string.IsNullOrEmpty(r.help));

            while (hintKeyPool.Count < r.keys.Count)
            {
                // Each key chip and what it does wrap together.
                var pair = UIK.Row(hintKeys, "hint-key-pair");
                var k = UIK.Text(pair, "", "hint-key");
                var a = UIK.Text(pair, "", "hint-key-action");
                NoPick(pair);
                hintKeyPool.Add((pair, k, a));
            }
            for (int i = 0; i < hintKeyPool.Count; i++)
            {
                bool on = i < r.keys.Count;
                UIK.Show(hintKeyPool[i].pair, on);
                if (!on) continue;
                hintKeyPool[i].key.text = r.keys[i].key;
                hintKeyPool[i].action.text = r.keys[i].action;
            }
            UIK.Show(hintKeys, r.keys.Count > 0);

            UIK.Show(cursorHint, true);
            Vector2 p = ScreenToPanel(InputX.MousePos);
            float w = cursorHint.resolvedStyle.width, h = cursorHint.resolvedStyle.height;
            if (float.IsNaN(w)) w = 200f;
            if (float.IsNaN(h)) h = 60f;
            float x = p.x + 20f, y = p.y + 20f;
            if (x + w > Root.layout.width - 6f) x = p.x - w - 14f;
            if (y + h > Root.layout.height - 6f) y = p.y - h - 14f;
            cursorHint.style.left = x;
            cursorHint.style.top = y;
        }

        public void HideCursorHint() => UIK.Show(cursorHint, false);

        // ------------------------------------------------------------------ toasts

        public void Toast(string text, ToastKind kind = ToastKind.Info, float seconds = 3f) => Toast(text, kind, seconds, 0f);

        /// <summary>
        /// A toast with a leading icon (errors get a warning sign, achievements a trophy) and an optional
        /// money chip: "Choo choo! Train added to the Red Line [−$144k]". Positive money is cash back.
        /// </summary>
        public void Toast(string text, ToastKind kind, float seconds, float money, IconKind? icon = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            // Avoid stacking duplicates.
            foreach (var t in toasts)
                if (t.text == text) return;
            var toast = UIK.Row(toastHost, "toast");
            toast.pickingMode = PickingMode.Ignore;
            // A few toasts get their own look: a new district wears a big star sticker, saves are small
            // pills, and "unlocks when..." gets a padlock.
            bool district = text.StartsWith("New district: ");
            bool small = text == "Autosaved" || text == "Game saved";
            if (icon == null && text.Contains(" unlocks when ")) icon = IconKind.Lock;
            IconKind? lead = icon ?? (kind == ToastKind.Error ? IconKind.Warning : kind == ToastKind.Achievement ? IconKind.Trophy : (IconKind?)null);
            if (district)
            {
                toast.AddToClassList("toast-district");
                var star = new Starburst(48f, "NEW!");
                star.Fill = Doodle.Sun;
                star.label.AddToClassList("toast-star-text");
                star.style.marginRight = 8f;
                toast.Add(star);
            }
            else if (small)
            {
                toast.AddToClassList("toast-small");
                var dot = UIK.Div(toast, "toast-dot");
                dot.style.backgroundColor = text == "Game saved" ? Doodle.Mint : Doodle.SkyL;
                dot.Add(new IconElement(IconKind.Floppy, 16f));
            }
            else if (lead.HasValue)
            {
                var ic = new IconElement(lead.Value, 26f);
                ic.style.marginRight = 8f;
                toast.Add(ic);
            }
            var label = UIK.Text(toast, text, "toast-text");
            label.pickingMode = PickingMode.Ignore;
            if (Mathf.Abs(money) >= 1f)
            {
                var chip = UIK.Text(toast, Fmt.MoneySigned(money).Replace("-", "−"), "toast-chip");
                chip.pickingMode = PickingMode.Ignore;
                chip.EnableInClassList("toast-chip-plus", money > 0f);
                UIK.Display(chip);
            }
            if (kind == ToastKind.Error) toast.AddToClassList("toast-error");
            if (kind == ToastKind.Good) toast.AddToClassList("toast-good");
            if (kind == ToastKind.Achievement) toast.AddToClassList("toast-achievement");
            toasts.Add((toast, time + seconds, text));
            if (kind == ToastKind.Error) Sfx.Error();
            while (toasts.Count > 4)
            {
                toasts[0].el.RemoveFromHierarchy();
                toasts.RemoveAt(0);
            }
        }

        /// <summary>While a dialog covers the middle of the screen, toasts pop up from the bottom instead of over its title.</summary>
        public void SetToastsLow(bool low) => toastHost.EnableInClassList("toast-host-low", low);

        public void Tick(float dt)
        {
            time += dt;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var (el, until, _) = toasts[i];
                if (time > until + 0.3f)
                {
                    el.RemoveFromHierarchy();
                    toasts.RemoveAt(i);
                }
                else if (time > until)
                {
                    // Float up and fade out.
                    el.style.opacity = 0f;
                    el.style.translate = new Translate(0f, -14f);
                }
            }
            if (tooltipOwner != null) PositionTooltip();
        }

        public void ClearTransient()
        {
            HideCursorHint();
            tooltipOwner = null;
            UIK.Show(tooltip, false);
        }

        public float UiScale
        {
            get => settings.scale;
            set => settings.scale = Mathf.Clamp(value, 0.7f, 1.5f);
        }
    }

    /// <summary>
    /// What a cursor receipt says while building: a title (with an optional icon), itemised costs with
    /// dotted leaders, a total, a note, a muted helper line and key chips ("right-click" build...).
    /// Get one from <see cref="UIRoot.Receipt"/>, fill it in and call <see cref="UIRoot.ShowReceipt"/>.
    /// </summary>
    public sealed class HintReceipt
    {
        public string title;
        public IconKind? icon;
        public Color paper = Doodle.Paper;
        public readonly List<(string label, string value)> rows = new List<(string, string)>();
        public string totalLabel, totalValue;
        public bool totalBad;
        public string note;
        public bool noteBad;
        public string help;
        public readonly List<(string key, string action)> keys = new List<(string, string)>();

        public void Clear()
        {
            title = null;
            icon = null;
            paper = Doodle.Paper;
            rows.Clear();
            totalLabel = totalValue = null;
            totalBad = false;
            note = null;
            noteBad = false;
            help = null;
            keys.Clear();
        }

        public HintReceipt Row(string label, string value)
        {
            rows.Add((label, value));
            return this;
        }

        public HintReceipt Key(string key, string action)
        {
            keys.Add((key, action));
            return this;
        }
    }
}
