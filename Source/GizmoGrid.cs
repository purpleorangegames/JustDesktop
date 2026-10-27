using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the selection's gizmos as a grid of small squares next to the card: three high, filled bottom to top, then the
// next column. The game still lays them out and handles them (groups, hotkeys, right click menus); only where each
// one is drawn changes, and buttons draw in the game's small size, with the label on hover like the dock's names.
// Gizmos that aren't buttons (shield, psyfocus, bandwidth...) keep their size and get a column of their own.
public static class GizmoGrid
{
    // three high, or more when the buttons wouldn't fit across in three; for a drafted pawn (see Pane.DraftedRow) one
    // row in the middle, or as many as it takes to stay clear of the card and the bars at its side
    private static int Rows = 3;
    private static bool oneRow;
    public static int DraftedRows = 1;
    // how wide the row was, to center the next frame's
    public static float RowWidth = 400f;
    private static float rowRight;
    // how many buttons there were last time, and how wide the rest of the grid was (bars, psyfocus...), to pick the rows
    private static int cells, lastCells;
    private static float othersWidth, lastOthersWidth;
    // the drafted pawn's bars (hemogen, deathrest, psyfocus...) stacked at the right of the card, and how far right they reach
    public static float BarsRight;
    private static float barsX, barsTop, barsRight, barsColumn;
    // the height of the drafted row, for the speed card above it
    public static float DraftedHeight => DraftedRows * Size + (DraftedRows - 1) * Gap;
    public static float Size => SizeOverride ?? Mathf.Round(58f * JustDesktopMod.Settings.paneSize);

    // another layout can say where each square goes and how big (the Architect's side panels), and to put the names
    // under them instead of over the one under the mouse
    public static Func<Rect> CellProvider;
    public static float? SizeOverride;
    public static bool LabelsUnder;
    private const float Gap = 5f;

    // only for the selection on the map, while our card shows; Architect's buttons stay as they are
    public static bool Active;
    // a button being drawn in its square: Command draws itself small and says it's that wide
    public static bool Small;

    private static float startX, bottom, columnX, top;
    private static int row;
    // the column of small bar cards (hemogen, deathrest...): where it is and how far up it's filled
    private static float compactX, compactTop, compactFloor;
    // or on the left of the card, going up from its bottom
    private static float leftTop;
    private static Rect last;
    private static Command hovered;
    private static Rect hoveredRect;

    private static readonly AccessTools.FieldRef<float> heightDrawn = AccessTools.StaticFieldRefAccess<float>(AccessTools.Field(typeof(GizmoGridDrawer), "heightDrawn"));

    public static void Begin(float x)
    {
        startX = x;
        columnX = x;
        row = 0;
        bottom = UI.screenHeight - Taskbar.Reserved;
        top = bottom;
        hovered = null;
        last = Rect.zero;
        compactX = -1f;
        leftTop = -1f;
        oneRow = Pane.DraftedRow;
        rowRight = x;
        cells = 0;
        othersWidth = 0f;
        barsX = -1f;
        barsRight = 0f;
        Rows = PickRows(x);
        if (oneRow)
        {
            DraftedRows = Rows;
        }
    }

    // as few rows as let last time's buttons fit across: drafted, between the card's side and the same distance from
    // the right edge; otherwise from the card to the right edge
    private static int PickRows(float x)
    {
        if (CellProvider != null)
        {
            return 3;
        }
        float room;
        if (oneRow)
        {
            float left = Mathf.Max(BarsRight, Pane.Current is { } t ? Pane.Place(t).xMax : 0f) + 8f;
            room = UI.screenWidth - 2f * left;
        }
        else
        {
            room = UI.screenWidth - 147f - x - lastOthersWidth;
        }
        int columns = Mathf.Max(1, Mathf.FloorToInt((room + Gap) / (Size + Gap)));
        int rows = Mathf.CeilToInt(lastCells / (float)columns);
        return Mathf.Max(oneRow ? 1 : 3, rows);
    }

    // the next square, going up the column; a full column or no room left starts the next one
    private static Rect NextCell()
    {
        if (row == Rows)
        {
            NextColumn(Size);
        }
        Rect r = new Rect(columnX, bottom - (row + 1) * Size - row * Gap, Size, Size);
        row++;
        cells++;
        return r;
    }

    private static void NextColumn(float width)
    {
        columnX += width + Gap;
        row = 0;
        // past the right edge: another band of rows above
        if (columnX + Size > UI.screenWidth - 147f)
        {
            columnX = startX;
            bottom -= Rows * (Size + Gap);
        }
    }

    // in place of Gizmo.GizmoOnGUI in the game's loop, the position it worked out is replaced by ours
    public static GizmoResult Draw(Gizmo gizmo, Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
    {
        if (!Active)
        {
            return gizmo.GizmoOnGUI(topLeft, maxWidth, parms);
        }
        GizmoResult result;
        // the room's stats go above the card, the grid keeps its place; the game then keeps clicks from going
        // through where it says the gizmo was, so that has to be the room's rect and not the last square's
        if (gizmo is Gizmo_RoomStats room && Pane.Current is { } selected)
        {
            return RoomCard.Draw(room, Pane.Place(selected), out last);
        }
        if (gizmo is Command command)
        {
            last = CellProvider != null ? CellProvider() : NextCell();
            // its own GizmoOnGUI still runs (toggles, abilities and mods add to it), inside the square
            Widgets.BeginGroup(last);
            Small = true;
            try
            {
                result = command.GizmoOnGUI(Vector2.zero, maxWidth, parms);
            }
            finally
            {
                Small = false;
                Widgets.EndGroup();
            }
            if (Mouse.IsOver(last))
            {
                hovered = command;
                hoveredRect = last;
            }
            if (LabelsUnder && !command.LabelCap.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                GUI.color = Style.Text;
                Widgets.Label(new Rect(last.x - 6f, last.yMax + 1f, last.width + 12f, LabelHeight), command.LabelCap);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }
        else if (oneRow && Pane.Current is { } drafted)
        {
            // drafted: the bars, psyfocus and the like go up at the right of the card, a column as high as the
            // card, then the next one
            bool compact = Compact.Is(gizmo);
            float w = compact ? Compact.Width : gizmo.GetWidth(maxWidth);
            float h = compact ? Compact.HeightOf(gizmo) : 75f;
            Rect card = Pane.Place(drafted);
            if (barsX < 0f)
            {
                barsX = card.xMax + Gap;
                barsTop = card.yMax;
                barsColumn = 0f;
            }
            else if (barsTop - h < card.y && barsTop < card.yMax)
            {
                barsX += barsColumn + Gap;
                barsTop = card.yMax;
                barsColumn = 0f;
            }
            last = new Rect(barsX, barsTop - h, w, h);
            barsTop = last.y - Gap;
            barsColumn = Mathf.Max(barsColumn, w);
            barsRight = Mathf.Max(barsRight, last.xMax);
            Compact.Drawing = compact;
            try
            {
                result = gizmo.GizmoOnGUI(last.position, maxWidth, parms);
            }
            finally
            {
                Compact.Drawing = false;
            }
            return result;
        }
        else if (gizmo is RimWorld.Planet.Gizmo_CaravanInfo && CaravanInfoLeft(gizmo.GetWidth(maxWidth), out Rect left))
        {
            // the caravan's mass, speed and food at the left of the world card, like the bars on the map
            last = left;
            return gizmo.GizmoOnGUI(last.position, maxWidth, parms);
        }
        else if (Compact.Is(gizmo) && Compact.LeftOf(out float leftX))
        {
            // at the left of the card, out of the gizmos' way, while no tab panel is open there
            if (leftTop < 0f)
            {
                leftTop = Pane.Place(Pane.Current).yMax;
            }
            last = new Rect(leftX, leftTop - Compact.HeightOf(gizmo), Compact.Width, Compact.HeightOf(gizmo));
            leftTop = last.y - Gap;
            Compact.Drawing = true;
            try
            {
                result = gizmo.GizmoOnGUI(last.position, maxWidth, parms);
            }
            finally
            {
                Compact.Drawing = false;
            }
        }
        else if (Compact.Is(gizmo))
        {
            // stacked going up in their own column, as high as three squares
            float h = Compact.HeightOf(gizmo);
            if (compactX < 0f || compactTop - h < compactFloor)
            {
                if (row > 0)
                {
                    NextColumn(Size);
                }
                compactX = columnX;
                compactTop = bottom;
                compactFloor = bottom - Rows * Size - (Rows - 1) * Gap;
                othersWidth += Compact.Width + Gap;
                NextColumn(Compact.Width);
            }
            last = new Rect(compactX, compactTop - h, Compact.Width, h);
            compactTop = last.y - Gap;
            Compact.Drawing = true;
            try
            {
                result = gizmo.GizmoOnGUI(last.position, maxWidth, parms);
            }
            finally
            {
                Compact.Drawing = false;
            }
        }
        else
        {
            if (row > 0)
            {
                NextColumn(Size);
            }
            float width = gizmo.GetWidth(maxWidth);
            othersWidth += width + Gap;
            last = new Rect(columnX, bottom - 75f, width, 75f);
            result = gizmo.GizmoOnGUI(last.position, maxWidth, parms);
            NextColumn(width);
        }
        rowRight = Mathf.Max(rowRight, last.xMax);
        // how high the gizmos reach, for what the game puts above them; it sets this from its own positions first
        top = Mathf.Min(top, last.y);
        heightDrawn() = UI.screenHeight - top;
        return result;
    }

    // at the left of the world card when the bars go at the left of the map's, no tab panel is open there and it fits
    private static bool CaravanInfoLeft(float width, out Rect rect)
    {
        rect = default;
        if (JustDesktopMod.Settings.compactRight || !WorldCard.Shown || Find.World.UI.inspectPane.OpenTabType != null)
        {
            return false;
        }
        Rect card = WorldCard.Rect;
        rect = new Rect(card.x - Gap - width, card.yMax - 75f, width, 75f);
        return rect.x >= 0f;
    }

    // the game's second loop, for buttons it already shrank
    public static GizmoResult DrawShrunk(Command command, Vector2 topLeft, float size, GizmoRenderParms parms)
    {
        return Active ? Draw(command, topLeft, 0f, parms) : command.GizmoOnGUIShrunk(topLeft, size, parms);
    }

    // clicks are kept from reaching the map where the gizmo really is
    public static void Absorb(Rect rect)
    {
        GenUI.AbsorbClicksInRect(Active ? last.ExpandedBy(Gap / 2f) : rect);
    }

    // the label of the button under the mouse, over everything else
    // two lines of small text under a square
    public const float LabelHeight = 26f;

    // the world map's own buttons (view orbit, form caravan, jump to...) with nothing selected: no card to sit next
    // to, so in the game's corner or centered above the dock, by how wide they were last time
    public static bool Loose;
    private static float looseWidth = 200f;
    private static Rect looseRect;
    private static int looseFrame = -10;
    // where they were last frame, for the speed card above the dock to stay clear of them
    public static Rect? LooseRect => Time.frameCount - looseFrame <= 2 ? looseRect : null;
    public static float LooseStart => JustDesktopMod.Settings.panePlace == PanePlace.GamePlace ? 14f : Mathf.Round((UI.screenWidth - looseWidth) / 2f);

    public static void End()
    {
        if (Active && Loose && rowRight > startX)
        {
            looseWidth = rowRight - startX;
            looseRect = new Rect(startX, top, looseWidth, UI.screenHeight - Taskbar.Reserved - top);
            looseFrame = Time.frameCount;
        }
        // the game draws nothing on the layout event, that one would measure an empty row
        if (Active && oneRow && rowRight > startX)
        {
            RowWidth = rowRight - startX;
        }
        if (Active && CellProvider == null && (cells > 0 || barsRight > 0f))
        {
            lastCells = cells;
            lastOthersWidth = othersWidth;
            BarsRight = barsRight;
        }
        if (Active && !LabelsUnder && hovered != null && !hovered.LabelCap.NullOrEmpty())
        {
            Style.Label(hovered.LabelCap, hoveredRect);
        }
        Active = false;
    }
}

// only the selection's gizmos, and only with our card
[HarmonyPatch(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGridFor))]
public static class GizmoGrid_For
{
    public static void Prefix()
    {
        GizmoGrid.Loose = JustDesktopMod.Settings.gizmoGrid && JustDesktopMod.Settings.paneOn && Loading.Ready && !Pane.Shown && !WorldCard.Shown && RimWorld.Planet.WorldRendererUtility.WorldSelected;
        GizmoGrid.Active = JustDesktopMod.Settings.gizmoGrid && (Pane.Shown || WorldCard.Shown || GizmoGrid.Loose);
    }

    public static void Postfix()
    {
        GizmoGrid.Active = false;
    }
}

[HarmonyPatch(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid))]
public static class GizmoGrid_Draw
{
    public static void Prefix(float startX)
    {
        if (GizmoGrid.Active)
        {
            GizmoGrid.Begin(GizmoGrid.Loose ? GizmoGrid.LooseStart : startX);
        }
    }

    public static void Postfix()
    {
        GizmoGrid.End();
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo onGui = AccessTools.Method(typeof(Gizmo), nameof(Gizmo.GizmoOnGUI));
        MethodInfo shrunk = AccessTools.Method(typeof(Command), nameof(Command.GizmoOnGUIShrunk));
        MethodInfo absorb = AccessTools.Method(typeof(GenUI), nameof(GenUI.AbsorbClicksInRect));
        instructions = Gizmos.Swap(instructions, onGui, AccessTools.Method(typeof(GizmoGrid), nameof(GizmoGrid.Draw)));
        instructions = Gizmos.Swap(instructions, shrunk, AccessTools.Method(typeof(GizmoGrid), nameof(GizmoGrid.DrawShrunk)));
        return Gizmos.Swap(instructions, absorb, AccessTools.Method(typeof(GizmoGrid), nameof(GizmoGrid.Absorb)));
    }
}

// a button in its square draws in the game's small size: picture and hotkey, no label
[HarmonyPatch(typeof(Command), nameof(Command.GizmoOnGUI))]
public static class GizmoGrid_Small
{
    public static bool Prefix(Command __instance, Vector2 topLeft, GizmoRenderParms parms, ref GizmoResult __result)
    {
        if (!GizmoGrid.Small)
        {
            return true;
        }
        __result = __instance.GizmoOnGUIShrunk(topLeft, __instance.GetWidth(0f), parms);
        return false;
    }
}

// and is as wide as its square, so what a toggle or ability adds in its corner lands inside it
[HarmonyPatch(typeof(Command), nameof(Command.GetWidth))]
public static class GizmoGrid_Width
{
    public static void Postfix(ref float __result)
    {
        if (GizmoGrid.Small)
        {
            __result = GizmoGrid.Size;
        }
    }
}
