using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustDesktop;

public enum BarNames
{
    Always,
    Hover,
    Never
}

// the colonist bar at the top in the modern look: each colonist a rounded tile with the portrait, a thin mood bar
// along its bottom (marked where breaks start, red when one is close), a health bar when hurt, the game's status
// icons as small round badges on the top corner and the name in a pill under it. Map and caravan groups get a soft
// rounded panel. Where they are, clicks, double clicks, dragging and right click are all still the game's, so turning
// it off (or using another bar mod) leaves the game's bar
[StaticConstructorOnStartup]
public static class Colonists
{
    private static Settings S => JustDesktopMod.Settings;
    public static bool On => S.colonistBar;

    private static Texture2D _deadTex;

    private static Texture2D DeadTex => _deadTex ??= ContentFinder<Texture2D>.Get("UI/Misc/DeadColonist");
    private static readonly Func<ColonistBarColonistDrawer, int, Rect> groupRect = AccessTools.MethodDelegate<Func<ColonistBarColonistDrawer, int, Rect>>(AccessTools.Method(typeof(ColonistBarColonistDrawer), "GroupFrameRect"));
    private static readonly Action<ColonistBarColonistDrawer, Rect, Pawn> drawIcons = AccessTools.MethodDelegate<Action<ColonistBarColonistDrawer, Rect, Pawn>>(AccessTools.Method(typeof(ColonistBarColonistDrawer), "DrawIcons"));

    private static readonly Color MoodColor = new(0.93f, 0.77f, 0.33f);
    private static readonly Color MinorColor = new(1f, 0.6f, 0.25f);
    private static readonly Color MajorColor = new(0.92f, 0.32f, 0.3f);
    private static readonly Color HealthColor = new(0.38f, 0.75f, 0.44f);

    // the game's icons for this colonist, caught instead of drawn
    public static bool Collecting;
    public static readonly List<(Texture2D tex, string tip, Color color)> icons = new();

    private static void Fill(Rect r, Color c)
    {
        GUI.color = c;
        Widgets.DrawAtlas(r, ModernWidgets.Fill);
    }

    private static void Outline(Rect r, Color c)
    {
        GUI.color = c;
        Widgets.DrawAtlas(r, ModernWidgets.Line);
    }

    public static void Draw(ColonistBarColonistDrawer drawer, Rect rect, Pawn colonist, Map pawnMap, bool highlight, bool reordering)
    {
        ColonistBar bar = Find.ColonistBar;
        float scale = bar.Scale;
        float alpha = bar.GetEntryRectAlpha(rect);
        // colonists on another map or away are faded, like the game does
        if (pawnMap == null ? !WorldRendererUtility.WorldSelected : pawnMap != Find.CurrentMap || WorldRendererUtility.WorldSelected)
        {
            alpha = Mathf.Min(alpha, 0.4f);
        }
        if (reordering)
        {
            alpha *= 0.5f;
        }
        bool moodShown = Prefs.VisibleMood && colonist.needs?.mood != null && colonist.mindState.mentalBreaker.CanDoRandomMentalBreaks && !colonist.Dead && !colonist.Downed;
        MoodThreshold threshold = MoodThresholdExtensions.CurrentMoodThresholdFor(colonist);
        Color warn = threshold switch { MoodThreshold.Minor => MinorColor, MoodThreshold.None => MoodColor, _ => MajorColor };
        bool selected = colonist.Dead ? Find.Selector.SelectedObjects.Contains(colonist.Corpse) : Find.Selector.SelectedObjects.Contains(colonist);
        selected = selected && !WorldRendererUtility.WorldSelected
            || WorldRendererUtility.WorldSelected && colonist.IsCaravanMember() && Find.WorldSelector.IsSelected(colonist.GetCaravan());

        // a glow around it when a break is close
        if (moodShown && threshold != MoodThreshold.None && alpha >= 1f)
        {
            float grow = (threshold == MoodThreshold.Minor ? 2f : 3f) * scale;
            Fill(rect.ExpandedBy(grow), new Color(warn.r, warn.g, warn.b, 0.55f));
        }
        Color back = S.windowColor;
        back.a = 0.88f * alpha;
        Fill(rect, back);
        if (colonist.Drafted)
        {
            Fill(rect, Style.Faded(0.28f * alpha));
        }
        if (highlight)
        {
            Fill(rect, new Color(1f, 1f, 1f, 0.1f * alpha));
        }

        // the edge, behind the pawn so the head goes over it: selected in the accent, downed in red, drafted a bit of the accent
        Color edge = selected ? Style.Accent : colonist.Downed && !colonist.Dead ? MajorColor : colonist.Drafted ? Style.Faded(0.6f) : Style.Border;
        Outline(rect, new Color(edge.r, edge.g, edge.b, edge.a * alpha));
        if (selected)
        {
            Outline(rect.ContractedBy(1f), Style.Faded(0.6f * alpha));
        }

        GUI.color = new Color(1f, 1f, 1f, alpha);
        // zoomed on the face: the same picture as the game's, over the top of the tile, with the camera closer and
        // moved so the face goes from where the game has it to the middle of the tile as the zoom grows
        float zoom = S.colonistBarZoom;
        Vector3 offset = ColonistBarColonistDrawer.PawnTextureCameraOffset;
        if (zoom > 1.001f)
        {
            Vector2 size = ColonistBarColonistDrawer.PawnTextureSize;
            // the picture covers 2 / zoom cells top to bottom, centered on the camera
            float perCell = size.y * ColonistBarColonistDrawer.PawnTextureCameraZoom / 2f;
            float face = colonist.Drawer.renderer.BaseHeadOffsetAt(Rot4.South).z + 0.04f;
            float gameY = size.y / 2f - (face - offset.z) * perCell;
            float middle = size.y - ColonistBar.BaseSize.y / 2f;
            float faceY = Mathf.Lerp(gameY, middle, Mathf.InverseLerp(1f, 2.5f, zoom));
            offset = new Vector3(0f, 0f, face - (size.y / 2f - faceY) / (perCell * zoom));
        }
        GUI.DrawTexture(drawer.GetPawnTextureRect(rect.position), PortraitsCache.Get(colonist, ColonistBarColonistDrawer.PawnTextureSize, Rot4.South, offset, ColonistBarColonistDrawer.PawnTextureCameraZoom * zoom));
        if (colonist.Dead)
        {
            GUI.DrawTexture(rect, DeadTex);
        }

        // the bars along the bottom, inside the tile, or under it going down
        bool below = S.colonistBarBarsBelow;
        float thick = Mathf.Max(2f, Mathf.Round((below ? 4f : 3f) * scale));
        float inset = below ? 1f : Mathf.Round(4f * scale);
        float gap = Mathf.Max(1f, Mathf.Round(2f * scale)) + (S.colonistBarBarBorder ? 1f : 0f);
        float y = below ? rect.yMax + gap + (S.colonistBarBarBorder ? 1f : 0f) : rect.yMax - inset - thick;
        float step = below ? thick + gap : -(thick + gap);
        if (S.colonistBarMood && colonist.needs?.mood != null && !colonist.Dead)
        {
            Thin(new Rect(rect.x + inset, y, rect.width - inset * 2f, thick), colonist.needs.mood.CurLevelPercentage, moodShown ? warn : Style.Dim, alpha, moodShown ? colonist.mindState.mentalBreaker : null);
            y += step;
        }
        float health = colonist.health.summaryHealth.SummaryHealthPercent;
        if (S.colonistBarHealth && !colonist.Dead && health < 0.999f)
        {
            Thin(new Rect(rect.x + inset, y, rect.width - inset * 2f, thick), health, Color.Lerp(MajorColor, HealthColor, health), alpha, null);
            y += step;
        }
        // the name goes under the bars when they're under the tile
        float nameTop = below ? Mathf.Max(rect.yMax, y - gap) : rect.yMax;

        if (S.colonistBarIcons)
        {
            Badges(drawer, rect, colonist, alpha, scale);
        }
        Name(rect, nameTop, colonist, alpha, scale, highlight);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static void Thin(Rect r, float value, Color color, float alpha, Verse.AI.MentalBreaker breaker)
    {
        // a black edge around it makes it easier to see over the map
        if (S.colonistBarBarBorder)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.9f * alpha);
            GUI.DrawTexture(r.ExpandedBy(1f), BaseContent.WhiteTex);
        }
        GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
        GUI.DrawTexture(r, BaseContent.WhiteTex);
        GUI.color = new Color(color.r, color.g, color.b, alpha);
        GUI.DrawTexture(new Rect(r.x, r.y, Mathf.Round(r.width * Mathf.Clamp01(value)), r.height), BaseContent.WhiteTex);
        if (breaker != null)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f * alpha);
            foreach (float at in new[] { breaker.BreakThresholdExtreme, breaker.BreakThresholdMajor, breaker.BreakThresholdMinor })
            {
                GUI.DrawTexture(new Rect(r.x + Mathf.Round(r.width * at), r.y, 1f, r.height), BaseContent.WhiteTex);
            }
        }
    }

    // sleeping, idle, fighting, burning, inspired, role or faction...: what the game would draw, on small round
    // badges going down the tile's right edge
    private static void Badges(ColonistBarColonistDrawer drawer, Rect rect, Pawn colonist, float alpha, float scale)
    {
        icons.Clear();
        Collecting = true;
        try
        {
            drawIcons(drawer, rect, colonist);
        }
        finally
        {
            Collecting = false;
        }
        float size = Mathf.Round(16f * scale);
        float x = rect.xMax - size + Mathf.Round(4f * scale), top = rect.y - Mathf.Round(4f * scale);
        for (int i = 0; i < icons.Count; i++)
        {
            Rect r = new Rect(x, top + i * (size + 1f), size, size);
            if (r.yMax > rect.yMax)
            {
                break;
            }
            GUI.color = new Color(0.04f, 0.05f, 0.06f, 0.9f * alpha);
            GUI.DrawTexture(r, Style.Circle);
            Color c = icons[i].color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * alpha);
            GUI.DrawTexture(r.ContractedBy(Mathf.Round(2f * scale)), icons[i].tex);
            if (icons[i].tip != null)
            {
                TooltipHandler.TipRegion(r, icons[i].tip);
            }
        }
    }

    private static void Name(Rect rect, float top, Pawn colonist, float alpha, float scale, bool hover)
    {
        if (S.colonistBarNames == BarNames.Never || S.colonistBarNames == BarNames.Hover && !hover && !Mouse.IsOver(rect))
        {
            return;
        }
        Text.Font = GameFont.Tiny;
        float max = rect.width + Find.ColonistBar.SpaceBetweenColonistsHorizontal - 4f;
        string name = colonist.LabelShortCap;
        Text.WordWrap = false;
        float w = Mathf.Min(max, Text.CalcSize(name).x + 10f);
        if (w < 12f)
        {
            Text.WordWrap = true;
            return;
        }
        Rect pill = new Rect(Mathf.Round(rect.center.x - w / 2f), top + Mathf.Round(2f * scale), w, 16f);
        Fill(pill, new Color(0.04f, 0.05f, 0.06f, 0.75f * alpha));
        Color c = PawnNameColorUtility.PawnNameColorOf(colonist);
        GUI.color = new Color(c.r, c.g, c.b, alpha);
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(pill, name.Truncate(w - 6f));
        Text.Anchor = TextAnchor.UpperLeft;
        Text.WordWrap = true;
    }

    public static void Group(ColonistBarColonistDrawer drawer, int group)
    {
        Rect r = groupRect(drawer, group);
        Map map = Find.ColonistBar.Entries.Find(x => x.group == group).map;
        bool here = map == null ? WorldRendererUtility.WorldSelected : map == Find.CurrentMap && !WorldRendererUtility.WorldSelected;
        // the colors from the options, one for the map being looked at and one for the rest
        Color c = here ? S.colonistBarGroupHere : S.colonistBarGroupAway;
        Fill(r, c);
        Outline(r, new Color(c.r, c.g, c.b, Mathf.Min(1f, c.a * 1.6f)));
        GUI.color = Color.white;
    }

    // the biggest the bar gets; it still shrinks to fit many colonists
    public static float MaxScale() => On ? S.colonistBarSize : 1f;

    public static void Recache()
    {
        if (Current.ProgramState == ProgramState.Playing)
        {
            Find.ColonistBar?.MarkColonistsDirty();
        }
    }
}

[HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
public static class Colonists_Draw
{
    public static bool Prefix(ColonistBarColonistDrawer __instance, Rect rect, Pawn colonist, Map pawnMap, bool highlight, bool reordering)
    {
        if (!Colonists.On)
        {
            return true;
        }
        Colonists.Draw(__instance, rect, colonist, pawnMap, highlight, reordering);
        return false;
    }
}

[HarmonyPatch(typeof(ColonistBarColonistDrawer), "DrawIcon")]
public static class Colonists_Icon
{
    public static bool Prefix(Texture2D icon, ref Vector2 pos, float iconSize, string tooltip)
    {
        if (!Colonists.Collecting)
        {
            return true;
        }
        Colonists.icons.Add((icon, tooltip, GUI.color));
        return false;
    }
}

[HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawGroupFrame))]
public static class Colonists_Group
{
    public static bool Prefix(ColonistBarColonistDrawer __instance, int group)
    {
        if (!Colonists.On)
        {
            return true;
        }
        Colonists.Group(__instance, group);
        return false;
    }
}

// the size from the options as where the game starts looking for the scale that fits
[HarmonyPatch(typeof(ColonistBarDrawLocsFinder), "FindBestScale")]
public static class Colonists_Scale
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        bool done = false;
        foreach (CodeInstruction ins in instructions)
        {
            if (!done && ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && f == 1f)
            {
                done = true;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Colonists), nameof(Colonists.MaxScale))).MoveLabelsFrom(ins);
                continue;
            }
            yield return ins;
        }
        if (!done)
        {
            Log.Warning("[JustDesktop] couldn't find where the colonist bar picks its size, the size option does nothing");
        }
    }
}
