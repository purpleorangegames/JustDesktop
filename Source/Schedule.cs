using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// the schedule in the modern windows: each hour is still its own cell to click or drag over, but hours in a row
// with the same activity are drawn as one rounded block (hairlines between the hours, the name inside when it
// fits), in softer colors; a line marks the hour it is now, the header names every sixth hour, the activities to
// paint with are pills, and the allowed areas are pills with the area's color (dragging down the column still sets
// them for many at once). Pawn tables get alternating rows instead of lines between them
public static class Schedule
{
    public static bool On => ModernWidgets.Active;

    private static readonly Color DarkText = new(0.1f, 0.11f, 0.13f);

    public static Color ColorFor(TimeAssignmentDef def) => def.defName switch
    {
        "Anything" => new Color(0.24f, 0.26f, 0.3f),
        "Work" => new Color(0.82f, 0.62f, 0.3f),
        "Joy" => new Color(0.6f, 0.45f, 0.85f),
        "Sleep" => new Color(0.32f, 0.36f, 0.66f),
        "Meditate" => new Color(0.4f, 0.7f, 0.46f),
        _ => Color.Lerp(def.color, new Color(0.3f, 0.32f, 0.36f), 0.3f)
    };

    private static Color TextOn(Color c) => c.r * 0.3f + c.g * 0.59f + c.b * 0.11f > 0.55f ? DarkText : Color.white;

    // where in a 24 hour span it is now on the map shown
    private static float? NowX(Rect r) => Find.CurrentMap is { } map ? r.x + r.width * GenLocalDate.HourFloat(map) / 24f : null;

    private static void NowLine(Rect r)
    {
        if (NowX(r) is { } x)
        {
            GUI.color = Style.Accent;
            GUI.DrawTexture(new Rect(Mathf.Round(x) - 1f, r.y, 2f, r.height), BaseContent.WhiteTex);
            GUI.color = Color.white;
        }
    }

    public static void Row(Rect rect, Pawn pawn)
    {
        float hour = rect.width / 24f;
        Rect band = new Rect(rect.x, rect.y + 3f, rect.width, rect.height - 6f);
        // the blocks: a run of the same activity, with a little gap between runs
        int start = 0;
        for (int i = 1; i <= 24; i++)
        {
            TimeAssignmentDef def = pawn.timetable.GetAssignment(start);
            if (i < 24 && pawn.timetable.GetAssignment(i) == def)
            {
                continue;
            }
            Rect run = new Rect(Mathf.Round(band.x + start * hour) + 1f, band.y, Mathf.Round((i - start) * hour) - 2f, band.height);
            Color color = ColorFor(def);
            GUI.color = color;
            Widgets.DrawAtlas(run, ModernWidgets.Fill);
            GUI.color = new Color(0f, 0f, 0f, 0.22f);
            for (int h = start + 1; h < i; h++)
            {
                GUI.DrawTexture(new Rect(Mathf.Round(band.x + h * hour), run.y + 2f, 1f, run.height - 4f), BaseContent.WhiteTex);
            }
            Text.Font = GameFont.Tiny;
            string label = def.LabelCap;
            if (Text.CalcSize(label).x + 8f <= run.width)
            {
                GUI.color = TextOn(color);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(run, label);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            start = i;
        }
        // each hour stays its own cell: hover, tooltip and painting like the game's
        bool mouse = Input.GetMouseButton(0);
        for (int i = 0; i < 24; i++)
        {
            Rect cell = new Rect(band.x + i * hour, band.y, hour, band.height);
            if (!mouse)
            {
                MouseoverSounds.DoRegion(cell);
            }
            if (!Mouse.IsOver(cell))
            {
                continue;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.22f);
            Widgets.DrawAtlas(cell.ContractedBy(1f), ModernWidgets.Fill);
            GUI.color = Color.white;
            TimeAssignmentDef current = pawn.timetable.GetAssignment(i);
            TooltipHandler.TipRegion(cell, pawn.LabelShortCap + "  ·  " + i.ToString("00") + ":00 - " + ((i + 1) % 24).ToString("00") + ":00  ·  " + current.LabelCap);
            TimeAssignmentDef selected = TimeAssignmentSelector.selectedAssignment;
            if (mouse && selected != null && current != selected)
            {
                SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
                pawn.timetable.SetAssignment(i, selected);
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.TimeAssignments, KnowledgeAmount.SmallInteraction);
                if (selected == TimeAssignmentDefOf.Meditate)
                {
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.MeditationSchedule, KnowledgeAmount.Total);
                }
            }
        }
        NowLine(rect);
        if (TimeAssignmentSelector.selectedAssignment != null)
        {
            UIHighlighter.HighlightOpportunity(rect, "TimeAssignmentTableRow-If" + TimeAssignmentSelector.selectedAssignment.defName + "Selected");
        }
    }

    // every hour, the sixth ones brighter, and the hour it is now in the accent
    public static void Header(Rect rect)
    {
        float hour = rect.width / 24f;
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.LowerCenter;
        int now = Find.CurrentMap is { } map ? GenLocalDate.HourOfDay(map) : -1;
        for (int i = 0; i < 24; i++)
        {
            GUI.color = i == now ? Style.Accent : i % 6 == 0 ? Style.Text : Style.Dim;
            Widgets.Label(new Rect(rect.x + i * hour, rect.y, hour, rect.height + 3f), i.ToString());
        }
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
    }

    // the activities to paint with as pills, a dot of their color and the name, the chosen one lit
    public static void Selector(Rect rect)
    {
        List<TimeAssignmentDef> defs = new() { TimeAssignmentDefOf.Anything, TimeAssignmentDefOf.Work, TimeAssignmentDefOf.Joy, TimeAssignmentDefOf.Sleep };
        if (ModsConfig.RoyaltyActive)
        {
            defs.Add(TimeAssignmentDefOf.Meditate);
        }
        float x = rect.x, h = 28f, y = rect.y + 2f;
        Text.Font = GameFont.Small;
        foreach (TimeAssignmentDef def in defs)
        {
            string label = def.LabelCap;
            Rect pill = new Rect(x, y, Text.CalcSize(label).x + 34f, h);
            bool chosen = TimeAssignmentSelector.selectedAssignment == def;
            bool over = Mouse.IsOver(pill);
            Color color = ColorFor(def);
            GUI.color = chosen ? new Color(color.r, color.g, color.b, 0.45f) : over ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.06f);
            Widgets.DrawAtlas(pill, ModernWidgets.Fill);
            if (chosen)
            {
                GUI.color = new Color(color.r, color.g, color.b, 0.9f);
                Widgets.DrawAtlas(pill, ModernWidgets.Line);
            }
            GUI.color = color;
            GUI.DrawTexture(new Rect(pill.x + 10f, Mathf.Round(pill.center.y - 5f), 10f, 10f), Style.Circle);
            GUI.color = chosen || over ? Color.white : Style.Text;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(pill.x + 26f, pill.y, pill.width - 26f, pill.height), label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (Widgets.ButtonInvisible(pill))
            {
                TimeAssignmentSelector.selectedAssignment = def;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            if (!chosen)
            {
                UIHighlighter.HighlightOpportunity(pill, def.cachedHighlightNotSelectedTag);
            }
            x = pill.xMax + 6f;
        }
    }

    private static readonly AccessTools.FieldRef<bool> areaDragging = AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(AreaAllowedGUI), "dragging"));

    // an area as a pill: its color as a dot, its whole name, tinted with its color when it's the one allowed;
    // the game's dragging: press on one and drag down the column to set it for each pawn on the way
    public static void Area(Rect rect, Pawn p, Area area)
    {
        MouseoverSounds.DoRegion(rect);
        rect = rect.ContractedBy(1.5f);
        bool chosen = p.playerSettings.AreaRestrictionInPawnCurrentMap == area;
        bool over = Mouse.IsOver(rect);
        Color color = area?.Color ?? new Color(0.55f, 0.58f, 0.62f);
        GUI.color = chosen ? new Color(color.r, color.g, color.b, 0.45f) : over ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.05f);
        Widgets.DrawAtlas(rect, ModernWidgets.Fill);
        float dot = 8f;
        GUI.color = new Color(color.r, color.g, color.b, 1f);
        GUI.DrawTexture(new Rect(rect.x + 6f, Mathf.Round(rect.center.y - dot / 2f), dot, dot), Style.Circle);
        string text = AreaUtility.AreaAllowedLabel_Area(area);
        Rect label = new Rect(rect.x + 6f + dot + 4f, rect.y, rect.width - dot - 14f, rect.height);
        GUI.color = chosen || over ? Color.white : Style.Dim;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(label, text.Truncate(label.width));
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
        if (Event.current.rawType == EventType.MouseUp && Event.current.button == 0)
        {
            areaDragging() = false;
        }
        if (!Input.GetMouseButton(0) && Event.current.type != EventType.MouseDown)
        {
            areaDragging() = false;
        }
        if (over)
        {
            area?.MarkForDraw();
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                areaDragging() = true;
            }
            if (areaDragging() && p.playerSettings.AreaRestrictionInPawnCurrentMap != area)
            {
                p.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
            }
        }
        TooltipHandler.TipRegion(rect, text);
    }

    // ---- pawn tables: alternating rows instead of lines ----

    private static readonly AccessTools.FieldRef<PawnTable, List<Pawn>> cachedPawns = AccessTools.FieldRefAccess<PawnTable, List<Pawn>>("cachedPawns");
    private static readonly AccessTools.FieldRef<PawnTable, List<float>> cachedRowHeights = AccessTools.FieldRefAccess<PawnTable, List<float>>("cachedRowHeights");
    private static readonly AccessTools.FieldRef<PawnTable, Vector2> cachedSize = AccessTools.FieldRefAccess<PawnTable, Vector2>("cachedSize");

    // called right after the table starts its scroll view, so it's under everything in the rows
    public static void Rows(PawnTable table)
    {
        if (!On)
        {
            return;
        }
        List<float> heights = cachedRowHeights(table);
        float y = 0f, width = cachedSize(table).x - 16f;
        for (int i = 0; i < cachedPawns(table).Count && i < heights.Count; i++)
        {
            float h = (int)heights[i];
            if (i % 2 == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.035f);
                Widgets.DrawAtlas(new Rect(0f, y, width, h), ModernWidgets.Fill);
            }
            y += h;
        }
        GUI.color = Color.white;
    }

    // the lines between rows and around grouped cells, none in the modern windows
    public static Color Border(Color game) => On ? Color.clear : game;
}

[HarmonyPatch(typeof(PawnColumnWorker_Timetable), nameof(PawnColumnWorker_Timetable.DoCell))]
public static class Schedule_Cell
{
    public static bool Prefix(Rect rect, Pawn pawn)
    {
        if (!Schedule.On)
        {
            return true;
        }
        if (pawn.timetable != null && !pawn.IsSubhuman)
        {
            Schedule.Row(rect, pawn);
        }
        return false;
    }
}

[HarmonyPatch(typeof(PawnColumnWorker_Timetable), nameof(PawnColumnWorker_Timetable.DoHeader))]
public static class Schedule_Header
{
    public static bool Prefix(Rect rect)
    {
        if (!Schedule.On)
        {
            return true;
        }
        Schedule.Header(rect);
        return false;
    }
}

[HarmonyPatch(typeof(TimeAssignmentSelector), nameof(TimeAssignmentSelector.DrawTimeAssignmentSelectorGrid))]
public static class Schedule_Selector
{
    public static bool Prefix(Rect rect)
    {
        if (!Schedule.On)
        {
            return true;
        }
        Schedule.Selector(rect);
        return false;
    }
}

[HarmonyPatch(typeof(AreaAllowedGUI), "DoAreaSelector")]
public static class Schedule_Area
{
    public static bool Prefix(Rect rect, Pawn p, Area area)
    {
        if (!Schedule.On)
        {
            return true;
        }
        Schedule.Area(rect, p, area);
        return false;
    }
}

[HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI))]
public static class Schedule_Table
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        FieldInfo border = AccessTools.Field(typeof(PawnTable), "BorderColor");
        MethodInfo scroll = AccessTools.Method(typeof(Widgets), nameof(Widgets.BeginScrollView));
        bool rows = false;
        foreach (CodeInstruction ins in instructions)
        {
            if (ins.LoadsField(border))
            {
                yield return ins;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Schedule), nameof(Schedule.Border)));
                continue;
            }
            yield return ins;
            if (!rows && ins.Calls(scroll))
            {
                rows = true;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Schedule), nameof(Schedule.Rows)));
            }
        }
    }
}
