using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the Log tab as wide as the card: what to show as three small toggles by the title and the copy button, then the
// game's own log lines (battles, entries, the one an injury points to highlighted), as tall as they need up to a limit
public static class LogPanel
{
    private static float Scale => Pane.Scale;
    private static float Width => Mathf.Round(400f * Scale);
    private static float Control => Mathf.Round(22f * Scale);
    private static float Section => Mathf.Round(8f * Scale);
    private static float MaxHeight => Mathf.Round(480f * Scale);
    private const int MaxLines = 300;

    private static Vector2 scroll;

    // the game's lines for this pawn, made again when the log or the filters change, kept on its tab as it does
    private static Pawn lastPawn;

    private static void Refresh(ITab_Pawn_Log tab, Pawn p)
    {
        // another pawn selected with the tab open
        if (lastPawn != p)
        {
            lastPawn = p;
            tab.cachedLogDisplay = null;
        }
        if (tab.cachedLogDisplay == null || tab.cachedLogDisplayLastTick != p.records.LastBattleTick || tab.cachedLogPlayLastTick != Find.PlayLog.LastTick)
        {
            tab.cachedLogDisplay = ITab_Pawn_Log_Utility.GenerateLogLinesFor(p, tab.showAll, tab.showCombat, tab.showSocial, MaxLines);
            tab.cachedLogDisplayLastTick = p.records.LastBattleTick;
            tab.cachedLogPlayLastTick = Find.PlayLog.LastTick;
        }
    }

    private static float LineWidth(float width) => width - 16f;

    private static float LinesHeight(ITab_Pawn_Log tab, float width)
    {
        float h = 0f;
        foreach (ITab_Pawn_Log_Utility.LogLineDisplayable line in tab.cachedLogDisplay)
        {
            h += line.GetHeight(LineWidth(width));
        }
        return h;
    }

    public static Vector2 Size(ITab_Pawn_Log tab, Pawn p)
    {
        Refresh(tab, p);
        GameFont font = Text.Font;
        Text.Font = GameFont.Small;
        float inner = Width - Pane.Pad * 2f;
        float lines = Mathf.Max(LinesHeight(tab, inner), 24f * Scale);
        Text.Font = font;
        return new Vector2(Width, Mathf.Min(MaxHeight, Mathf.Round(Pane.Pad * 2f + 24f * Scale + Section + lines)));
    }

    public static void Draw(Rect all, ITab_Pawn_Log tab, Pawn p)
    {
        Rect inner = all.ContractedBy(Pane.Pad);
        Widgets.BeginGroup(inner);
        try
        {
            Rect area = inner.AtZero();
            Text.Font = Pane.NameFont;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, 0f, area.width / 3f, 24f * Scale), "TabLog".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = Pane.SmallFont;
            Toolbar(new Rect(0f, 0f, area.width - 24f, 24f * Scale), tab);
            Refresh(tab, p);
            Text.Font = GameFont.Small;
            Rect list = new Rect(0f, 24f * Scale + Section, area.width, area.height - 24f * Scale - Section);
            float width = LineWidth(area.width);
            float total = 0f;
            foreach (ITab_Pawn_Log_Utility.LogLineDisplayable line in tab.cachedLogDisplay)
            {
                // an injury in Health asked for its entry: scroll so it's in the middle
                if (line.Matches(tab.logSeek))
                {
                    scroll.y = total - (line.GetHeight(width) + list.height) / 2f;
                }
                total += line.GetHeight(width);
            }
            tab.logSeek = null;
            if (total <= 0f)
            {
                GUI.color = ColoredText.SubtleGrayColor;
                Text.Font = Pane.SmallFont;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(5f * Scale, list.y, list.width, 24f * Scale), "NoRecentEntries".Translate().CapitalizeFirst());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }
            Rect view = new Rect(0f, 0f, list.width - 16f, total);
            Rect visible = new Rect(0f, scroll.y, view.width, list.height);
            tab.data.StartNewDraw();
            Widgets.BeginScrollView(list, ref scroll, view);
            float y = 0f;
            foreach (ITab_Pawn_Log_Utility.LogLineDisplayable line in tab.cachedLogDisplay)
            {
                float h = line.GetHeight(width);
                if (visible.Overlaps(new Rect(0f, y, width, h)))
                {
                    line.Draw(y, width, tab.data);
                }
                else
                {
                    tab.data.alternatingBackground = !tab.data.alternatingBackground;
                }
                y += h;
            }
            Widgets.EndScrollView();
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
            Widgets.EndGroup();
        }
    }

    // combat, social and all as toggles, then copy, right to left at the end of the title row
    private static void Toolbar(Rect row, ITab_Pawn_Log tab)
    {
        float right = row.xMax;
        float y = Mathf.Round(row.center.y - Control / 2f);
        Rect copy = new Rect(right - Control, y, Control, Control);
        if (Widgets.ButtonImage(copy.ContractedBy(3f * Scale), TexButton.Copy, Style.Dim, Color.white))
        {
            StringBuilder sb = new StringBuilder();
            foreach (ITab_Pawn_Log_Utility.LogLineDisplayable line in tab.cachedLogDisplay)
            {
                line.AppendTo(sb);
            }
            GUIUtility.systemCopyBuffer = sb.ToString();
        }
        TooltipHandler.TipRegionByKey(copy, "CopyLogTip");
        right = copy.x - 6f * Scale;
        if (Toggle(ref right, y, "ShowAll".Translate(), ref tab.showAll) | Toggle(ref right, y, "ShowSocial".Translate(), ref tab.showSocial) | Toggle(ref right, y, "ShowCombat".Translate(), ref tab.showCombat))
        {
            tab.cachedLogDisplay = null;
        }
    }

    // a small rounded toggle, lit in the accent when on; returns whether it was clicked
    private static bool Toggle(ref float right, float y, string label, ref bool on)
    {
        float w = Text.CalcSize(label).x + 14f * Scale;
        Rect r = new Rect(right - w, y, w, Control);
        right = r.x - 3f * Scale;
        bool over = Mouse.IsOver(r);
        GUI.color = on ? Style.Faded(0.3f) : over ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.06f);
        Widgets.DrawAtlas(r, ModernWidgets.Fill);
        GUI.color = on || over ? Color.white : Style.Dim;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(r, label);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        if (Widgets.ButtonInvisible(r))
        {
            on = !on;
            return true;
        }
        return false;
    }
}
