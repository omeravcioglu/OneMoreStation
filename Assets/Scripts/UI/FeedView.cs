using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Metro.UI
{
    /// <summary>
    /// The manager's feed: speech bubbles from operations, riders, City Hall, finance and safety, newest on
    /// top. Warnings wear a "!" sticker, critical ones a "!!" and a little shake, good news some confetti.
    /// Click a message (or its Show on map button) to jump to the problem. Tucked away, the feed is a pill
    /// with whoever spoke last peeking out of it.
    /// </summary>
    public sealed class FeedView
    {
        readonly GameRoot g;
        public readonly VisualElement root;
        readonly ScrollView list;
        readonly Label title, badge, quiet, empty;
        readonly VisualElement peek, readBtn;
        readonly IconElement peekIcon, chevron;
        bool collapsed;
        int shownLastId = -1;
        int shownUnread = -1;
        int shownCount = -1;
        bool hot;
        readonly List<VisualElement> shakers = new List<VisualElement>();

        public FeedView(GameRoot g, VisualElement parent)
        {
            this.g = g;
            root = UIK.Div(parent, "panel", "feed");
            var head = UIK.Row(root, "feed-header");
            // Collapsed: the last speaker peeks out over the pill.
            peek = UIK.Div(head, "feed-stripe", "feed-peek");
            peekIcon = new IconElement(IconKind.Info, 18f);
            peek.Add(peekIcon);
            peek.pickingMode = PickingMode.Ignore;
            var ic = new IconElement(IconKind.Feed, 26f);
            ic.style.marginRight = 8f;
            head.Add(ic);
            title = UIK.Text(head, "Manager's feed", "feed-title");
            quiet = UIK.Text(head, "quiet… for now", "feed-quiet");
            badge = UIK.Text(head, "0", "feed-badge");
            UIK.Spacer(head);
            readBtn = UIK.IconButton(head, IconKind.Check, () =>
            {
                g.State?.advisor.MarkAllRead();
                shownLastId = -1;
            }, "Mark all as read", "icon-btn", 18f);
            readBtn.AddToClassList("icon-btn-sm");
            var col = UIK.IconButton(head, IconKind.Chevron, () => SetCollapsed(!collapsed), null, "icon-btn", 18f);
            col.AddToClassList("icon-btn-sm");
            col.style.marginLeft = 6f;
            UIK.Tooltip(col, () => collapsed ? "Show the feed" : "Tuck the feed away");
            chevron = UIK.IconOf(col);
            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("feed-list");
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            root.Add(list);
            empty = UIK.Text(root, "quiet… for now. The manager pipes up when something needs you.", "feed-empty");
            SetCollapsed(false);
        }

        void SetCollapsed(bool on)
        {
            collapsed = on;
            root.EnableInClassList("feed-collapsed", on);
            UIK.Show(list, !on);
            UIK.Show(readBtn, !on);
            chevron.style.rotate = new Rotate(on ? 180f : 0f);
            shownCount = -1;
        }

        public static Color CategoryColor(MsgCategory c)
        {
            switch (c)
            {
                case MsgCategory.Operations: return Doodle.Sky;
                case MsgCategory.Riders: return Doodle.Gum;
                case MsgCategory.City: return Doodle.Mint;
                case MsgCategory.Finance: return Doodle.Sun;
                case MsgCategory.Safety: return Doodle.Tomato;
            }
            return Doodle.Sand;
        }

        static IconKind CategoryIcon(MsgCategory c)
        {
            switch (c)
            {
                case MsgCategory.Operations: return IconKind.Train;
                case MsgCategory.Riders: return IconKind.Smile;
                case MsgCategory.City: return IconKind.City;
                case MsgCategory.Finance: return IconKind.Coin;
                case MsgCategory.Safety: return IconKind.Warning;
            }
            return IconKind.Info;
        }

        public static Color SeverityColor(MsgSeverity s)
        {
            switch (s)
            {
                case MsgSeverity.Good: return Doodle.MintL;
                case MsgSeverity.Warning: return Doodle.SunL;
                case MsgSeverity.Critical: return Doodle.TomatoL;
            }
            return Doodle.Paper;
        }

        /// <summary>"CITY HALL" → "City Hall".</summary>
        static string TitleCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var chars = s.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (i == 0 || chars[i - 1] == ' ') chars[i] = char.ToUpperInvariant(chars[i]);
            return new string(chars);
        }

        public void Refresh()
        {
            var gs = g.State;
            if (gs == null) return;
            var msgs = gs.advisor.messages;
            int lastId = msgs.Count > 0 ? msgs[msgs.Count - 1].id : -1;
            if (lastId != shownLastId || msgs.Count != shownCount)
            {
                shownLastId = lastId;
                shownCount = msgs.Count;
                list.Clear();
                shakers.Clear();
                int shown = 0;
                for (int i = msgs.Count - 1; i >= 0 && shown < 40; i--, shown++) list.Add(Item(msgs[i]));
                if (msgs.Count > 0)
                {
                    var last = msgs[msgs.Count - 1];
                    peek.style.backgroundColor = CategoryColor(last.category);
                    peekIcon.Kind = CategoryIcon(last.category);
                }
                bool none = msgs.Count == 0;
                UIK.Show(empty, none && !collapsed);
                // Tucked away with nothing to say: a cream pill that just says so.
                root.EnableInClassList("feed-quiet-pill", none && collapsed);
                UIK.Show(quiet, none && collapsed);
                UIK.Show(title, !(none && collapsed));
                UIK.Show(peek, collapsed && !none);
                shownUnread = -1;
            }
            if (gs.advisor.unread != shownUnread)
            {
                shownUnread = gs.advisor.unread;
                badge.text = shownUnread > 99 ? "99+" : shownUnread.ToString();
                UIK.Show(badge, shownUnread > 0);
                hot = false;
                for (int i = msgs.Count - 1; i >= 0 && i >= msgs.Count - 40; i--)
                {
                    if (!msgs[i].read && msgs[i].severity == MsgSeverity.Critical)
                    {
                        hot = true;
                        break;
                    }
                }
                badge.EnableInClassList("feed-badge-hot", hot);
                if (!hot) badge.style.rotate = StyleKeyword.Null;
            }
            float t = Time.unscaledTime;
            // An unread critical message makes the count wiggle.
            if (hot && shownUnread > 0) badge.style.rotate = new Rotate(Mathf.Sin(t * 9f) * 9f - 6f);
            // Critical bubbles give a little shake every couple of seconds.
            float phase = t % 2.4f / 2.4f;
            float shake = 0f;
            if (phase > 0.84f)
            {
                float k = (phase - 0.84f) / 0.16f;
                shake = Mathf.Sin(k * Mathf.PI * 3f) * 1.4f * (1f - k * 0.45f);
            }
            foreach (var b in shakers) b.style.rotate = new Rotate(shake);
        }

        VisualElement Item(AdvisorMessage m)
        {
            var row = UIK.Div(null, "feed-item");
            if (!m.read) row.AddToClassList("feed-unread");
            else row.AddToClassList("feed-read");
            // Who's talking: a little round avatar in the category's colour...
            var avatar = UIK.Div(row, "feed-stripe");
            avatar.style.backgroundColor = CategoryColor(m.category);
            avatar.Add(new IconElement(CategoryIcon(m.category), 20f));
            // ...and what they say, in a speech bubble tinted by how urgent it is.
            var col = UIK.Col(row, "feed-bubble");
            col.style.backgroundColor = SeverityColor(m.severity);
            var meta = UIK.Div(col, "feed-meta");
            // Rider rumours name the speaker and quote them in big letters.
            string speaker = TitleCase(m.CategoryLabel), said = m.text;
            bool quote = false;
            if (m.category == MsgCategory.Riders && m.text.StartsWith("A rider at "))
            {
                int colon = m.text.IndexOf(": ");
                if (colon > 0)
                {
                    speaker = m.text.Substring(0, colon);
                    said = "“" + m.text.Substring(colon + 2).Trim().Trim('"') + "”";
                    quote = true;
                }
            }
            UIK.Text(meta, speaker, "feed-cat");
            UIK.Spacer(meta);
            var clock = g.State.clock;
            int day = (int)(m.time / Balance.MinutesPerDay);
            string t = GameClock.FormatMinute((float)(m.time % Balance.MinutesPerDay));
            if (day != clock.Day) t = $"Day {day + 1} · {t}";
            UIK.Text(meta, t, "feed-time");
            var text = UIK.Text(col, said, "feed-text");
            text.style.whiteSpace = WhiteSpace.Normal;
            if (quote) text.AddToClassList("feed-quote");
            if (m.HasTarget && !quote)
            {
                var show = UIK.Row(col, "feed-show");
                show.Add(new IconElement(IconKind.Pin, 15f));
                UIK.Text(show, "Show on map", "feed-show-label");
                show.AddManipulator(new Clickable(() => Open(m, row)));
            }
            if (m.severity == MsgSeverity.Critical)
            {
                col.AddToClassList("feed-critical");
                var alarm = UIK.Text(col, "!!", "feed-alarm");
                alarm.pickingMode = PickingMode.Ignore;
                UIK.Display(alarm);
                shakers.Add(col);
            }
            else if (m.severity == MsgSeverity.Warning)
            {
                col.AddToClassList("feed-critical");
                var alarm = UIK.Text(col, "!", "feed-alarm", "feed-alarm-warn");
                alarm.pickingMode = PickingMode.Ignore;
                UIK.Display(alarm);
            }
            else if (m.severity == MsgSeverity.Good)
            {
                var confetti = new Confetti(34f);
                confetti.AddToClassList("feed-confetti");
                col.Add(confetti);
            }
            row.AddManipulator(new Clickable(() => Open(m, row)));
            return row;
        }

        void Open(AdvisorMessage m, VisualElement row)
        {
            if (!m.read)
            {
                m.read = true;
                g.State.advisor.unread = Mathf.Max(0, g.State.advisor.unread - 1);
                row.RemoveFromClassList("feed-unread");
                row.AddToClassList("feed-read");
            }
            g.FocusMessage(m);
        }
    }
}
