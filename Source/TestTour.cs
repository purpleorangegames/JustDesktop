using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustDesktop;

// test: JUSTDESKTOP_TEST_TOUR=all (or names separated by commas) opens one window after another in the loaded game and
// takes the game's screenshot of each as tour-<name>, then tour-done. To find what still has the game's old look
public static class TestTour
{
    private const int Start = 300;
    private const int Each = 80;

    private static readonly string wanted = Environment.GetEnvironmentVariable("JUSTDESKTOP_TEST_TOUR");
    private static List<(string name, Action open)> steps;
    private static int frame;
    // JUSTDESKTOP_TEST_PRETTY=1 for screenshots to show: midday and the camera closer to the colony
    private static readonly bool pretty = Environment.GetEnvironmentVariable("JUSTDESKTOP_TEST_PRETTY") == "1";
    // JUSTDESKTOP_TEST_QUIT=1 quits the game like the menu does once the tour is done, to time how long closing takes
    private static readonly bool quit = Environment.GetEnvironmentVariable("JUSTDESKTOP_TEST_QUIT") == "1";
    // something a step does a few frames later, when the world map has to be up first
    private static Action later;

    private static Map Map => Find.CurrentMap;
    private static Pawn Colonist => Map.mapPawns.FreeColonistsSpawned.FirstOrDefault();

    private static void Select(object thing, Type tab = null)
    {
        if (thing == null)
        {
            return;
        }
        Find.Selector.ClearSelection();
        Find.Selector.Select(thing);
        if (thing is Thing t)
        {
            CameraJumper.TryJump(t);
        }
        if (tab != null)
        {
            InspectPaneUtility.OpenTab(tab);
        }
    }

    private static Building_WorkTable Bench => Map.listerBuildings.allBuildingsColonist.OfType<Building_WorkTable>().FirstOrDefault(b => b.BillStack.Count > 0)
        ?? Map.listerBuildings.allBuildingsColonist.OfType<Building_WorkTable>().FirstOrDefault();
    private static Zone_Stockpile Stockpile => Map.zoneManager.AllZones.OfType<Zone_Stockpile>().FirstOrDefault();

    private static List<(string, Action)> All()
    {
        var list = new List<(string, Action)>();
        foreach (string tab in new[] { "Work", "Assign", "Animals", "Wildlife", "Mechs", "Ideos" })
        {
            list.Add((tab, () => DefDatabase<MainButtonDef>.GetNamedSilentFail(tab)?.Worker.InterfaceTryActivate()));
        }
        list.Add(("map", () => { }));
        list.Add(("pawn", () => Select(Colonist, typeof(ITab_Pawn_Character))));
        list.Add(("psyfocus", () =>
        {
            Select(Colonist);
            if (Colonist != null && Colonist.GetPsylinkLevel() == 0)
            {
                Colonist.ChangePsylinkLevel(2, false);
            }
        }));
        list.Add(("minimap", () => Find.WindowStack.Add(new Window_Minimap())));
        list.Add(("drafted", () =>
        {
            Select(Colonist);
            if (Colonist?.drafter != null)
            {
                Colonist.drafter.Drafted = true;
            }
        }));
        list.Add(("multi", () =>
        {
            JustDesktopMod.Settings.severalWindows = true;
            foreach (string tab in new[] { "Work", "Research", "Schedule" })
            {
                DefDatabase<MainButtonDef>.GetNamedSilentFail(tab)?.Worker.InterfaceTryActivate();
            }
        }));
        list.Add(("colonists", () =>
        {
            Find.Selector.ClearSelection();
            foreach (Pawn p in Map.mapPawns.FreeColonistsSpawned.Take(5))
            {
                Find.Selector.Select(p);
            }
        }));
        list.Add(("architect", () => MainButtonDefOf.Architect.Worker.InterfaceTryActivate()));
        // the centered Architect with Furniture open on the left and Production on the right
        list.Add(("architectsides", () =>
        {
            JustDesktopMod.Settings.architectCentered = true;
            MainButtonDefOf.Architect.Worker.InterfaceTryActivate();
            later = () =>
            {
                if (MainButtonDefOf.Architect.TabWindow is MainTabWindow_Architect window && Traverse.Create(window).Field("desPanelsCached").GetValue<List<ArchitectCategoryTab>>() is { } panels)
                {
                    ArchitectSides.Left = ArchitectSides.Right = null;
                    if (panels.FirstOrDefault(x => x.def.defName == "Furniture") is { } furniture)
                    {
                        ArchitectSides.Clicked(window, furniture, false);
                    }
                    if (panels.FirstOrDefault(x => x.def.defName == "Production") is { } production)
                    {
                        ArchitectSides.Clicked(window, production, true);
                    }
                }
            };
        }));
        list.Add(("bills", () => Select(Bench, typeof(ITab_Bills))));
        list.Add(("billconfig", () =>
        {
            if (Bench?.BillStack.Bills.OfType<Bill_Production>().FirstOrDefault() is { } bill)
            {
                Find.WindowStack.Add(new Dialog_BillConfig(bill, Bench.Position));
            }
        }));
        list.Add(("storage", () => Select(Stockpile, typeof(ITab_Storage))));
        list.Add(("zone", () => Select(Stockpile)));
        list.Add(("growing", () => Select(Map.zoneManager.AllZones.OfType<Zone_Growing>().FirstOrDefault())));
        list.Add(("training", () => Select(Map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.RaceProps.Animal && p.Faction == Faction.OfPlayer), typeof(ITab_Pawn_Training))));
        list.Add(("prisoner", () => Select(Map.mapPawns.PrisonersOfColonySpawned.FirstOrDefault(), typeof(ITab_Pawn_Prisoner))));
        list.Add(("genes", () => Select(Colonist, typeof(ITab_Genes))));
        list.Add(("infocard", () => Find.WindowStack.Add(new Dialog_InfoCard(Colonist))));
        list.Add(("viewgenes", () => Find.WindowStack.Add(new Dialog_ViewGenes(Colonist))));
        list.Add(("apparel", () => Find.WindowStack.Add(new Dialog_ManageApparelPolicies(null))));
        list.Add(("food", () => Find.WindowStack.Add(new Dialog_ManageFoodPolicies(null))));
        list.Add(("drugs", () => Find.WindowStack.Add(new Dialog_ManageDrugPolicies(null))));
        list.Add(("reading", () => Find.WindowStack.Add(new Dialog_ManageReadingPolicies(null))));
        list.Add(("areas", () => Find.WindowStack.Add(new Dialog_ManageAreas(Map))));
        list.Add(("medical", () => Find.WindowStack.Add(new Dialog_MedicalDefaults())));
        list.Add(("caravan", () => Find.WindowStack.Add(new Dialog_FormCaravan(Map))));
        list.Add(("renamezone", () =>
        {
            if (Stockpile != null)
            {
                Find.WindowStack.Add(new Dialog_RenameZone(Stockpile));
            }
        }));
        list.Add(("keys", () => Find.WindowStack.Add(new Dialog_KeyBindings())));
        list.Add(("mods", () => Find.WindowStack.Add(new Page_ModsConfig())));
        list.Add(("letter", () => Find.LetterStack.LettersListForReading.LastOrDefault()?.OpenLetter()));
        list.Add(("world", () => CameraJumper.TryShowWorld()));
        list.Add(("worldcaravan", () =>
        {
            CameraJumper.TryShowWorld();
            if (Find.WorldObjects.Caravans.FirstOrDefault(c => c.IsPlayerControlled) is { } caravan)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(caravan);
            }
        }));
        list.Add(("worldtab", () =>
        {
            CameraJumper.TryShowWorld();
            if (Find.WorldObjects.Caravans.FirstOrDefault(c => c.IsPlayerControlled) is { } caravan)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(caravan);
                Find.World.UI.inspectPane.OpenTabType = typeof(WITab_Caravan_Items);
            }
        }));
        list.Add(("worldneeds", () =>
        {
            CameraJumper.TryShowWorld();
            if (Find.WorldObjects.Caravans.FirstOrDefault(c => c.IsPlayerControlled) is { } caravan)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(caravan);
                Find.World.UI.inspectPane.OpenTabType = typeof(WITab_Caravan_Needs);
            }
        }));
        list.Add(("worldhealth", () =>
        {
            CameraJumper.TryShowWorld();
            if (Find.WorldObjects.Caravans.FirstOrDefault(c => c.IsPlayerControlled) is { } caravan)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(caravan);
                Find.World.UI.inspectPane.OpenTabType = typeof(WITab_Caravan_Health);
            }
        }));
        list.Add(("worldgear", () =>
        {
            CameraJumper.TryShowWorld();
            if (Find.WorldObjects.Caravans.FirstOrDefault(c => c.IsPlayerControlled) is { } caravan)
            {
                Find.WorldSelector.ClearSelection();
                Find.WorldSelector.Select(caravan);
                Find.World.UI.inspectPane.OpenTabType = typeof(WITab_Caravan_Gear);
            }
        }));
        list.Add(("sellable", () =>
        {
            if (Find.WorldObjects.Settlements.FirstOrDefault(x => x.Faction != Faction.OfPlayer && x.TraderKind != null) is { } town)
            {
                Find.WindowStack.Add(new Dialog_SellableItems(town));
            }
        }));
        list.Add(("worldtown", () =>
        {
            CameraJumper.TryShowWorld();
            if (Find.WorldObjects.Settlements.FirstOrDefault(x => x.Faction != Faction.OfPlayer && x.TraderKind != null) is { } town)
            {
                later = () =>
                {
                    Find.WorldSelector.ClearSelection();
                    Find.WorldSelector.Select(town);
                    Find.WorldCameraDriver.JumpTo(town.Tile);
                };
                if (!RestockWatch.Get.Watching(town))
                {
                    RestockWatch.Get.Toggle(town);
                }
                AccessTools.Method(typeof(RestockWatch), "Send").Invoke(null, new object[] { town });
            }
        }));
        list.Add(("worldtile", () =>
        {
            CameraJumper.TryShowWorld();
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.SelectedTile = Find.CurrentMap.Tile;
        }));
        list.Add(("trade", () =>
        {
            if (Map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.CanTradeNow) is { } trader)
            {
                Find.WindowStack.Add(new Dialog_Trade(Colonist, trader));
            }
        }));
        list.Add(("toggles", () => { if (Hud.TogglesButton.width > 0f) Find.WindowStack.Add(new Window_Toggles(Hud.TogglesButton, false)); }));
        list.Add(("calendar", () => Window_Calendar.Toggle(Hud.ClockRect)));
        // the calendar keeping its events: turns the option on, empties the game's archive of letters, saves as calkeep
        // (load that save to see they come back from it) and opens the calendar
        list.Add(("calendarkept", () =>
        {
            JustDesktopMod.Settings.calendarKeep = true;
            CalendarHistory.Get.KeepArchive();
            foreach (IArchivable a in Find.Archive.ArchivablesListForReading.OfType<Letter>().Cast<IArchivable>().ToList())
            {
                Find.Archive.Remove(a);
            }
            Log.Message("[JustDesktop] calendarkept: kept " + CalendarHistory.Get.All.Count + ", letters left in the archive " + Find.Archive.ArchivablesListForReading.OfType<Letter>().Count());
            GameDataSaveLoader.SaveGame("calkeep");
            Window_Calendar.Toggle(Hud.ClockRect);
        }));
        // taking the mod out of the save: fills the restock watch, the research queue and the calendar's kept events,
        // saves as notcleaned, clears them like the button does and saves as cleaned. Load both without the mod to compare
        list.Add(("cleanup", () =>
        {
            if (Find.WorldObjects.Settlements.FirstOrDefault(x => x.TraderKind != null && x.Faction != null && !x.Faction.IsPlayer) is { } town && !RestockWatch.Get.Watching(town))
            {
                RestockWatch.Get.Toggle(town);
            }
            if (DefDatabase<ResearchProjectDef>.AllDefs.FirstOrDefault(ResearchQueue.Queueable) is { } project)
            {
                ResearchQueue.Get.Add(project);
            }
            JustDesktopMod.Settings.calendarKeep = true;
            CalendarHistory.Get.KeepArchive();
            GameDataSaveLoader.SaveGame("notcleaned");
            SaveCleanup.Clear();
            GameDataSaveLoader.SaveGame("cleaned");
            Log.Message("[JustDesktop] cleanup: saved notcleaned and cleaned, ours left in the game " + SaveCleanup.Has(Current.Game));
        }));
        // who builds: a wooden bed's plan near a colonist with the last one in the list as its builder, made a frame with
        // its materials, then who would get the job to build it is logged; the frame selected with the list open
        list.Add(("builder", () =>
        {
            ThingDef bed = ThingDefOf.Bed;
            Map map = Map;
            if (!CellFinder.TryFindRandomCellNear(Colonist.Position, map, 8, c => GenConstruct.CanPlaceBlueprintAt(bed, c, Rot4.North, map, stuffDef: ThingDefOf.WoodLog).Accepted, out IntVec3 cell))
            {
                Log.Message("[JustDesktop] builder: no room for a bed");
                return;
            }
            var plan = (Blueprint_Build)GenConstruct.PlaceBlueprintForBuild(bed, cell, map, Rot4.North, Faction.OfPlayer, ThingDefOf.WoodLog);
            List<Pawn> pawns = BuilderPick.Candidates(map);
            Log.Message("[JustDesktop] builder: list " + string.Join(", ", pawns.Select(p => p.LabelShort + " " + BuilderPick.Skill(p) + (BuilderPick.Bonus(p) is { } b ? " (" + b + ")" : ""))));
            Pawn picked = pawns.Last();
            BuilderPick.Get.Set(plan, picked);
            plan.TryReplaceWithSolidThing(picked, out Thing made, out bool _);
            var frame = (Frame)made;
            foreach (ThingDefCountClass cost in frame.TotalMaterialCost())
            {
                Thing stuff = ThingMaker.MakeThing(cost.thingDef);
                stuff.stackCount = cost.count;
                frame.resourceContainer.TryAdd(stuff);
            }
            WorkGiver worker = DefDatabase<WorkGiverDef>.GetNamed("ConstructFinishFrames").Worker;
            Log.Message("[JustDesktop] builder: frame kept " + BuilderPick.Get.BuilderOf(frame)?.LabelShort + ", complete " + frame.IsCompleted() + ", would build it: "
                + string.Join(", ", pawns.Select(p => p.LabelShort + " " + (((WorkGiver_Scanner)worker).JobOnThing(p, frame) != null ? "yes" : "no") + (((WorkGiver_Scanner)worker).JobOnThing(p, frame, true) != null ? "/forced yes" : "/forced no"))));
            Select(frame);
            BuilderPick.Menu(map);
            // next to the gizmos instead of where the mouse happens to be
            if (Find.WindowStack.WindowOfType<FloatMenu>() is { } menu)
            {
                menu.vanishIfMouseDistant = false;
                menu.windowRect.x = UI.screenWidth / 2f + 140f;
                menu.windowRect.y = UI.screenHeight - Taskbar.Reserved - 330f;
            }
        }));
        // the research window with a few projects in the queue
        list.Add(("research", () =>
        {
            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefs.Where(p => ResearchQueue.Queueable(p) && p.CanStartNow).Take(3))
            {
                ResearchQueue.Get.Add(project);
            }
            DefDatabase<MainButtonDef>.GetNamed("Research").Worker.InterfaceTryActivate();
        }));
        list.Add(("notifications", () => { Find.WindowStack.Add(new Window_Notifications(new Rect(UI.screenWidth - 38f, UI.screenHeight - 120f, 32f, 32f))); }));
        list.Add(("respick", () => Resources.Editing = true));
        list.Add(("reswindow", () => { Resources.Editing = false; Find.WindowStack.Add(new Dialog_Resources()); }));
        list.Add(("reshidden", () => { Resources.Set("cat:Foods", false); Resources.Set("thing:Silver", false); }));
        return list;
    }

    private static void CloseAll()
    {
        if (WorldRendererUtility.WorldSelected)
        {
            CameraJumper.TryHideWorld();
        }
        foreach (Window w in Find.WindowStack.Windows.ToList())
        {
            if (w is not ImmediateWindow && w is not MainTabWindow_Inspect)
            {
                Find.WindowStack.TryRemove(w, false);
            }
        }
        Find.Selector.ClearSelection();
    }

    public static void Tick()
    {
        if (wanted == null || Current.ProgramState != ProgramState.Playing)
        {
            return;
        }
        if (steps == null)
        {
            steps = All();
            if (wanted != "all")
            {
                var names = new HashSet<string>(wanted.Split(','));
                steps = steps.Where(s => names.Contains(s.name)).ToList();
            }
        }
        frame++;
        if (pretty && frame == Start - 1)
        {
            // midday, and closer to the colony
            int hour = GenLocalDate.HourOfDay(Map);
            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + (12 - hour + 24) % 24 * GenDate.TicksPerHour);
            if (Colonist != null)
            {
                Find.CameraDriver.SetRootPosAndSize(Colonist.DrawPos, 15f);
            }
        }
        if (frame < Start)
        {
            return;
        }
        int i = (frame - Start) / Each, at = (frame - Start) % Each;
        if (i >= steps.Count)
        {
            if (at == 0 && i == steps.Count)
            {
                CloseAll();
                ScreenshotTaker.TakeNonSteamShot("tour-done");
            }
            if (quit && at == 40 && i == steps.Count)
            {
                Log.Message("[JustDesktop] test quit");
                Root.Shutdown();
            }
            return;
        }
        if (at == 10 && later != null)
        {
            later();
            later = null;
        }
        if (at == 0)
        {
            CloseAll();
            try
            {
                steps[i].open();
            }
            catch (Exception e)
            {
                Log.Warning("[JustDesktop] tour " + steps[i].name + ": " + e.Message);
            }
        }
        else if (at == Each / 2 - 1 && WorldRendererUtility.WorldSelected)
        {
            Log.Message("[JustDesktop] tour " + steps[i].name + ": objects " + Find.WorldSelector.NumSelectedObjects + " (" + string.Join(", ", Find.WorldSelector.SelectedObjects.Select(o => o.GetType().Name)) + "), tile " + Find.WorldSelector.SelectedTile + ", open tab " + Find.MainTabsRoot.OpenTab?.defName + ", pane open " + Find.WindowStack.IsOpen(Find.World.UI.inspectPane) + ", card " + WorldCard.Shown);
        }
        else if (at == Each / 2)
        {
            ScreenshotTaker.TakeNonSteamShot("tour-" + steps[i].name);
            // without the "screenshot saved" note the game shows the next frame, the shot can still catch it
            AccessTools.Field(typeof(ScreenshotTaker), "suppressMessage").SetValue(null, true);
        }
    }
}
