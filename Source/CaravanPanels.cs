using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// a caravan's Needs, Health and Social tabs open one pawn's details beside the list; the game draws that panel in
// its old look, here it's our Needs, Health and Social panels on a card, bottom aligned with the list and left of it
// when there's room
public static class CaravanPanels
{
    private static readonly Func<InspectTabBase, Rect> tabRect = AccessTools.MethodDelegate<Func<InspectTabBase, Rect>>(AccessTools.PropertyGetter(typeof(InspectTabBase), "TabRect"));

    public static readonly AccessTools.FieldRef<WITab_Caravan_Needs, Pawn> NeedsPawn = AccessTools.FieldRefAccess<WITab_Caravan_Needs, Pawn>("specificNeedsTabForPawn");
    public static readonly AccessTools.FieldRef<WITab_Caravan_Health, Pawn> HealthPawn = AccessTools.FieldRefAccess<WITab_Caravan_Health, Pawn>("specificHealthTabForPawn");
    public static readonly AccessTools.FieldRef<WITab_Caravan_Social, Pawn> SocialPawn = AccessTools.FieldRefAccess<WITab_Caravan_Social, Pawn>("specificSocialTabForPawn");

    private static readonly AccessTools.FieldRef<InspectTabBase, Vector2> size = AccessTools.FieldRefAccess<InspectTabBase, Vector2>("size");

    // the pawn lists are as tall as their rows, up to the game's height, instead of always that tall. The game measures
    // the rows while drawing them (scrollViewHeight), so the first frame after opening still has the full height
    public static void FitList(WITab tab, float rowsHeight, float chrome)
    {
        if (!TabPanels.On(tab) || rowsHeight <= 0f)
        {
            return;
        }
        ref Vector2 v = ref size(tab);
        v.y = Mathf.Min(v.y, Mathf.Max(120f, rowsHeight + chrome));
    }

    // false = let the game draw it
    public static bool Draw(WITab tab, ref Pawn pawn, int id, Func<Pawn, Vector2> size, Action<Rect, Pawn> draw)
    {
        if (!TabPanels.On(tab))
        {
            return true;
        }
        if (pawn != null && (pawn.Destroyed || Find.WorldSelector.SingleSelectedObject is not Caravan c || !c.ContainsPawn(pawn)))
        {
            pawn = null;
        }
        if (pawn == null)
        {
            return false;
        }
        Pawn p = pawn;
        Rect list = tabRect(tab);
        Vector2 v = size(p);
        float h = Mathf.Min(v.y, list.yMax - 4f);
        // left of the list when it fits on the screen, else right of it
        float x = list.x - 4f - v.x >= 0f ? list.x - 4f - v.x : list.xMax + 4f;
        Rect rect = new Rect(x, list.yMax - h, v.x, h);
        bool close = false;
        Find.WindowStack.ImmediateWindow(id, rect, WindowLayer.GameUI, () =>
        {
            if (p.DestroyedOrNull())
            {
                return;
            }
            Rect all = rect.AtZero();
            Pane.CardBack(all);
            ModernWidgets.Active = JustDesktopMod.Settings.modernWidgets;
            try
            {
                draw(all, p);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[JustDesktop] exception drawing a caravan pawn's panel " + tab.GetType() + ": " + e, 71233 ^ tab.GetType().GetHashCode());
            }
            finally
            {
                ModernWidgets.Active = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = Color.white;
            }
            if (TabPanels.CloseButton(all))
            {
                close = true;
                SoundDefOf.TabClose.PlayOneShotOnCamera();
            }
        }, doBackground: false, absorbInputAroundWindow: false);
        if (close)
        {
            pawn = null;
        }
        return false;
    }
}

[HarmonyPatch(typeof(WITab_Caravan_Needs), "ExtraOnGUI")]
public static class CaravanPanels_Needs
{
    public static bool Prefix(WITab_Caravan_Needs __instance) =>
        CaravanPanels.Draw(__instance, ref CaravanPanels.NeedsPawn(__instance), 1439870016, p => NeedsPanel.Size(p), (r, p) => NeedsPanel.Draw(r, p));
}

[HarmonyPatch(typeof(WITab_Caravan_Health), "ExtraOnGUI")]
public static class CaravanPanels_Health
{
    public static bool Prefix(WITab_Caravan_Health __instance) =>
        CaravanPanels.Draw(__instance, ref CaravanPanels.HealthPawn(__instance), 1439870017, p => HealthPanel.Size(null, p), (r, p) => HealthPanel.Draw(r, null, p));
}

[HarmonyPatch(typeof(WITab_Caravan_Social), "ExtraOnGUI")]
public static class CaravanPanels_Social
{
    public static bool Prefix(WITab_Caravan_Social __instance) =>
        CaravanPanels.Draw(__instance, ref CaravanPanels.SocialPawn(__instance), 1439870018, p => SocialPanel.Size(p), (r, p) => SocialPanel.Draw(r, p));
}

// the rows' height the game measured; Health and Needs draw them inside 10px margins, Social starts 15px lower
[HarmonyPatch(typeof(WITab_Caravan_Needs), "UpdateSize")]
public static class CaravanPanels_NeedsSize
{
    private static readonly AccessTools.FieldRef<WITab_Caravan_Needs, float> rows = AccessTools.FieldRefAccess<WITab_Caravan_Needs, float>("scrollViewHeight");

    public static void Postfix(WITab_Caravan_Needs __instance) => CaravanPanels.FitList(__instance, rows(__instance), 20f);
}

[HarmonyPatch(typeof(WITab_Caravan_Health), "UpdateSize")]
public static class CaravanPanels_HealthSize
{
    private static readonly AccessTools.FieldRef<WITab_Caravan_Health, float> rows = AccessTools.FieldRefAccess<WITab_Caravan_Health, float>("scrollViewHeight");

    public static void Postfix(WITab_Caravan_Health __instance) => CaravanPanels.FitList(__instance, rows(__instance), 20f);
}

[HarmonyPatch(typeof(WITab_Caravan_Social), "UpdateSize")]
public static class CaravanPanels_SocialSize
{
    private static readonly AccessTools.FieldRef<WITab_Caravan_Social, float> rows = AccessTools.FieldRefAccess<WITab_Caravan_Social, float>("scrollViewHeight");

    public static void Postfix(WITab_Caravan_Social __instance) => CaravanPanels.FitList(__instance, rows(__instance), 35f);
}
