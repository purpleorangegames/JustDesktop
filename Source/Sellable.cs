using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// what a trader buys (from a settlement's or a trader's gizmo): an All tab first, open from the start, and a search
// box over the tabs that narrows down the tab that's open
public static class Sellable
{
    public static string Search = "";
    // the All tab is open: no category and not the pawns
    public static bool All;

    private static readonly AccessTools.FieldRef<Dialog_SellableItems, ThingCategoryDef> currentCategory = AccessTools.FieldRefAccess<Dialog_SellableItems, ThingCategoryDef>("currentCategory");
    private static readonly AccessTools.FieldRef<Dialog_SellableItems, bool> pawnsTabOpen = AccessTools.FieldRefAccess<Dialog_SellableItems, bool>("pawnsTabOpen");
    private static readonly AccessTools.FieldRef<Dialog_SellableItems, List<TabRecord>> tabs = AccessTools.FieldRefAccess<Dialog_SellableItems, List<TabRecord>>("tabs");
    private static readonly AccessTools.FieldRef<Dialog_SellableItems, List<ThingDef>> sellableItems = AccessTools.FieldRefAccess<Dialog_SellableItems, List<ThingDef>>("sellableItems");
    private static readonly AccessTools.FieldRef<Dialog_SellableItems, ITrader> trader = AccessTools.FieldRefAccess<Dialog_SellableItems, ITrader>("trader");

    private static readonly List<ThingDef> found = new();

    public static void Setup(Dialog_SellableItems dialog)
    {
        Search = "";
        All = true;
        currentCategory(dialog) = null;
        pawnsTabOpen(dialog) = false;
        // the other tabs leave All when picked
        foreach (TabRecord tab in tabs(dialog))
        {
            System.Action picked = tab.clickedAction;
            tab.clickedAction = () =>
            {
                All = false;
                picked();
            };
        }
        tabs(dialog).Insert(0, new TabRecord("All", () =>
        {
            All = true;
            currentCategory(dialog) = null;
            pawnsTabOpen(dialog) = false;
        }, () => All));
    }

    public static List<ThingDef> AllItems(Dialog_SellableItems dialog) => sellableItems(dialog);

    // a tab's list narrowed down by the search
    public static List<ThingDef> Filter(List<ThingDef> list)
    {
        string query = Search.Trim();
        if (query.Length == 0)
        {
            return list;
        }
        found.Clear();
        foreach (ThingDef def in list)
        {
            if (def.label != null && def.label.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found.Add(def);
            }
        }
        return found;
    }

    // in the room between the restock line and the tabs, at the right
    public static void DrawSearch(Dialog_SellableItems dialog, Rect inRect)
    {
        float top = 40f;
        if (trader(dialog) is ITraderRestockingInfoProvider p && (p.NextRestockTick != -1 || !p.EverVisited || p.RestockedSinceLastVisit))
        {
            top += 20f;
        }
        Rect r = new Rect(inRect.width - 17f - 200f, top + 1f, 200f, 28f);
        GUI.SetNextControlName("JustDesktopSellableSearch");
        string typed = Widgets.TextField(r, Search);
        if (typed != Search)
        {
            Search = typed;
        }
        if (Search.Length == 0 && GUI.GetNameOfFocusedControl() != "JustDesktopSellableSearch")
        {
            GUI.color = Style.Dim;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(r.x + 8f, r.y, r.width - 8f, r.height), "Search");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
    }
}

[HarmonyPatch(typeof(Dialog_SellableItems), MethodType.Constructor, typeof(ITrader))]
public static class Sellable_Setup
{
    public static void Postfix(Dialog_SellableItems __instance)
    {
        Sellable.Setup(__instance);
    }
}

[HarmonyPatch(typeof(Dialog_SellableItems), "GetSellableItemsInCategory")]
public static class Sellable_Items
{
    // the All tab has no category, the game's lookup would fail on it
    public static bool Prefix(Dialog_SellableItems __instance, ThingCategoryDef category, bool pawns, ref List<ThingDef> __result)
    {
        if (category != null || pawns || !Sellable.All)
        {
            return true;
        }
        __result = Sellable.Filter(Sellable.AllItems(__instance));
        return false;
    }

    public static void Postfix(Dialog_SellableItems __instance, ThingCategoryDef category, bool pawns, ref List<ThingDef> __result)
    {
        if (category == null && !pawns && Sellable.All)
        {
            return;
        }
        // only for the list being shown; the tabs count theirs when the window opens, before anything is typed
        __result = Sellable.Filter(__result);
    }
}

[HarmonyPatch(typeof(Dialog_SellableItems), nameof(Dialog_SellableItems.DoWindowContents))]
public static class Sellable_Search
{
    public static void Postfix(Dialog_SellableItems __instance, Rect inRect)
    {
        Sellable.DrawSearch(__instance, inRect);
    }
}
