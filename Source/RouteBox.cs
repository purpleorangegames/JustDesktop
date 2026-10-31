using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace JustDesktop;

// the route planner's box (route time, the caravan speed note, the keys) as a card at the left of the world card,
// where the game's centered box covered the card and its buttons. The Accept button goes inside it, at the bottom
public static class RouteBox
{
    private static Settings S => JustDesktopMod.Settings;
    private static float Scale => S.paneSize;
    private static float Width => Mathf.Round(340f * Scale);

    // the same id the game uses for its box
    private const int WindowId = 1373514241;

    private static readonly AccessTools.FieldRef<WorldRoutePlanner, Dialog_FormCaravan> formDialog = AccessTools.FieldRefAccess<WorldRoutePlanner, Dialog_FormCaravan>("currentFormCaravanDialog");
    private static readonly AccessTools.FieldRef<WorldRoutePlanner, bool> cantRemoveFirst = AccessTools.FieldRefAccess<WorldRoutePlanner, bool>("cantRemoveFirstWaypoint");
    private static readonly Func<WorldRoutePlanner, CaravanTicksPerMoveUtility.CaravanInfo?> caravanInfo = AccessTools.MethodDelegate<Func<WorldRoutePlanner, CaravanTicksPerMoveUtility.CaravanInfo?>>(AccessTools.PropertyGetter(typeof(WorldRoutePlanner), "CaravanInfo"));
    private static readonly Func<WorldRoutePlanner, Caravan> firstCaravan = AccessTools.MethodDelegate<Func<WorldRoutePlanner, Caravan>>(AccessTools.PropertyGetter(typeof(WorldRoutePlanner), "CaravanAtTheFirstWaypoint"));

    public static bool On => S.paneOn && Loading.Ready && Current.ProgramState == ProgramState.Playing;

    private static readonly List<(string text, Color color)> lines = new();

    // what the game's box says, in the same order
    private static void Lines(WorldRoutePlanner planner)
    {
        lines.Clear();
        List<RoutePlannerWaypoint> waypoints = planner.waypoints;
        if (waypoints.Count >= 2)
        {
            lines.Add(("RoutePlannerEstTimeToFinalDest".Translate(planner.GetTicksToWaypoint(waypoints.Count - 1).ToStringTicksToDays("0.#")), Style.Text));
        }
        else
        {
            lines.Add(((cantRemoveFirst(planner) ? "RoutePlannerAddOneOrMoreWaypoints" : "RoutePlannerAddTwoOrMoreWaypoints").Translate(), Style.Text));
        }
        CaravanTicksPerMoveUtility.CaravanInfo? info = caravanInfo(planner);
        Dialog_FormCaravan dialog = formDialog(planner);
        if (!info.HasValue || !info.Value.pawns.Any())
        {
            lines.Add(("RoutePlannerUsingAverageTicksPerMoveWarning".Translate(), new Color(0.8f, 0.6f, 0.6f)));
        }
        else if (dialog == null && firstCaravan(planner) is { } caravan)
        {
            lines.Add(("RoutePlannerUsingTicksPerMoveOfCaravan".Translate(caravan.LabelCap), Style.Dim));
        }
        bool deck = SteamDeck.IsSteamDeckInNonKeyboardMode;
        lines.Add(((deck ? "RoutePlannerPressRMBToAddAndRemoveWaypointsController" : "RoutePlannerPressRMBToAddAndRemoveWaypoints").Translate(), Style.Dim));
        string escape = dialog != null
            ? (deck ? "RoutePlannerPressEscapeToReturnToCaravanFormationDialogController".Translate().Resolve().AdjustedForKeys() : "RoutePlannerPressEscapeToReturnToCaravanFormationDialog".Translate().Resolve())
            : (deck ? "RoutePlannerPressEscapeToExitController".Translate().Resolve().AdjustedForKeys() : "RoutePlannerPressEscapeToExit".Translate().Resolve());
        lines.Add((escape, Style.Dim));
    }

    private static bool CanAccept(WorldRoutePlanner planner) => formDialog(planner) != null && planner.waypoints.Count >= 2;

    private static float ButtonHeight => Mathf.Round(30f * Scale);

    // left of the world card, above the caravan's info when that sits there too; centered above the dock when
    // nothing is selected, and above the card when there's no room at its left
    private static Rect Place(Vector2 size)
    {
        float bottom = UI.screenHeight - Taskbar.Reserved;
        float x = Mathf.Round((UI.screenWidth - size.x) / 2f);
        if (WorldCard.Shown)
        {
            Rect card = WorldCard.Rect;
            x = card.x - GizmoGrid.Gap - size.x;
            bottom = card.yMax;
            if (!S.compactRight && Find.WorldSelector.SingleSelectedObject is Caravan && Find.World.UI.inspectPane.OpenTabType == null)
            {
                bottom -= 75f + GizmoGrid.Gap;
            }
            if (x < 0f)
            {
                x = card.x;
                bottom = card.y - GizmoGrid.Gap;
            }
        }
        return new Rect(x, bottom - size.y, size.x, size.y);
    }

    public static void Draw(WorldRoutePlanner planner)
    {
        Lines(planner);
        float width = Width;
        float textWidth = width - Pane.Pad * 2f;
        Text.Font = Pane.SmallFont;
        float height = Pane.Pad * 2f;
        foreach ((string text, Color _) in lines)
        {
            height += Mathf.Ceil(Text.CalcHeight(text, textWidth));
        }
        Text.Font = GameFont.Small;
        bool accept = CanAccept(planner);
        if (accept)
        {
            height += 8f * Scale + ButtonHeight;
        }
        Rect rect = Place(new Vector2(width, Mathf.Round(height)));
        Find.WindowStack.ImmediateWindow(WindowId, rect, WindowLayer.Dialog, () =>
        {
            if (!planner.Active)
            {
                return;
            }
            Rect area = rect.AtZero();
            Pane.CardBack(area);
            Rect inner = area.ContractedBy(Pane.Pad);
            float y = inner.y;
            Text.Font = Pane.SmallFont;
            foreach ((string text, Color color) in lines)
            {
                float h = Mathf.Ceil(Text.CalcHeight(text, inner.width));
                GUI.color = color;
                Widgets.Label(new Rect(inner.x, y, inner.width, h), text);
                y += h;
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            if (accept && CanAccept(planner))
            {
                Rect button = new Rect(inner.xMax - Mathf.Round(140f * Scale), inner.yMax - ButtonHeight, Mathf.Round(140f * Scale), ButtonHeight);
                if (Widgets.ButtonText(button, "Accept".Translate()))
                {
                    // what the game's Accept button does
                    Dialog_FormCaravan dialog = formDialog(planner);
                    Find.WindowStack.Add(dialog);
                    dialog.Notify_ChoseRoute(planner.waypoints[1].Tile);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    planner.Stop();
                }
            }
        }, doBackground: false, absorbInputAroundWindow: false, 0f);
    }
}

[HarmonyPatch(typeof(WorldRoutePlanner), "DoRouteDetailsBox")]
public static class RouteBox_Details
{
    public static bool Prefix(WorldRoutePlanner __instance)
    {
        if (!RouteBox.On)
        {
            return true;
        }
        RouteBox.Draw(__instance);
        return false;
    }
}

// the Accept button is in our box
[HarmonyPatch(typeof(WorldRoutePlanner), "DoChooseRouteButton")]
public static class RouteBox_Accept
{
    public static bool Prefix(ref bool __result)
    {
        if (!RouteBox.On)
        {
            return true;
        }
        __result = false;
        return false;
    }
}
