using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// an All tab first in the Quests window: every quest, active ones first, then the ones you can still accept, then
// the past ones, each row with a colored mark at its left saying which. The game's own tabs work as before
public static class Quests
{
    public static bool All;

    private static readonly AccessTools.FieldRef<MainTabWindow_Quests, List<TabRecord>> tabs = AccessTools.FieldRefAccess<MainTabWindow_Quests, List<TabRecord>>("tabs");
    private static readonly AccessTools.FieldRef<MainTabWindow_Quests, bool> showAll = AccessTools.FieldRefAccess<MainTabWindow_Quests, bool>("showAll");
    private static readonly AccessTools.FieldRef<List<Quest>> toShow = AccessTools.StaticFieldRefAccess<List<Quest>>(AccessTools.Field(typeof(MainTabWindow_Quests), "tmpQuestsToShow"));

    public static readonly Color AvailableColor = new(0.45f, 0.68f, 1f);
    public static readonly Color ActiveColor = new(0.45f, 0.82f, 0.52f);
    public static readonly Color PastColor = new(0.55f, 0.58f, 0.62f);

    // 0 active, 1 can be accepted, 2 past (or dismissed), like the game's tabs
    public static int Kind(Quest q) => q.dismissed || q.Historical ? 2 : q.State == QuestState.Ongoing ? 0 : 1;

    [HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.PreOpen))]
    public static class Tab
    {
        public static void Postfix(MainTabWindow_Quests __instance)
        {
            List<TabRecord> list = tabs(__instance);
            // the game's tabs turn All off, and aren't shown as open while it's on
            foreach (TabRecord t in list)
            {
                Action click = t.clickedAction;
                Func<bool> open = t.selectedGetter;
                t.clickedAction = () =>
                {
                    All = false;
                    click();
                };
                t.selectedGetter = () => !All && open();
            }
            list.Insert(0, new TabRecord("All", () =>
            {
                All = true;
                // the game's Available tab underneath, so its scroll and the rest have something to go with
                list[1].clickedAction();
                All = true;
            }, () => All));
        }
    }

    // picking a quest moves the game to its own tab, and to that tab's scroll; in All it stays put
    private static readonly System.Reflection.FieldInfo curTab = AccessTools.Field(typeof(MainTabWindow_Quests), "curTab");

    [HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.Select))]
    public static class Select
    {
        public static void Postfix(MainTabWindow_Quests __instance)
        {
            if (All)
            {
                curTab.SetValue(__instance, Enum.ToObject(curTab.FieldType, 0));
            }
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Quests), "ShouldListNow")]
    public static class Listed
    {
        public static bool Prefix(MainTabWindow_Quests __instance, Quest quest, ref bool __result)
        {
            if (!All)
            {
                return true;
            }
            __result = !quest.hiddenInUI && (!quest.hidden || showAll(__instance));
            return false;
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Quests), "SortQuestsByTab")]
    public static class Sort
    {
        public static void Postfix()
        {
            if (!All)
            {
                return;
            }
            List<Quest> quests = toShow();
            // active first, newest accepted on top; then what can be accepted, soonest to expire on top; then the past
            quests.SortBy(q => Kind(q), q => Kind(q) switch { 0 => q.TicksSinceAccepted, 1 => q.TicksUntilExpiry, _ => q.TicksSinceCleanup });
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Quests), "DoRow")]
    public static class Row
    {
        public static void Prefix(Rect rect, Quest quest)
        {
            if (!All)
            {
                return;
            }
            int kind = Kind(quest);
            Color c = kind == 0 ? ActiveColor : kind == 1 ? AvailableColor : PastColor;
            GUI.color = c;
            GUI.DrawTexture(new Rect(rect.x, rect.y + 6f, 3f, rect.height - 12f), BaseContent.WhiteTex);
            GUI.color = Color.white;
            if (Mouse.IsOver(new Rect(rect.x, rect.y, 8f, rect.height)))
            {
                TooltipHandler.TipRegion(new Rect(rect.x, rect.y, 8f, rect.height), (kind == 0 ? "ActiveQuests" : kind == 1 ? "AvailableQuests" : "HistoricalQuests").Translate());
            }
        }
    }
}
