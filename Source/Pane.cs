using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustDesktop;

public enum PanePlace
{
    GamePlace,
    Center,
    NearPawn
}

public enum SkillNames
{
    Full,
    Short,
    None
}

// a smaller selection pane for one selected pawn: bars for health, mood and needs, skills, and the
// pane's tabs (Log, Gear, Health...) as small icons. One building, item or plant gets a card too: hit points
// and fuel or growth as bars, the game's text about it and its tabs. Several things selected get a card with a row
// for each pawn and the things' hit points together; linked storage keeps the game's pane, it has tabs.
[StaticConstructorOnStartup]
public static class Pane
{
    private static Settings S => JustDesktopMod.Settings;
    public static float Scale => S.paneSize;

    public static readonly Action<InspectTabBase, IInspectPane> ToggleTab = AccessTools.MethodDelegate<Action<InspectTabBase, IInspectPane>>(AccessTools.Method(typeof(InspectPaneUtility), "InterfaceToggleTab"));

    private static Texture2D _genericTab;

    private static Texture2D GenericTab => _genericTab ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Pane/Generic");
    private static readonly Dictionary<Type, Texture2D> tabIcons = new();
    private static readonly List<InspectTabBase> tabs = new();

    // the pawn or thing our card is showing (the first one when several are selected), null when the game's pane is used
    public static Thing Current
    {
        get
        {
            if (!S.paneOn || Verse.Current.ProgramState != ProgramState.Playing || WorldRendererUtility.WorldSelected || Find.Selector.NumSelected == 0)
            {
                return null;
            }
            if (!S.paneWithRimHud && RimHud.ModifiesPane)
            {
                return null;
            }
            if (Multi)
            {
                return MultiFirst();
            }
            Thing t = Find.Selector.SingleSelectedThing;
            if (t is not { Spawned: true })
            {
                return null;
            }
            if (t is Pawn p)
            {
                return p;
            }
            return S.paneThings ? t : null;
        }
    }

    // one selected zone (stockpile, growing zone...) gets a card of its own
    public static Zone CurrentZone
    {
        get
        {
            if (!S.paneOn || !S.paneThings || Verse.Current.ProgramState != ProgramState.Playing || WorldRendererUtility.WorldSelected || Find.Selector.NumSelected != 1)
            {
                return null;
            }
            if (!S.paneWithRimHud && RimHud.ModifiesPane)
            {
                return null;
            }
            return Find.Selector.SelectedZone;
        }
    }

    // our card is up, for a thing, pawn or zone
    public static bool Shown => Current != null || CurrentZone != null;

    public static Vector2 CardSize => Current is { } t ? SizeFor(t) : CurrentZone is { } z ? ZoneCard.Size(z) : default;

    public static Rect CardRect => Current is { } t ? Place(t) : CurrentZone is { } z ? ZoneCard.Place(z) : default;

    // the pawn the tab panels show: the one selected, or the one in a selected corpse
    public static Pawn CurrentPawn => Current switch { Pawn p => p, Corpse c => c.InnerPawn, _ => null };

    private static bool ShowSkills(Pawn p) => S.paneSkills && p.skills != null && p.RaceProps.Humanlike;

    public struct Bar
    {
        public string Label, Tip;
        public float Value;
        public Color Color;
        public Need Need;
        // shown instead of the percentage when set (hit points as "45 / 100")
        public string ValueText;
        // small marks across the bar (a need's thresholds), and arrows at the end of the fill when it's going up or down
        public List<float> Marks;
        public int Arrow;
    }

    private static readonly List<Bar> bars = new();

    private static void CollectBars(Pawn p)
    {
        bars.Clear();
        if (!S.paneBars)
        {
            return;
        }
        float health = p.health.summaryHealth.SummaryHealthPercent;
        bars.Add(new Bar { Label = "Health".Translate(), Value = health, Color = Color.Lerp(new Color(0.85f, 0.3f, 0.3f), new Color(0.38f, 0.75f, 0.44f), health) });
        if (p.needs == null)
        {
            return;
        }
        // the game keeps mood off its needs list (it has its own place), here it's a bar like the others
        if (p.needs.mood != null)
        {
            bars.Add(new Bar { Label = p.needs.mood.LabelCap, Value = p.needs.mood.CurLevelPercentage, Color = new Color(0.93f, 0.77f, 0.33f), Need = p.needs.mood });
        }
        Add(p.needs.food, new Color(0.95f, 0.6f, 0.3f));
        Add(p.needs.rest, new Color(0.45f, 0.63f, 0.96f));
        Add(p.needs.joy, new Color(0.72f, 0.52f, 0.95f));

        void Add(Need need, Color color)
        {
            if (need != null && need.ShowOnNeedList)
            {
                bars.Add(new Bar { Label = need.LabelCap, Value = need.CurLevelPercentage, Color = color, Need = need });
            }
        }
    }

    public static float RowHeight => Mathf.Round(16f * Scale);

    public static Vector2 SizeFor(Thing t)
    {
        if (Multi)
        {
            return MultiSize();
        }
        if (t is not Pawn p)
        {
            return ThingSize(t);
        }
        CollectBars(p);
        bool skills = ShowSkills(p);
        float rows = Mathf.Max(bars.Count * (RowHeight + 2f), skills ? SkillRows * (SkillRow + 2f) : 0f);
        float h = 10f * Scale + 24f * Scale + (S.paneActivity ? 16f * Scale : 0f) + 6f * Scale + rows + (rows > 0f ? 8f * Scale : 0f) + TabRow + 2f * Scale + 10f * Scale;
        float w = 260f * Scale;
        if (skills)
        {
            // full names: a bit wider for icon, name and badge; short or none: as wide as the grid needs
            w = SkillGrid ? Mathf.Max(340f * Scale, 20f * Scale + GridBarWidth + 14f * Scale + SkillsGridWidth()) : 430f * Scale;
        }
        return new Vector2(Mathf.Round(w), Mathf.Round(h));
    }

    // where the card goes: the game's corner, centered above the taskbar, or next to the pawn
    public static Rect Place(Thing p) => Place(SizeFor(p), p.DrawPos);

    public static Rect Place(Vector2 size, Vector3 drawPos)
    {
        float bottom = UI.screenHeight - Taskbar.Reserved;
        Rect r = new Rect(0f, bottom - size.y, size.x, size.y);
        if (DraftedRow)
        {
            // the left side, the gizmos have the middle
            return r;
        }
        if (S.panePlace == PanePlace.Center)
        {
            // the card alone is centered, so it stays put however many gizmos the selection has; they go to its right
            r.x = Mathf.Round((UI.screenWidth - size.x) / 2f);
        }
        else if (S.panePlace == PanePlace.NearPawn)
        {
            Vector2 at = drawPos.MapToUIPosition();
            float gap = 36f / Find.CameraDriver.ZoomRootSize * 10f + 12f;
            r.x = at.x + gap;
            if (r.xMax > UI.screenWidth)
            {
                r.x = at.x - gap - size.x;
            }
            r.y = at.y - size.y / 2f;
            r.x = Mathf.Round(Mathf.Clamp(r.x, 0f, UI.screenWidth - size.x));
            r.y = Mathf.Round(Mathf.Clamp(r.y, 40f, bottom - size.y));
        }
        return r;
    }

    private static Texture2D TabIcon(InspectTabBase tab)
    {
        Type type = tab.GetType();
        if (!tabIcons.TryGetValue(type, out Texture2D tex))
        {
            string name = type.Name.Replace("WITab_Caravan_", "").Replace("WITab_", "").Replace("ITab_Pawn_", "").Replace("ITab_", "");
            if (type.Name == "WITab_Caravan_Items")
            {
                name = "Contents";
            }
            // a few tabs share a picture
            if (name.StartsWith("Genes"))
            {
                name = "Genes";
            }
            else if (name.StartsWith("Contents"))
            {
                name = "Contents";
            }
            else if (name.StartsWith("StudyNotes"))
            {
                name = "StudyNotes";
            }
            else if (name.EndsWith("AutoCut"))
            {
                name = "AutoCut";
            }
            else if (name.StartsWith("Pen"))
            {
                name = "Pen";
            }
            else if (name.EndsWith("Storage"))
            {
                name = "Storage";
            }
            tex = ContentFinder<Texture2D>.Get("UI/JustDesktop/Pane/" + name, false) ?? GenericTab;
            tabIcons[type] = tex;
        }
        return tex;
    }

    // the card, drawn inside the game's selection pane window
    public static void Draw(Rect inRect, Thing t, IInspectPane pane)
    {
        if (Multi)
        {
            DrawMulti(inRect, pane);
            return;
        }
        if (t is not Pawn p)
        {
            DrawThing(inRect, t, pane);
            return;
        }
        pane.RecentHeight = inRect.height;
        CardBack(inRect);

        Rect inner = inRect.ContractedBy(Mathf.Round(10f * Scale));
        Widgets.BeginGroup(inner);
        try
        {
            Rect area = inner.AtZero();
            float end = Buttons(area, p, pane);
            float y = 0f;
            Text.Font = Scale >= 1.3f ? GameFont.Medium : GameFont.Small;
            Rect name = new Rect(0f, y, area.width - end - 6f, 24f * Scale);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(name, p.LabelShortCap);
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(name))
            {
                CameraJumper.TryJump(p);
            }
            y += 24f * Scale;
            Text.Font = Scale >= 1.25f ? GameFont.Small : GameFont.Tiny;
            if (S.paneActivity)
            {
                string title = p.RaceProps.Humanlike ? p.story?.TitleCap : p.KindLabel.CapitalizeFirst();
                // what they're doing only for pawns that are ours (prisoners and slaves too), not wild animals, visitors or enemies
                string job = Ours(p) ? p.GetJobReport() : null;
                GUI.color = Style.Dim;
                Rect line = new Rect(0f, y, area.width, 16f * Scale);
                Widgets.Label(line, title.NullOrEmpty() ? job : job.NullOrEmpty() ? title : title + "  ·  " + job);
                GUI.color = Color.white;
                y += 16f * Scale;
            }
            y += 6f * Scale;

            CollectBars(p);
            bool skills = ShowSkills(p);
            float barWidth = !skills ? area.width : SkillGrid ? GridBarWidth : Mathf.Round(area.width * 0.4f);
            for (int i = 0; i < bars.Count; i++)
            {
                DrawBar(new Rect(0f, y + i * (RowHeight + 2f), barWidth, RowHeight), bars[i], p);
            }
            if (skills)
            {
                DrawSkills(new Rect(barWidth + 14f * Scale, y, area.width - barWidth - 14f * Scale, SkillRows * (SkillRow + 2f)), p);
            }
            DrawTabs(new Rect(0f, area.height - TabRow, area.width, TabRow), pane);
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Widgets.EndGroup();
        }
    }

    // ---- buildings, items and plants ----

    private static float ThingWidth => Mathf.Round(320f * Scale);
    public static float Pad => Mathf.Round(10f * Scale);
    public static GameFont SmallFont => Scale >= 1.25f ? GameFont.Small : GameFont.Tiny;
    public static GameFont NameFont => Scale >= 1.3f ? GameFont.Medium : GameFont.Small;
    private const int MaxTextLines = 8;

    private static void CollectThingBars(Thing t)
    {
        bars.Clear();
        if (!S.paneBars)
        {
            return;
        }
        if (t.def.useHitPoints && t.MaxHitPoints > 0)
        {
            float hp = (float)t.HitPoints / t.MaxHitPoints;
            bars.Add(new Bar { Label = "HitPointsBasic".Translate().CapitalizeFirst(), Value = hp, ValueText = t.HitPoints + " / " + t.MaxHitPoints, Color = Color.Lerp(new Color(0.85f, 0.3f, 0.3f), new Color(0.38f, 0.75f, 0.44f), hp) });
        }
        if (t is Plant plant)
        {
            bars.Add(new Bar { Label = "Growth".Translate(), Value = plant.Growth, Color = new Color(0.5f, 0.78f, 0.36f) });
        }
        if (t.TryGetComp<CompRefuelable>() is { } fuel)
        {
            bars.Add(new Bar { Label = fuel.Props.FuelLabel.CapitalizeFirst(), Value = fuel.FuelPercentOfMax, ValueText = fuel.Fuel.ToString("0.#") + " / " + fuel.Props.fuelCapacity.ToString("0.#"), Color = new Color(0.95f, 0.6f, 0.3f) });
        }
        if (t.TryGetComp<CompPowerBattery>() is { } battery)
        {
            bars.Add(new Bar { Label = "PowerBatteryStored".Translate(), Value = battery.StoredEnergyPct, ValueText = battery.StoredEnergy.ToString("0") + " / " + battery.Props.storedEnergyMax.ToString("0") + " Wd", Color = new Color(0.93f, 0.8f, 0.35f) });
        }
    }

    // the line under the name: what kind of thing it is, and whose when it isn't ours
    private static string ThingKind(Thing t)
    {
        string kind = t.def.designationCategory?.LabelCap ?? t.def.FirstThingCategory?.LabelCap ?? (t is Plant ? t.def.plant.IsTree ? "Tree" : "Plant" : null);
        if (t.Faction != null && t.Faction != Faction.OfPlayer)
        {
            kind = kind.NullOrEmpty() ? t.Faction.Name : kind + "  ·  " + t.Faction.Name;
        }
        return kind;
    }

    // the game's text about the thing (power, bills, temperature, what's inside...), worked out once a frame
    private static Thing textThing;
    private static int textFrame = -1;
    private static string text;
    private static float textHeight;

    private static string InspectText(Thing t, out float height)
    {
        if (textThing != t || textFrame != Time.frameCount)
        {
            textThing = t;
            textFrame = Time.frameCount;
            try
            {
                text = t.GetInspectString();
                string low = t.GetInspectStringLowPriority();
                if (!low.NullOrEmpty())
                {
                    text = text.NullOrEmpty() ? low : text.TrimEndNewlines() + "\n" + low;
                }
            }
            catch (Exception e)
            {
                text = null;
                Log.ErrorOnce("[JustDesktop] couldn't get the text for " + t + ": " + e, 81723411);
            }
            text = text?.TrimEndNewlines();
            textHeight = 0f;
            if (!text.NullOrEmpty())
            {
                GameFont old = Text.Font;
                Text.Font = SmallFont;
                textHeight = Mathf.Min(Mathf.Ceil(Text.CalcHeight(text, ThingWidth - Pad * 2f)), Mathf.Ceil(Text.LineHeight * MaxTextLines));
                Text.Font = old;
            }
        }
        height = textHeight;
        return text;
    }

    public static bool HasTabs(IInspectPane pane) => pane.CurTabs != null && pane.CurTabs.Any(tab => tab.IsVisible && !tab.Hidden);

    private static Vector2 ThingSize(Thing t)
    {
        CollectThingBars(t);
        InspectText(t, out float textH);
        float h = Pad + 24f * Scale;
        if (!ThingKind(t).NullOrEmpty())
        {
            h += 16f * Scale;
        }
        h += 6f * Scale;
        if (bars.Count > 0)
        {
            h += bars.Count * (RowHeight + 2f) + 6f * Scale;
        }
        if (textH > 0f)
        {
            h += textH + 6f * Scale;
        }
        // the selection pane itself: with several windows open the one in front can be another
        if (MainButtonDefOf.Inspect.TabWindow is IInspectPane pane && HasTabs(pane))
        {
            h += TabRow + 2f * Scale;
        }
        return new Vector2(ThingWidth, Mathf.Round(h + Pad));
    }

    private static Vector2 textScroll;

    private static void DrawThing(Rect inRect, Thing t, IInspectPane pane)
    {
        pane.RecentHeight = inRect.height;
        CardBack(inRect);

        Rect inner = inRect.ContractedBy(Pad);
        Widgets.BeginGroup(inner);
        try
        {
            Rect area = inner.AtZero();
            float end = ThingButtons(area, t, pane);
            float y = 0f;
            Text.Font = Scale >= 1.3f ? GameFont.Medium : GameFont.Small;
            Rect name = new Rect(0f, y, area.width - end - 6f, 24f * Scale);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(name, t.LabelCap.Truncate(name.width));
            Text.WordWrap = true;
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(name))
            {
                CameraJumper.TryJump(t);
            }
            y += 24f * Scale;
            Text.Font = SmallFont;
            string kind = ThingKind(t);
            if (!kind.NullOrEmpty())
            {
                GUI.color = Style.Dim;
                Widgets.Label(new Rect(0f, y, area.width, 16f * Scale), kind);
                GUI.color = Color.white;
                y += 16f * Scale;
            }
            y += 6f * Scale;

            CollectThingBars(t);
            for (int i = 0; i < bars.Count; i++)
            {
                DrawBar(new Rect(0f, y + i * (RowHeight + 2f), area.width, RowHeight), bars[i], t);
            }
            if (bars.Count > 0)
            {
                y += bars.Count * (RowHeight + 2f) + 6f * Scale;
            }

            string info = InspectText(t, out float textH);
            if (textH > 0f)
            {
                Text.Font = SmallFont;
                GUI.color = Style.Text;
                Widgets.LabelScrollable(new Rect(0f, y, area.width, textH), info, ref textScroll, dontConsumeScrollEventsIfNoScrollbar: true);
                GUI.color = Color.white;
            }
            if (HasTabs(pane))
            {
                DrawTabs(new Rect(0f, area.height - TabRow, area.width, TabRow), pane);
            }
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

    // ---- several things selected ----

    private static bool Multi => Find.Selector.NumSelected > 1;
    private const int MaxPawnRows = 8;
    private static readonly List<Thing> multi = new();
    private static readonly List<Pawn> multiPawns = new();
    private static readonly List<(string label, int count)> groups = new();

    // all of them things on the map, and not linked storage (the game's pane shows its tabs)
    private static Thing MultiFirst()
    {
        if (!S.paneMulti)
        {
            return null;
        }
        Thing first = null;
        foreach (object o in Find.Selector.SelectedObjects)
        {
            if (o is not Thing { Spawned: true } t)
            {
                return null;
            }
            first ??= t;
        }
        if (MainButtonDefOf.Inspect.TabWindow is IInspectPane pane && pane.CurTabs != null)
        {
            return null;
        }
        return first;
    }

    // the pawns get rows, the rest share bars; what's selected is counted by kind like the game's title does
    private static void CollectMulti()
    {
        multi.Clear();
        multiPawns.Clear();
        groups.Clear();
        foreach (object o in Find.Selector.SelectedObjects)
        {
            if (o is not Thing t)
            {
                continue;
            }
            if (t is Pawn p)
            {
                multiPawns.Add(p);
            }
            else
            {
                multi.Add(t);
            }
            string label = (t is Pawn { IsMutant: true } mutant ? mutant.mutant.Def.label : t.GetInnerIfMinified().def.label).CapitalizeFirst();
            int i = groups.FindIndex(g => g.label == label);
            if (i < 0)
            {
                groups.Add((label, t.stackCount));
            }
            else
            {
                groups[i] = (label, groups[i].count + t.stackCount);
            }
        }
        bars.Clear();
        if (!S.paneBars)
        {
            return;
        }
        int hp = 0, maxHp = 0, plants = 0;
        float growth = 0f;
        foreach (Thing t in multi)
        {
            if (t.def.useHitPoints && t.MaxHitPoints > 0)
            {
                hp += t.HitPoints;
                maxHp += t.MaxHitPoints;
            }
            if (t is Plant plant)
            {
                growth += plant.Growth;
                plants++;
            }
        }
        if (maxHp > 0)
        {
            float value = (float)hp / maxHp;
            bars.Add(new Bar { Label = "HitPointsBasic".Translate().CapitalizeFirst(), Value = value, ValueText = hp + " / " + maxHp, Color = Color.Lerp(new Color(0.85f, 0.3f, 0.3f), new Color(0.38f, 0.75f, 0.44f), value) });
        }
        if (plants > 0)
        {
            bars.Add(new Bar { Label = "Growth".Translate(), Value = growth / plants, Color = new Color(0.5f, 0.78f, 0.36f) });
        }
    }

    private static bool ShowPawnRows => S.paneBars && multiPawns.Count > 0;

    private static Vector2 MultiSize()
    {
        CollectMulti();
        float h = Pad + 24f * Scale;
        if (groups.Count > 1)
        {
            h += 16f * Scale;
        }
        h += 6f * Scale;
        if (ShowPawnRows)
        {
            int rows = Mathf.Min(multiPawns.Count, MaxPawnRows);
            h += (rows + 1) * (RowHeight + 2f) + (multiPawns.Count > MaxPawnRows ? 16f * Scale : 0f) + 6f * Scale;
        }
        if (bars.Count > 0)
        {
            h += bars.Count * (RowHeight + 2f) + 6f * Scale;
        }
        return new Vector2(ThingWidth, Mathf.Round(h + Pad - 6f * Scale));
    }

    private static void DrawMulti(Rect inRect, IInspectPane pane)
    {
        pane.RecentHeight = inRect.height;
        CardBack(inRect);
        CollectMulti();
        Rect inner = inRect.ContractedBy(Pad);
        Widgets.BeginGroup(inner);
        try
        {
            Rect area = inner.AtZero();
            float y = 0f;
            Text.Font = Scale >= 1.3f ? GameFont.Medium : GameFont.Small;
            Rect name = new Rect(0f, y, area.width, 24f * Scale);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(name, pane.GetLabel(name).Truncate(name.width));
            Text.WordWrap = true;
            Text.Anchor = TextAnchor.UpperLeft;
            y += 24f * Scale;
            Text.Font = SmallFont;
            if (groups.Count > 1)
            {
                GUI.color = Style.Dim;
                Text.WordWrap = false;
                Rect line = new Rect(0f, y, area.width, 16f * Scale);
                Widgets.Label(line, string.Join("  ·  ", groups.Select(g => g.label + " x" + g.count)).Truncate(line.width));
                Text.WordWrap = true;
                GUI.color = Color.white;
                y += 16f * Scale;
            }
            y += 6f * Scale;

            if (ShowPawnRows)
            {
                float nameWidth = Mathf.Round(area.width * 0.36f);
                float gap = 6f * Scale;
                float barWidth = Mathf.Floor((area.width - nameWidth - gap * 2f) / 2f);
                float healthX = nameWidth + gap, moodX = healthX + barWidth + gap;
                GUI.color = Style.Dim;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(healthX + 5f * Scale, y, barWidth, RowHeight), "Health".Translate());
                Widgets.Label(new Rect(moodX + 5f * Scale, y, barWidth, RowHeight), "Mood".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                y += RowHeight + 2f;
                for (int i = 0; i < multiPawns.Count && i < MaxPawnRows; i++)
                {
                    PawnRow(new Rect(0f, y, area.width, RowHeight), multiPawns[i], nameWidth, healthX, moodX, barWidth);
                    y += RowHeight + 2f;
                }
                if (multiPawns.Count > MaxPawnRows)
                {
                    GUI.color = Style.Dim;
                    Widgets.Label(new Rect(0f, y, area.width, 16f * Scale), "+" + (multiPawns.Count - MaxPawnRows));
                    GUI.color = Color.white;
                    y += 16f * Scale;
                }
                y += 6f * Scale;
            }

            for (int i = 0; i < bars.Count; i++)
            {
                DrawBar(new Rect(0f, y + i * (RowHeight + 2f), area.width, RowHeight), bars[i], null);
            }
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

    // a pawn's name (click to select only them) with health and mood next to it
    private static void PawnRow(Rect row, Pawn p, float nameWidth, float healthX, float moodX, float barWidth)
    {
        Rect name = new Rect(row.x, row.y, nameWidth, row.height);
        bool over = Mouse.IsOver(name);
        if (over)
        {
            Widgets.DrawBoxSolid(name, new Color(1f, 1f, 1f, 0.05f));
        }
        GUI.color = over ? Color.white : Style.Text;
        Text.Anchor = TextAnchor.MiddleLeft;
        Text.WordWrap = false;
        Widgets.Label(name.ContractedBy(2f * Scale, 0f), p.LabelShortCap.Truncate(name.width - 4f * Scale));
        Text.WordWrap = true;
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        TooltipHandler.TipRegion(name, p.LabelCap);
        if (Widgets.ButtonInvisible(name))
        {
            Find.Selector.ClearSelection();
            Find.Selector.Select(p);
        }
        float health = p.health.summaryHealth.SummaryHealthPercent;
        DrawBar(new Rect(row.x + healthX, row.y, barWidth, row.height), new Bar { Label = "", Value = health, Color = Color.Lerp(new Color(0.85f, 0.3f, 0.3f), new Color(0.38f, 0.75f, 0.44f), health) }, p);
        if (p.needs?.mood != null)
        {
            DrawBar(new Rect(row.x + moodX, row.y, barWidth, row.height), new Bar { Label = "", Value = p.needs.mood.CurLevelPercentage, Color = new Color(0.93f, 0.77f, 0.33f), Need = p.needs.mood }, p);
        }
    }

    // the card's rounded background and border
    public static void CardBack(Rect rect)
    {
        GUI.color = S.windowColor;
        Widgets.DrawAtlas(rect, Style.Round);
        GUI.color = Style.Border;
        Widgets.DrawAtlas(rect, Style.RoundLine);
        GUI.color = Color.white;
    }

    // like the game's: select the next thing in the cell, info card, rename, and the owner's icon when it isn't ours
    private static float ThingButtons(Rect area, Thing t, IInspectPane pane)
    {
        float size = Mathf.Round(24f * Scale);
        float x = area.width;
        Rect Next()
        {
            x -= size;
            Rect r = new Rect(x, 0f, size, size);
            x -= 4f * Scale;
            return r;
        }

        if (pane.ShouldShowSelectNextInCellButton)
        {
            Rect r = Next();
            if (Widgets.ButtonImage(r, TexButton.SelectOverlappingNext))
            {
                pane.SelectNextInCell();
            }
            TooltipHandler.TipRegionByKey(r, "SelectNextInSquareTip", KeyBindingDefOf.SelectNextInCell.MainKeyLabel);
        }
        Rect info = Next();
        Widgets.InfoCardButton(info.x, info.y, t);
        bool ours = t.Faction == Faction.OfPlayer;
        if (t is IStorageGroupMember { ShowRenameButton: true } member)
        {
            if (member.Group != null)
            {
                RenameUIUtility.DrawRenameButton(Next(), member.Group);
            }
            else
            {
                RenameUIUtility.DrawRenameButton(Next(), member);
            }
        }
        else if (ours && t.TryGetComp<CompAnimalPenMarker>() is { } pen)
        {
            RenameUIUtility.DrawRenameButton(Next(), pen);
        }
        else if (ours && t.TryGetComp<CompPilotConsole>() is { engine: { } engine })
        {
            RenameUIUtility.DrawRenameButton(Next(), engine);
        }
        else if (t is IRenameable renameable)
        {
            RenameUIUtility.DrawRenameButton(Next(), renameable);
        }
        if (t.Faction != null && !ours && !t.Faction.def.FactionIcon.NullOrBad())
        {
            FactionUIUtility.DrawFactionIconWithTooltip(Next(), t.Faction);
        }
        return area.width - x;
    }

    // the game's small arrows for a bar that's filling or emptying
    private static Texture2D _arrowUp;
    private static Texture2D ArrowUp => _arrowUp ??= ContentFinder<Texture2D>.Get("UI/Widgets/FillChangeArrowRight");
    private static Texture2D _arrowDown;
    private static Texture2D ArrowDown => _arrowDown ??= ContentFinder<Texture2D>.Get("UI/Widgets/FillChangeArrowLeft");
    private static Texture2D _selfTendIcon;

    private static Texture2D SelfTendIcon => _selfTendIcon ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Pane/SelfTend");
    // the small buttons at the top right, right to left: the game's ones plus xenotype, faction, ideoligion,
    // and for our pawns medical care and self-tend, like RimHUD has. Returns how much room they took.
    private static float Buttons(Rect area, Pawn p, IInspectPane pane)
    {
        float size = Mathf.Round(24f * Scale);
        float x = area.width;
        Rect Next(float w = 0f)
        {
            w = w > 0f ? w : size;
            x -= w;
            Rect r = new Rect(x, 0f, w, w);
            x -= 4f * Scale;
            return r;
        }

        if (pane.ShouldShowSelectNextInCellButton)
        {
            Rect r = Next();
            if (Widgets.ButtonImage(r, TexButton.SelectOverlappingNext))
            {
                pane.SelectNextInCell();
            }
            TooltipHandler.TipRegionByKey(r, "SelectNextInSquareTip", KeyBindingDefOf.SelectNextInCell.MainKeyLabel);
        }
        Rect info = Next();
        Widgets.InfoCardButton(info.x, info.y, p);
        if (ModsConfig.BiotechActive && p.genes?.Xenotype != null)
        {
            Rect r = Next();
            if (Widgets.ButtonImage(r, p.genes.XenotypeIcon))
            {
                InspectPaneUtility.OpenTab(typeof(ITab_Genes));
            }
            TooltipHandler.TipRegion(r, "Xenotype".Translate().CapitalizeFirst() + ": " + p.genes.XenotypeLabelCap);
        }
        if (p.Faction != null && !p.Faction.def.FactionIcon.NullOrBad())
        {
            FactionUIUtility.DrawFactionIconWithTooltip(Next(), p.Faction);
        }
        if (ModsConfig.IdeologyActive && p.Ideo != null)
        {
            Rect r = Next();
            Ideo ideo = p.Ideo;
            IdeoUIUtility.DoIdeoIcon(r, ideo, false, () => IdeoUIUtility.OpenIdeoInfo(ideo));
            TooltipHandler.TipRegion(r, ideo.name + (p.ideo != null ? "\n" + "Certainty".Translate().CapitalizeFirst() + ": " + p.ideo.Certainty.ToStringPercent() : ""));
        }
        if (p.guilt != null && p.guilt.IsGuilty)
        {
            Rect r = Next();
            GUI.DrawTexture(r, TexUI.GuiltyTex);
            TooltipHandler.TipRegion(r, () => p.guilt.Tip, 6321224);
        }
        if (p.Faction == Faction.OfPlayer && p.playerSettings != null)
        {
            x -= 4f * Scale;
            if (p.playerSettings.UsesConfigurableHostilityResponse)
            {
                HostilityResponseModeUtility.DrawResponseButton(Next(), p, paintable: false);
            }
            if (p.RaceProps.IsFlesh)
            {
                MedicalCareUtility.MedicalCareSelectButton(Next(), p);
            }
            if (p.IsColonist)
            {
                SelfTend(Next(), p);
            }
            if ((p.RaceProps.Animal && p.RaceProps.hideTrainingTab) || (ModsConfig.BiotechActive && p.IsColonyMech))
            {
                RenameUIUtility.DrawRenameButton(Next(), p);
            }
        }
        return area.width - x;
    }

    public static void SelfTend(Rect r, Pawn p)
    {
        bool canDoctor = !p.WorkTypeIsDisabled(WorkTypeDefOf.Doctor);
        bool on = p.playerSettings.selfTend;
        GUI.color = !canDoctor ? Style.Disabled : on ? Style.Accent : Style.Dim;
        GUI.DrawTexture(r.ContractedBy(2f * Scale), SelfTendIcon);
        GUI.color = Color.white;
        if (Mouse.IsOver(r))
        {
            Widgets.DrawHighlight(r);
        }
        TooltipHandler.TipRegion(r, () => canDoctor
            ? "AllowSelfTend".Translate() + ": " + (on ? "On".Translate() : "Off".Translate()) + "\n\n" + "AllowSelfTendTip".Translate(Faction.OfPlayer.def.pawnsPlural.CapitalizeFirst(), 0.7f.ToStringPercent())
            : "MessageCannotSelfTendEver".Translate(p.LabelShort, p), 6321225);
        if (!Widgets.ButtonInvisible(r))
        {
            return;
        }
        if (!canDoctor)
        {
            Messages.Message("MessageCannotSelfTendEver".Translate(p.LabelShort, p), MessageTypeDefOf.RejectInput, false);
            return;
        }
        p.playerSettings.selfTend = !on;
        if (!on && p.workSettings != null && p.workSettings.GetPriority(WorkTypeDefOf.Doctor) == 0)
        {
            Messages.Message("MessageSelfTendUnsatisfied".Translate(p.LabelShort, p), MessageTypeDefOf.CautionInput, false);
        }
    }

    public static void DrawBar(Rect rect, Bar bar, Thing t)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.07f);
        Widgets.DrawAtlas(rect, ModernWidgets.Fill);
        float width = rect.width * Mathf.Clamp01(bar.Value);
        if (width >= 3f)
        {
            GUI.color = new Color(bar.Color.r, bar.Color.g, bar.Color.b, 0.75f);
            Widgets.DrawAtlas(new Rect(rect.x, rect.y, width, rect.height), ModernWidgets.Fill);
        }
        // mood shows where breaks start
        if (bar.Need is Need_Mood && t is Pawn { mindState.mentalBreaker: { } breaker })
        {
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            foreach (float at in new[] { breaker.BreakThresholdExtreme, breaker.BreakThresholdMajor, breaker.BreakThresholdMinor })
            {
                GUI.DrawTexture(new Rect(rect.x + Mathf.Round(rect.width * at), rect.y, 1f, rect.height), BaseContent.WhiteTex);
            }
        }
        if (bar.Marks != null)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            foreach (float at in bar.Marks)
            {
                GUI.DrawTexture(new Rect(rect.x + Mathf.Round(rect.width * at), rect.y, 1f, rect.height), BaseContent.WhiteTex);
            }
        }
        if (bar.Arrow != 0)
        {
            // pointing the way it's going, from the end of the fill, kept inside the bar
            int n = Mathf.Min(Mathf.Abs(bar.Arrow), 3);
            float w = Mathf.Round(rect.height * 0.45f), h = Mathf.Round(rect.height * 0.75f);
            float x = bar.Arrow > 0 ? rect.x + width + 2f : rect.x + width - 2f - n * w;
            x = Mathf.Clamp(x, rect.x, rect.xMax - n * w);
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            for (int i = 0; i < n; i++)
            {
                GUI.DrawTexture(new Rect(x + i * w, Mathf.Round(rect.center.y - h / 2f), w, h), bar.Arrow > 0 ? ArrowUp : ArrowDown);
            }
        }
        GUI.color = Color.white;
        Rect text = rect.ContractedBy(5f * Scale, 0f);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(text, bar.Label);
        Text.Anchor = TextAnchor.MiddleRight;
        Widgets.Label(text, bar.ValueText ?? bar.Value.ToStringPercent());
        Text.Anchor = TextAnchor.UpperLeft;
        if (Mouse.IsOver(rect))
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.04f));
            TooltipHandler.TipRegion(rect, bar.Need != null ? bar.Need.GetTipString() : bar.Tip ?? (bar.Label.NullOrEmpty() ? "" : bar.Label + ": ") + (bar.ValueText ?? bar.Value.ToStringPercent()));
        }
    }

    // the skills on the card: with full names two columns of six, top to bottom; with short names a grid of three
    // columns of four, left to right, with bigger icons; with only icons four columns of three, the icons as big as
    // three rows can be next to the five bars
    private static bool SkillGrid => S.paneSkillNames != SkillNames.Full;
    private static int SkillCols => S.paneSkillNames switch { SkillNames.Full => 2, SkillNames.Short => 3, _ => 4 };
    private static int SkillRows => S.paneSkillNames switch { SkillNames.Full => 6, SkillNames.Short => 4, _ => 3 };
    private static float SkillRow => S.paneSkillNames switch { SkillNames.Full => RowHeight, SkillNames.Short => Mathf.Round(20f * Scale), _ => Mathf.Round(28f * Scale) };
    private static float SkillGap => Mathf.Round(10f * Scale);
    private static float GridBarWidth => Mathf.Round(164f * Scale);

    private static string SkillLabel(SkillDef def)
    {
        string label = def.skillLabel.CapitalizeFirst();
        return S.paneSkillNames == SkillNames.Short && label.Length > 3 ? label.Substring(0, 3) : S.paneSkillNames == SkillNames.None ? "" : label;
    }

    // the grid's width: icon, short name if shown, badge, in each of its columns; measured in the font it's drawn in
    private static float SkillsGridWidth()
    {
        GameFont old = Text.Font;
        Text.Font = SmallFont;
        float name = S.paneSkillNames == SkillNames.Short ? Mathf.Ceil(Text.CalcSize("Con").x) + 6f * Scale : 0f;
        float cell = SkillIconRoom(null, SkillRow) + name + BadgeWidth;
        Text.Font = old;
        return SkillCols * cell + (SkillCols - 1) * SkillGap;
    }

    private static void DrawSkills(Rect rect, Pawn p)
    {
        List<SkillRecord> skills = p.skills.skills.OrderBy(s => s.def.listOrder).ToList();
        int cols = SkillCols, rows = SkillRows;
        float colWidth = (rect.width - (cols - 1) * SkillGap) / cols;
        for (int i = 0; i < skills.Count && i < cols * rows; i++)
        {
            SkillRecord skill = skills[i];
            int col = SkillGrid ? i % cols : i / rows, row = SkillGrid ? i / cols : i % rows;
            Rect cell = new Rect(rect.x + col * (colWidth + SkillGap), rect.y + row * (SkillRow + 2f), colWidth, SkillRow);
            bool disabled = skill.TotallyDisabled;
            float icon = SkillIconRoom(skill.def, cell.height);
            DrawSkillIcon(cell, skill.def, disabled);
            // no name with only icons; the game's Truncate throws on an empty string with no room
            string name = SkillLabel(skill.def);
            Rect label = new Rect(cell.x + icon, cell.y, cell.width - icon - BadgeWidth - 2f, cell.height);
            if (!name.NullOrEmpty() && label.width > 4f)
            {
                GUI.color = disabled ? Style.Disabled : Style.Text;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                Widgets.Label(label, name.Truncate(label.width));
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            // as tall as on a row of text, centered next to a bigger icon
            float badge = Mathf.Min(cell.height - 2f, RowHeight);
            SkillBadge(new Rect(cell.xMax - BadgeWidth, Mathf.Round(cell.center.y - badge / 2f), BadgeWidth, badge), skill);
            GUI.color = Color.white;
            if (Mouse.IsOver(cell))
            {
                Widgets.DrawBoxSolid(cell, new Color(1f, 1f, 1f, 0.05f));
                TooltipHandler.TipRegion(cell, skill.def.LabelCap + (disabled ? "" : ": " + skill.GetLevelForUI()) + "\n\n" + skill.def.description);
            }
        }
    }

    // our line icon for each of the game's skills, none for skills from mods
    private static readonly Dictionary<SkillDef, Texture2D> skillIcons = new();

    public static Texture2D SkillIcon(SkillDef def)
    {
        if (!skillIcons.TryGetValue(def, out Texture2D tex))
        {
            tex = ContentFinder<Texture2D>.Get("UI/JustDesktop/Skills/" + def.defName, false);
            skillIcons[def] = tex;
        }
        return tex;
    }

    // how much the icon takes at the start of a row, so the labels line up even next to a skill without one
    public static float SkillIconRoom(SkillDef def, float height) => Mathf.Round(height - 2f) + 4f * Scale;

    public static void DrawSkillIcon(Rect row, SkillDef def, bool disabled)
    {
        if (SkillIcon(def) is not { } tex)
        {
            return;
        }
        float size = Mathf.Round(row.height - 2f);
        GUI.color = disabled ? Style.Disabled : Style.Dim;
        GUI.DrawTexture(new Rect(row.x, Mathf.Round(row.center.y - size / 2f), size, size), tex);
        GUI.color = Color.white;
    }

    // the level in a small rounded badge colored by passion: dark for none, silver for minor, gold for major
    private static readonly Color BadgeNone = new(1f, 1f, 1f, 0.09f);
    private static readonly Color BadgeMinor = new(0.76f, 0.79f, 0.84f);
    private static readonly Color BadgeMajor = new(0.91f, 0.71f, 0.29f);
    private static readonly Color BadgeDarkText = new(0.1f, 0.11f, 0.13f);

    // room for two digits, so the badges line up and don't change size between 4 and 14
    public static float BadgeWidth => Mathf.Round(Text.CalcSize("20").x + 10f * Scale);

    public static void SkillBadge(Rect r, SkillRecord skill)
    {
        if (skill.TotallyDisabled)
        {
            GUI.color = Style.Disabled;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, "-");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return;
        }
        GUI.color = skill.passion == Passion.Major ? BadgeMajor : skill.passion == Passion.Minor ? BadgeMinor : BadgeNone;
        Widgets.DrawAtlas(r, ModernWidgets.Fill);
        GUI.color = skill.passion > Passion.None ? BadgeDarkText : Style.Text;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(r, skill.GetLevelForUI().ToString());
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
    }

    // the row of tab buttons at the bottom of the cards
    public static float TabRow => Mathf.Round(30f * Scale);

    // the pane's tabs as small icons, the open one marked
    public static void DrawTabs(Rect row, IInspectPane pane)
    {
        tabs.Clear();
        if (pane.CurTabs != null)
        {
            foreach (InspectTabBase tab in pane.CurTabs)
            {
                if (tab.IsVisible && !tab.Hidden)
                {
                    tabs.Add(tab);
                }
            }
        }
        float size = row.height;
        float gap = 4f * Scale;
        for (int i = 0; i < tabs.Count; i++)
        {
            InspectTabBase tab = tabs[i];
            Rect button = new Rect(row.x + i * (size + gap), row.y, size, size);
            bool open = tab.GetType() == pane.OpenTabType;
            bool over = Mouse.IsOver(button);
            if (open || over)
            {
                GUI.color = open ? Style.Faded(0.3f) : new Color(1f, 1f, 1f, 0.1f);
                Widgets.DrawAtlas(button, ModernWidgets.Fill);
            }
            GUI.color = open ? Color.white : Style.Text;
            GUI.DrawTexture(button.ContractedBy(Mathf.Round(4f * Scale)), TabIcon(tab));
            GUI.color = Color.white;
            TooltipHandler.TipRegion(button, tab.labelKey.Translate());
            if (!open && !tab.TutorHighlightTagClosed.NullOrEmpty())
            {
                UIHighlighter.HighlightOpportunity(button, tab.TutorHighlightTagClosed);
            }
            if (Widgets.ButtonInvisible(button))
            {
                ToggleTab(tab, pane);
            }
        }
    }

    public static bool Ours(Pawn p) => p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony || p.IsSlaveOfColony;

    // gizmos start right after the card in the game's corner and when centered; next to the pawn they go to the left edge
    // a drafted pawn with the option on: the card at the left and the gizmos in one row in the middle
    public static bool DraftedRow => S.paneDraftedRow && S.gizmoGrid && CurrentPawn is { Drafted: true };

    public static float GizmoOffset(IInspectPane pane)
    {
        if (pane is WorldInspectPane)
        {
            return WorldCard.Shown ? WorldCard.Rect.xMax : InspectPaneUtility.PaneWidthFor(pane);
        }
        if (Shown && DraftedRow)
        {
            // centered by how wide the row came out last time; the game adds 14 to this
            return Mathf.Max(Mathf.Max(CardRect.xMax, GizmoGrid.BarsRight) + 8f, Mathf.Round((UI.screenWidth - GizmoGrid.RowWidth) / 2f)) - 14f;
        }
        if (!Shown || S.panePlace == PanePlace.GamePlace)
        {
            return InspectPaneUtility.PaneWidthFor(pane);
        }
        // centered: gizmos start after the card; next to the pawn: back to the left edge
        return S.panePlace == PanePlace.Center ? CardRect.xMax : 0f;
    }
}

[HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.InspectPaneOnGUI))]
public static class Pane_Draw
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(Rect inRect, IInspectPane pane)
    {
        if (pane is WorldInspectPane && WorldCard.Shown)
        {
            WorldCard.Draw(inRect, pane);
            return false;
        }
        if (pane is not MainTabWindow_Inspect)
        {
            return true;
        }
        if (Pane.Current is { } p)
        {
            Pane.Draw(inRect, p, pane);
            return false;
        }
        if (Pane.CurrentZone is { } z)
        {
            ZoneCard.Draw(inRect, z, pane);
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.PaneSizeFor))]
public static class Pane_Size
{
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(IInspectPane pane, ref Vector2 __result)
    {
        if (pane is MainTabWindow_Inspect && Pane.Shown)
        {
            __result = Pane.CardSize;
        }
        else if (pane is WorldInspectPane && WorldCard.Shown)
        {
            __result = WorldCard.Size;
        }
    }
}

[HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.PaneWidthFor))]
public static class Pane_Width
{
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(IInspectPane pane, ref float __result)
    {
        if (pane is MainTabWindow_Inspect && Pane.Shown)
        {
            __result = Pane.CardSize.x;
        }
        else if (pane is WorldInspectPane && WorldCard.Shown)
        {
            __result = WorldCard.Size.x;
        }
    }
}

// the tab buttons are in the card, only the open tab's panel is left to draw
[HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
public static class Pane_Tabs
{
    [HarmonyPriority(Priority.First)]
    public static bool Prefix(IInspectPane pane)
    {
        if (!(pane is MainTabWindow_Inspect && Pane.Shown || pane is WorldInspectPane && WorldCard.Shown) || pane.CurTabs == null)
        {
            return true;
        }
        foreach (InspectTabBase tab in pane.CurTabs)
        {
            if (tab.IsVisible && tab.GetType() == pane.OpenTabType)
            {
                tab.DoTabGUI();
                pane.RecentHeight = 700f;
            }
        }
        return false;
    }
}

// tab panels (Gear, Health...) open to the left of the card, their bottoms lined up; on top of it when
// there's no room on the left (the card in the game's corner)
[HarmonyPatch(typeof(InspectTabBase), "TabRect", MethodType.Getter)]
public static class Pane_TabRect
{
    public static void Postfix(InspectTabBase __instance, ref Rect __result)
    {
        Rect card;
        if (__instance is ITab && Pane.Shown)
        {
            TabPanels.Resize(__instance, ref __result);
            card = Pane.CardRect;
        }
        else if (__instance is WITab && WorldCard.Shown)
        {
            card = WorldCard.Rect;
        }
        else
        {
            return;
        }
        float left = card.x - 8f - __result.width;
        if (left >= 0f)
        {
            __result.x = left;
            __result.y = Mathf.Max(0f, card.yMax - __result.height);
            return;
        }
        __result.x = Mathf.Clamp(card.x, 0f, UI.screenWidth - __result.width);
        __result.y = Mathf.Max(0f, card.y - __result.height);
    }
}

[HarmonyPatch(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGridFor))]
public static class Pane_Gizmos
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo width = AccessTools.Method(typeof(InspectPaneUtility), nameof(InspectPaneUtility.PaneWidthFor));
        MethodInfo offset = AccessTools.Method(typeof(Pane), nameof(Pane.GizmoOffset));
        foreach (CodeInstruction ins in instructions)
        {
            yield return ins.Calls(width) ? new CodeInstruction(OpCodes.Call, offset).MoveLabelsFrom(ins) : ins;
        }
    }
}

// Camera+ pushes the bottom-right corner (gizmos included) inward when its edge markers would cover it.
// With the card centered the gizmos sit next to it, away from the edges, so they keep their place:
// the matrix from before Camera+ is put back after its prefix (its finalizer restores it as usual)
[HarmonyPatch(typeof(MapGizmoUtility), "MapUIOnGUI")]
public static class Pane_GizmoMatrixSave
{
    public static Matrix4x4 Before;

    // Camera+ also uses the first priority, so it has to be named
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("net.pardeike.rimworld.mod.camera+")]
    public static void Prefix()
    {
        Before = GUI.matrix;
    }
}

[HarmonyPatch(typeof(MapGizmoUtility), "MapUIOnGUI")]
public static class Pane_GizmoMatrixUndo
{
    [HarmonyPriority(Priority.Last)]
    public static void Prefix()
    {
        if (Pane.Shown && (JustDesktopMod.Settings.panePlace == PanePlace.Center || Pane.DraftedRow))
        {
            GUI.matrix = Pane_GizmoMatrixSave.Before;
        }
    }
}

// Camera+ counts the gizmos as part of the bottom-right corner (from the selection pane to the right edge) when it decides to
// push that corner in for its edge markers. Centered they're nowhere near it, so with the card centered it doesn't count them.
[HarmonyPatch]
public static class Pane_CameraPlusGizmoArea
{
    private static MethodBase target;

    public static bool Prepare()
    {
        target = AccessTools.TypeByName("CameraPlus.MapGizmoEdgeInsetPatch") is { } type ? AccessTools.Method(type, "Postfix") : null;
        return target != null;
    }

    public static MethodBase TargetMethod() => target;

    public static bool Prefix() => !(Pane.Shown && (JustDesktopMod.Settings.panePlace == PanePlace.Center || Pane.DraftedRow));
}
