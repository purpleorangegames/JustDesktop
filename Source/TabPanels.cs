using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the selection pane's tab panels (Gear, Health, Needs...) in the card's style: rounded background, our close button,
// and the modern buttons, checkboxes and tabs inside. Needs, Character, Health, Social, Gear and Log have layouts of their own; the others keep the game's for now
public static class TabPanels
{
    private static Settings S => JustDesktopMod.Settings;

    private static readonly Action<InspectTabBase> fillTab = AccessTools.MethodDelegate<Action<InspectTabBase>>(AccessTools.Method(typeof(InspectTabBase), "FillTab"));
    private static readonly Action<InspectTabBase> closeTab = AccessTools.MethodDelegate<Action<InspectTabBase>>(AccessTools.Method(typeof(InspectTabBase), "CloseTab"));
    private static readonly Action<InspectTabBase> extraOnGui = AccessTools.MethodDelegate<Action<InspectTabBase>>(AccessTools.Method(typeof(InspectTabBase), "ExtraOnGUI"));
    private static readonly Func<InspectTabBase, bool> stillValid = AccessTools.MethodDelegate<Func<InspectTabBase, bool>>(AccessTools.PropertyGetter(typeof(InspectTabBase), "StillValid"));
    private static readonly Func<InspectTabBase, Rect> tabRect = AccessTools.MethodDelegate<Func<InspectTabBase, Rect>>(AccessTools.PropertyGetter(typeof(InspectTabBase), "TabRect"));

    // the map's tabs, and on the world map a caravan's (Items, Gear, Needs...), which keep the game's layout on our card
    public static bool On(InspectTabBase tab) => S.modernTabs && (tab is ITab && Pane.Shown || tab is RimWorld.Planet.WITab && WorldCard.Shown);

    // the pawn of a tab we lay out ourselves, null when the game's layout is used
    private static Pawn NeedsPawn(InspectTabBase tab) => tab is ITab_Pawn_Needs && Pane.CurrentPawn is Pawn { needs: not null } p ? p : null;
    private static Pawn CharacterPawn(InspectTabBase tab) => tab is ITab_Pawn_Character && Pane.CurrentPawn is Pawn { story: not null } p ? p : null;
    private static Pawn HealthPawn(InspectTabBase tab) => tab is ITab_Pawn_Health && Pane.CurrentPawn is Pawn p ? p : null;
    private static Pawn SocialPawn(InspectTabBase tab) => tab is ITab_Pawn_Social && Pane.CurrentPawn is Pawn p && SocialPanel.Shows(p) ? p : null;
    private static Pawn GearPawn(InspectTabBase tab) => tab is ITab_Pawn_Gear && Pane.CurrentPawn is Pawn p ? p : null;
    private static Pawn LogPawn(InspectTabBase tab) => tab is ITab_Pawn_Log && Pane.CurrentPawn is Pawn p ? p : null;

    // the size of a tab we lay out ourselves, before it's placed next to the card
    public static void Resize(InspectTabBase tab, ref Rect rect)
    {
        if (!On(tab))
        {
            return;
        }
        Vector2? size = NeedsPawn(tab) is { } needs ? NeedsPanel.Size(needs)
            : CharacterPawn(tab) is { } character ? CharacterPanel.Size(character)
            : HealthPawn(tab) is { } health ? HealthPanel.Size((ITab_Pawn_Health)tab, health)
            : SocialPawn(tab) is { } social ? SocialPanel.Size(social)
            : GearPawn(tab) is { } gear ? GearPanel.Size((ITab_Pawn_Gear)tab, gear)
            : LogPawn(tab) is { } log ? LogPanel.Size((ITab_Pawn_Log)tab, log)
            : null;
        if (size is { } v)
        {
            rect.width = v.x;
            rect.height = v.y;
        }
    }

    // InspectTabBase.DoTabGUI with our background and close button; the window and its id are the game's
    public static void DoTabGUI(InspectTabBase tab)
    {
        Rect rect = tabRect(tab);
        Find.WindowStack.ImmediateWindow(235086, rect, WindowLayer.GameUI, () =>
        {
            if (!stillValid(tab) || !tab.IsVisible)
            {
                return;
            }
            Rect all = rect.AtZero();
            Pane.CardBack(all);
            if (CloseButton(all))
            {
                closeTab(tab);
            }
            ModernWidgets.Active = S.modernWidgets;
            try
            {
                if (NeedsPawn(tab) is { } p)
                {
                    NeedsPanel.Draw(all, p);
                }
                else if (CharacterPawn(tab) is { } c)
                {
                    CharacterPanel.Draw(all, c);
                }
                else if (HealthPawn(tab) is { } h)
                {
                    HealthPanel.Draw(all, (ITab_Pawn_Health)tab, h);
                }
                else if (SocialPawn(tab) is { } s)
                {
                    SocialPanel.Draw(all, s);
                }
                else if (GearPawn(tab) is { } g)
                {
                    GearPanel.Draw(all, (ITab_Pawn_Gear)tab, g);
                }
                else if (LogPawn(tab) is { } l)
                {
                    LogPanel.Draw(all, (ITab_Pawn_Log)tab, l);
                }
                else
                {
                    fillTab(tab);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[JustDesktop] exception filling tab " + tab.GetType() + ": " + e, 49827 ^ tab.GetType().GetHashCode());
            }
            finally
            {
                ModernWidgets.Active = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = Color.white;
            }
        }, doBackground: false, absorbInputAroundWindow: false, 1f, tab.Notify_ClickOutsideWindow);
        extraOnGui(tab);
    }

    // where the game puts its close button, so the tabs' own layouts still leave room for it
    private static bool CloseButton(Rect all)
    {
        Rect r = new Rect(all.width - 18f - 6f, 6f, 18f, 18f);
        bool over = Mouse.IsOver(r);
        GUI.color = over ? Style.CloseHover : Style.Dim;
        GUI.DrawTexture(r, Style.Close);
        GUI.color = Color.white;
        return Widgets.ButtonInvisible(r);
    }
}

[HarmonyPatch(typeof(InspectTabBase), nameof(InspectTabBase.DoTabGUI))]
public static class TabPanels_DoTabGUI
{
    public static bool Prefix(InspectTabBase __instance)
    {
        if (!TabPanels.On(__instance))
        {
            return true;
        }
        TabPanels.DoTabGUI(__instance);
        return false;
    }
}

// mood on top, the needs as bars in two columns, then the thoughts as rows with their mood and the total;
// as tall as what's in it, the thoughts scroll past a dozen
public static class NeedsPanel
{
    private static float Scale => Pane.Scale;
    private static float Width => Mathf.Round(400f * Scale);
    private static float Row => Pane.RowHeight + 2f;
    private static float ThoughtRow => Mathf.Round(20f * Scale);
    private const int MaxThoughtRows = 12;

    private static readonly List<Need> needs = new();
    private static readonly List<Thought> groups = new();
    private static readonly List<Thought> group = new();

    private struct ThoughtLine
    {
        public Thought Group, Leading;
        public int Count;
        public float Mood;
    }

    private static readonly List<ThoughtLine> thoughts = new();
    private static Vector2 scroll;

    private static readonly AccessTools.FieldRef<Need, List<float>> threshPercents = AccessTools.FieldRefAccess<Need, List<float>>("threshPercents");

    private static void Collect(Pawn p)
    {
        needs.Clear();
        foreach (Need need in p.needs.AllNeeds)
        {
            if (need.ShowOnNeedList && need is not Need_Mood)
            {
                needs.Add(need);
            }
        }
        PawnNeedsUIUtility.SortInDisplayOrder(needs);
        thoughts.Clear();
        if (p.needs.mood == null)
        {
            return;
        }
        PawnNeedsUIUtility.GetThoughtGroupsInDisplayOrder(p.needs.mood, groups);
        foreach (Thought g in groups)
        {
            p.needs.mood.thoughts.GetMoodThoughts(g, group);
            if (group.Count == 0)
            {
                continue;
            }
            Thought leading = PawnNeedsUIUtility.GetLeadingThoughtInGroup(group);
            if (!leading.VisibleInNeedsTab)
            {
                continue;
            }
            thoughts.Add(new ThoughtLine { Group = g, Leading = leading, Count = group.Count, Mood = p.needs.mood.thoughts.MoodOffsetOfGroup(g) });
        }
        group.Clear();
    }

    private static bool HumanMeat(Pawn p) => ModLister.IdeologyInstalled && p.Ideo != null && p.Ideo.IdeoCausesHumanMeatCravings();

    public static Vector2 Size(Pawn p)
    {
        Collect(p);
        float h = Pane.Pad + 24f * Scale + 6f * Scale;
        if (p.needs.mood != null)
        {
            h += Row + 4f * Scale + 8f * Scale;
        }
        if (needs.Count > 0)
        {
            h += Mathf.Ceil(needs.Count / 2f) * Row + 8f * Scale;
        }
        if (p.needs.mood != null)
        {
            h += Row + Mathf.Max(1, Mathf.Min(thoughts.Count, MaxThoughtRows)) * ThoughtRow;
            if (HumanMeat(p))
            {
                h += 4f * Scale + 16f * Scale;
            }
        }
        return new Vector2(Width, Mathf.Round(h + Pane.Pad));
    }

    public static void Draw(Rect all, Pawn p)
    {
        Collect(p);
        Rect inner = all.ContractedBy(Pane.Pad);
        Widgets.BeginGroup(inner);
        try
        {
            Rect area = inner.AtZero();
            float y = 0f;
            Text.Font = Pane.NameFont;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, y, area.width - 30f, 24f * Scale), "TabNeeds".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            y += 24f * Scale + 6f * Scale;
            Text.Font = Pane.SmallFont;

            Need_Mood mood = p.needs.mood;
            if (mood != null)
            {
                Rect bar = new Rect(0f, y, area.width, Pane.RowHeight + 4f * Scale);
                Pane.DrawBar(bar, new Pane.Bar { Label = mood.LabelCap + "  ·  " + mood.MoodString.CapitalizeFirst(), Value = mood.CurLevelPercentage, Color = new Color(0.93f, 0.77f, 0.33f), Need = mood, Arrow = mood.GUIChangeArrow }, p);
                y += bar.height + 2f + 8f * Scale;
            }

            if (needs.Count > 0)
            {
                float gap = 10f * Scale;
                float col = Mathf.Floor((area.width - gap) / 2f);
                for (int i = 0; i < needs.Count; i++)
                {
                    Need need = needs[i];
                    Rect r = new Rect(i % 2 == 0 ? 0f : col + gap, y + (i / 2) * Row, col, Pane.RowHeight);
                    Pane.DrawBar(r, new Pane.Bar { Label = need.LabelCap, Value = need.CurLevelPercentage, Color = ColorFor(need), Need = need, Marks = threshPercents(need), Arrow = need.GUIChangeArrow }, p);
                    if (!need.def.tutorHighlightTag.NullOrEmpty())
                    {
                        UIHighlighter.HighlightOpportunity(r, need.def.tutorHighlightTag);
                    }
                }
                y += Mathf.Ceil(needs.Count / 2f) * Row + 8f * Scale;
            }

            if (mood != null)
            {
                DrawThoughts(new Rect(0f, y, area.width, area.height - y), p);
            }
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.EndGroup();
        }
    }

    private static Color ColorFor(Need need)
    {
        if (need is Need_Chemical or Need_Chemical_Any)
        {
            return new Color(0.85f, 0.4f, 0.4f);
        }
        return need.def.defName switch
        {
            "Food" => new Color(0.95f, 0.6f, 0.3f),
            "Rest" => new Color(0.45f, 0.63f, 0.96f),
            "Joy" => new Color(0.72f, 0.52f, 0.95f),
            "Beauty" => new Color(0.9f, 0.55f, 0.75f),
            "Comfort" => new Color(0.6f, 0.78f, 0.5f),
            "Outdoors" or "Indoors" => new Color(0.45f, 0.78f, 0.7f),
            _ => new Color(0.55f, 0.68f, 0.8f)
        };
    }

    private static readonly Color Good = new(0.5f, 0.85f, 0.5f);
    private static readonly Color Bad = new(0.92f, 0.45f, 0.45f);

    private static void DrawThoughts(Rect rect, Pawn p)
    {
        if (Event.current.type == EventType.Layout)
        {
            return;
        }
        float total = thoughts.Sum(t => t.Mood);
        Rect header = new Rect(rect.x, rect.y, rect.width, Pane.RowHeight);
        GUI.color = Style.Dim;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(header, "Thoughts");
        Text.Anchor = TextAnchor.MiddleRight;
        GUI.color = total > 0f ? Good : total < 0f ? Bad : Style.Dim;
        Widgets.Label(header.ContractedBy(5f * Scale, 0f), total.ToStringWithSign("0"));
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;

        Rect list = new Rect(rect.x, rect.y + Row, rect.width, Mathf.Max(1, Mathf.Min(thoughts.Count, MaxThoughtRows)) * ThoughtRow);
        if (thoughts.Count == 0)
        {
            GUI.color = Style.Dim;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(list.ContractedBy(5f * Scale, 0f), "None".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        else
        {
            bool scrolls = thoughts.Count > MaxThoughtRows;
            Rect view = new Rect(0f, 0f, list.width - (scrolls ? 16f : 0f), thoughts.Count * ThoughtRow);
            Widgets.BeginScrollView(list, ref scroll, view, scrolls);
            for (int i = 0; i < thoughts.Count; i++)
            {
                DrawThought(new Rect(0f, i * ThoughtRow, view.width, ThoughtRow), thoughts[i], p);
            }
            Widgets.EndScrollView();
        }

        if (HumanMeat(p))
        {
            Rect meat = new Rect(rect.x, list.yMax + 4f * Scale, rect.width, 16f * Scale);
            GUI.color = Style.Dim;
            string text = "LastHumanMeat".Translate() + ": " + "TimeAgo".Translate((Find.TickManager.TicksGame - p.mindState.lastHumanMeatIngestedTick).ToStringTicksToPeriod());
            Widgets.Label(meat, text);
            GUI.color = Color.white;
            if (Mouse.IsOver(meat))
            {
                Widgets.DrawHighlight(meat);
                TooltipHandler.TipRegion(meat, "LastHumanMeatDesc".Translate(p.Named("PAWN"), p.Ideo.Named("IDEO"), 8.Named("DURATION")).Resolve());
            }
        }
    }

    // a thought: its name (and how many), the ideoligion it comes from, and its mood, with the game's tooltip
    private static void DrawThought(Rect r, ThoughtLine line, Pawn p)
    {
        if (Mouse.IsOver(r))
        {
            GUI.color = new Color(1f, 1f, 1f, 0.06f);
            Widgets.DrawAtlas(r, ModernWidgets.Fill);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(r, new TipSignal(() => Tip(line, p), 7291 ^ line.Group.def.shortHash));
        }
        float number = Mathf.Round(36f * Scale);
        Rect text = new Rect(r.x + 5f * Scale, r.y, r.width - number - 5f * Scale - 24f * Scale, r.height);
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        GUI.color = Style.Text;
        string label = line.Leading.LabelCap + (line.Count > 1 ? " x" + line.Count : "");
        Widgets.Label(text, label.Truncate(text.width));
        Text.WordWrap = true;
        if (ModsConfig.IdeologyActive && line.Leading.sourcePrecept != null && !Find.IdeoManager.classicMode)
        {
            float icon = Mathf.Round(16f * Scale);
            Ideo ideo = line.Leading.sourcePrecept.ideo;
            GUI.color = Color.white;
            IdeoUIUtility.DoIdeoIcon(new Rect(r.xMax - number - icon - 4f * Scale, Mathf.Round(r.center.y - icon / 2f), icon, icon), ideo, false, () => IdeoUIUtility.OpenIdeoInfo(ideo));
        }
        GUI.color = line.Mood > 0f ? Good : line.Mood < 0f ? Bad : Style.Dim;
        Text.Anchor = TextAnchor.MiddleRight;
        Widgets.Label(new Rect(r.xMax - number - 5f * Scale, r.y, number, r.height), line.Mood.ToStringWithSign("0"));
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
    }

    // what the game says when the mouse is over a thought: what it is, when it wears off, and the others like it
    private static string Tip(ThoughtLine line, Pawn p)
    {
        p.needs.mood.thoughts.GetMoodThoughts(line.Group, group);
        StringBuilder sb = new StringBuilder();
        sb.Append(line.Leading.LabelCap.AsTipTitle()).AppendLine().AppendLine();
        sb.Append(p.DevelopmentalStage.Baby() ? line.Leading.BabyTalk : line.Leading.Description);
        int duration = line.Group.DurationTicks;
        if (duration > 5 && line.Leading is Thought_Memory memory)
        {
            sb.AppendLine().AppendLine();
            if (group.Count <= 1)
            {
                sb.AppendTagged("ThoughtExpiresIn".Translate((duration - memory.age).ToStringTicksToPeriod()));
            }
            else
            {
                int youngest = int.MaxValue, oldest = int.MinValue;
                foreach (Thought t in group)
                {
                    if (t is Thought_Memory m)
                    {
                        youngest = Mathf.Min(youngest, m.age);
                        oldest = Mathf.Max(oldest, m.age);
                    }
                }
                sb.AppendTagged("ThoughtStartsExpiringIn".Translate((duration - oldest).ToStringTicksToPeriod()));
                sb.AppendLine();
                sb.AppendTagged("ThoughtFinishesExpiringIn".Translate((duration - youngest).ToStringTicksToPeriod()));
            }
        }
        HashSet<string> others = new();
        foreach (Thought t in group)
        {
            if (t != line.Leading && t.LabelCap != line.Leading.LabelCap && others.Add(t.LabelCap))
            {
                if (others.Count == 1)
                {
                    sb.AppendLine().AppendLine();
                }
                sb.AppendLine("+ " + t.LabelCap);
            }
        }
        group.Clear();
        return sb.ToString();
    }
}
