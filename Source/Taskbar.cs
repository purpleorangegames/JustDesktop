using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustDesktop;

// replaces the row of buttons at the bottom; hotkeys still go through the game's own code
[HarmonyPatch(typeof(MainButtonsRoot), "DoButtons")]
public static class Taskbar
{
    private const float GroupGap = 15f;

    private static float Scale => JustDesktopMod.Settings.size;

    // height of the bar, also what the game keeps free at the bottom (see BarHeight)
    public static float Height => Mathf.Round(35f * Scale);
    // what sits on the bar (windows, the selection card, gizmos, Architect) sits this far up, raised by the option
    public static int Reserved => (int)(Height + JustDesktopMod.Settings.windowLift);

    private static readonly string[][] groups =
    {
        new[] { "Work", "Schedule", "Assign" },
        new[] { "Animals", "Mechs", "Wildlife" },
        new[] { "Research", "Quests" },
        new[] { "World", "Factions", "Ideos", "History" }
    };

    private static List<MainButtonDef> programs, tray;
    // the button under the mouse this frame, for the name label; the dock gives its icon to put the label over
    private static MainButtonDef hovered;
    private static Rect hoveredRect;
    public static Rect? hoveredIcon;
    private static readonly List<MainButtonDef> visible = new();

    public static int GroupOf(MainButtonDef def)
    {
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i].Contains(def.defName))
            {
                return i;
            }
        }
        return groups.Length;
    }

    private static void Sort()
    {
        var all = DefDatabase<MainButtonDef>.AllDefs.Where(d => d != MainButtonDefOf.Architect && d != MainButtonDefOf.Inspect).ToList();
        // the game menu and small icon buttons from other mods go on the right, like a tray
        tray = all.Where(d => d == MainButtonDefOf.Menu || (GroupOf(d) == groups.Length && d.minimized)).OrderBy(d => d == MainButtonDefOf.Menu).ThenBy(d => d.order).ToList();
        programs = all.Except(tray).OrderBy(GroupOf).ThenBy(d => d.order).ToList();
    }

    // test switch, JUSTDESKTOP_TEST_TAB=<defName> opens that tab once the map is up, add ":max" to maximize it;
    // several separated by commas; "settings" opens the mod settings, "taskbar" the taskbar settings, "icons" the icon window,
    // "select" selects a colonist ("select:animal" one of ours, "select:wild" a wild one, "select:bench", "select:item", "select:plant" or "select:<defName>"), "itab:ITab_Pawn_Gear" opens that tab of the selection pane
    private static string testTab = System.Environment.GetEnvironmentVariable("JUSTDESKTOP_TEST_TAB");
    // JUSTDESKTOP_TEST_SHOT=<name> saves the game's own screenshot a bit later, into the Screenshots folder
    private static string testShot = System.Environment.GetEnvironmentVariable("JUSTDESKTOP_TEST_SHOT");
    private static int testFrames;
    // JUSTDESKTOP_TEST_PAN=1 sweeps the camera around the selected pawn
    private static readonly bool panTest = System.Environment.GetEnvironmentVariable("JUSTDESKTOP_TEST_PAN") == "1";

    public static bool Prefix()
    {
        if (programs == null)
        {
            Sort();
        }
        if (testShot != null && testFrames > 500)
        {
            ScreenshotTaker.TakeNonSteamShot(testShot);
            testShot = null;
        }
        if (Current.ProgramState == ProgramState.Playing)
        {
            testFrames++;
            TestTour.Tick();
        }
        // test: pan the camera back and forth around the selected pawn
        if (panTest && testFrames > 260 && Find.Selector.SingleSelectedThing is Pawn panned)
        {
            Find.CameraDriver.JumpToCurrentMapLoc(panned.DrawPos + new Vector3(Mathf.Sin(testFrames * 0.01f) * 60f, 0f, Mathf.Cos(testFrames * 0.013f) * 40f));
            // and a screenshot every so often along the way
            if (testFrames > 300 && testFrames % 70 == 0 && testFrames < 300 + 70 * 9)
            {
                ScreenshotTaker.TakeNonSteamShot("pan" + testFrames);
            }
        }
        if (testTab != null && testFrames > 200)
        {
            string tabs = testTab;
            testTab = null;
            foreach (string tab in tabs.Split(','))
            {
                string[] parts = tab.Split(':');
                if (parts[0] == "settings")
                {
                    Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<JustDesktopMod>()));
                }
                else if (parts[0] == "taskbar")
                {
                    Find.WindowStack.Add(new Dialog_TaskbarSettings());
                }
                else if (parts[0] == "select" && Find.CurrentMap != null)
                {
                    string which = parts.Length > 1 ? parts[1] : "";
                    Map map = Find.CurrentMap;
                    Thing pick = which == "animal" ? map.mapPawns.AllPawnsSpawned.FirstOrDefault(x => x.RaceProps.Animal && x.Faction == Faction.OfPlayer)
                        : which == "wild" ? map.mapPawns.AllPawnsSpawned.FirstOrDefault(x => x.RaceProps.Animal && x.Faction == null)
                        : which == "bench" ? map.listerBuildings.allBuildingsColonist.FirstOrDefault(b => b is Building_WorkTable)
                        : which == "item" ? map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).FirstOrDefault(x => x.Spawned && x.stackCount > 1)
                        : which == "plant" ? map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).FirstOrDefault(x => x.def.plant.Sowable)
                        : which != "" ? map.listerThings.AllThings.FirstOrDefault(x => x.def.defName == which && x.Spawned)
                        : map.mapPawns.FreeColonists.FirstOrDefault();
                    if (pick != null)
                    {
                        Find.Selector.ClearSelection();
                        Find.Selector.Select(pick);
                        // pan there so the screenshot shows it
                        CameraJumper.TryJump(pick);
                    }
                }
                else if (parts[0] == "itab" && parts.Length > 1 && GenTypes.GetTypeInAnyAssembly("RimWorld." + parts[1]) is { } itab)
                {
                    InspectPaneUtility.OpenTab(itab);
                }
                else if (parts[0] == "icons")
                {
                    Find.WindowStack.Add(new Dialog_Icons(JustDesktopMod.Settings.style));
                }
                else if (DefDatabase<MainButtonDef>.GetNamedSilentFail(parts[0]) is { } def)
                {
                    def.Worker.InterfaceTryActivate();
                    if (parts.Length > 1 && parts[1] == "max" && def.TabWindow != null)
                    {
                        WindowFrame.Maximize(def.TabWindow);
                    }
                }
            }
        }
        WindowFrame.UpdateDrag();
        hovered = null;
        hoveredIcon = null;
        if (JustDesktopMod.Settings.style == BarStyle.Dock)
        {
            Dock.Draw(programs, tray);
        }
        else
        {
            DrawBar();
        }
        // drawn last so it goes over the other buttons
        if (hovered != null && JustDesktopMod.Settings.hoverInfo == HoverInfo.Name)
        {
            Style.NameLabel(hovered, hoveredIcon ?? hoveredRect);
        }
        GUI.color = Color.white;
        return false;
    }

    private static void DrawBar()
    {
        Settings s = JustDesktopMod.Settings;
        float h = Height;
        float y = UI.screenHeight - h;
        if (s.back == BackMode.Bar)
        {
            Widgets.DrawBoxSolid(new Rect(0f, y, UI.screenWidth, h), s.backColor);
            Widgets.DrawBoxSolid(new Rect(0f, y, UI.screenWidth, 1f), Style.Faded(0.45f));
        }

        float iconOnly = 40f * Scale;
        float right = UI.screenWidth;
        for (int i = tray.Count - 1; i >= 0; i--)
        {
            if (tray[i].Worker.Visible)
            {
                right -= iconOnly;
                DoButton(new Rect(right, y, iconOnly, h), tray[i], false);
            }
        }

        visible.Clear();
        visible.AddRange(programs.Where(d => d.Worker.Visible));
        GameFont font = Scale >= 1.3f ? GameFont.Medium : GameFont.Small;
        float start = MainButtonDefOf.Architect.Worker.Visible ? 48f * Scale : 0f;
        bool labels = s.labels && start + 8f + Layout(true, font) <= right - 8f;
        float width = start + 8f + Layout(labels, font);
        float x = s.align switch
        {
            BarAlign.Center => Mathf.Max(0f, (UI.screenWidth - width) / 2f),
            BarAlign.Right => Mathf.Max(0f, right - 8f - width),
            _ => 0f
        };
        if (start > 0f)
        {
            DoStart(new Rect(x, y, start, h));
        }
        x += start + 8f;
        int lastGroup = -1;
        foreach (MainButtonDef def in visible)
        {
            int group = GroupOf(def);
            if (lastGroup >= 0 && group != lastGroup && s.separators)
            {
                Widgets.DrawBoxSolid(new Rect(x + GroupGap / 2f - 1f, y + h * 0.26f, 1f, h * 0.48f), Style.Divider);
                x += GroupGap;
            }
            lastGroup = group;
            float w = Width(def, labels, font);
            DoButton(new Rect(x, y, w, h), def, labels, font);
            x += w + 2f;
        }
    }

    private static float Width(MainButtonDef def, bool labels, GameFont font)
    {
        if (!labels)
        {
            return 40f * Scale;
        }
        GameFont old = Text.Font;
        Text.Font = font;
        float text = Text.CalcSize(def.LabelCap).x;
        Text.Font = old;
        return (10f + 20f + 8f + 12f) * Scale + text;
    }

    private static float Layout(bool labels, GameFont font)
    {
        float w = 0f;
        int lastGroup = -1;
        foreach (MainButtonDef def in visible)
        {
            int group = GroupOf(def);
            w += Width(def, labels, font) + 2f + (lastGroup >= 0 && group != lastGroup && JustDesktopMod.Settings.separators ? GroupGap : 0f);
            lastGroup = group;
        }
        return w;
    }

    // buttons with their own background get a little space around them
    private static Rect Inset(Rect rect)
    {
        return JustDesktopMod.Settings.back == BackMode.Icons ? rect.ContractedBy(2f * Scale, 3f * Scale) : rect;
    }

    public static bool IsOpen(MainButtonDef def)
    {
        return def == MainButtonDefOf.World ? WorldRendererUtility.WorldSelected : Windows.IsOpen(def);
    }

    // where the line goes: below or above, moved toward the icon by the offset setting
    public static Rect LineRect(float x, float width, float below, float above, float height)
    {
        Settings s = JustDesktopMod.Settings;
        float y = s.lineSide == LineSide.Above ? above + s.lineOffset : below - height - s.lineOffset;
        return new Rect(x, y, width, height);
    }

    // the line on each icon, brighter for the open one
    public static void Underline(Rect under, bool open)
    {
        if (open)
        {
            Widgets.DrawBoxSolid(under, Style.Accent);
        }
        else if (JustDesktopMod.Settings.lines)
        {
            Widgets.DrawBoxSolid(under, Style.Faded(0.55f));
        }
    }

    private static void DoStart(Rect rect)
    {
        MainButtonDef def = MainButtonDefOf.Architect;
        bool open = IsOpen(def);
        Rect back = Inset(rect);
        Style.ButtonBack(back, !open && Mouse.IsOver(rect));
        if (open)
        {
            Style.Box(back, Style.Faded(0.13f));
        }
        float icon = 24f * Scale;
        var (tex, line) = Style.TaskbarIconFor(def);
        GUI.color = def.Worker.Disabled ? Style.Disabled : line ? Style.Accent : Color.white;
        GUI.DrawTexture(new Rect(rect.center.x - icon / 2f, rect.center.y - icon / 2f, icon, icon), tex);
        GUI.color = Color.white;
        Click(rect, def);
    }

    private static void DoButton(Rect rect, MainButtonDef def, bool label, GameFont font = GameFont.Small)
    {
        MainButtonWorker worker = def.Worker;
        bool disabled = worker.Disabled;
        bool open = IsOpen(def);
        Rect back = Inset(rect);
        Style.ButtonBack(back, !open && !disabled && Mouse.IsOver(rect));
        if (open)
        {
            Style.Box(back, Style.Faded(0.13f));
        }
        float lineH = Mathf.Max(2f, Mathf.Round(2f * Scale));
        Underline(LineRect(rect.x + 3f, rect.width - 6f, rect.yMax, rect.y, lineH), open);
        float icon = 20f * Scale;
        float iconX = label ? rect.x + 10f * Scale : rect.center.x - icon / 2f;
        var (tex, line) = Style.TaskbarIconFor(def);
        // line icons take the colors, pictures stay as they are
        GUI.color = line ? (disabled ? Style.Disabled : open ? Style.Accent : Style.Text) : new Color(1f, 1f, 1f, disabled ? 0.4f : 1f);
        GUI.DrawTexture(new Rect(iconX, rect.center.y - icon / 2f, icon, icon), tex);
        GUI.color = disabled ? Style.Disabled : open ? Style.Accent : Style.Text;
        if (label)
        {
            Text.Font = font;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(iconX + icon + 8f * Scale, rect.y, rect.xMax - iconX - icon - 8f * Scale, rect.height), def.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
        float progress = worker.ButtonBarPercent;
        if (progress > 0f)
        {
            GUI.color = Style.Faded(0.7f);
            GUI.DrawTexture(new Rect(rect.x + 6f, rect.yMax - lineH - 3f, (rect.width - 12f) * Mathf.Clamp01(progress), 2f), BaseContent.WhiteTex);
        }
        GUI.color = Color.white;
        Click(rect, def);
    }

    public static void Click(Rect rect, MainButtonDef def, bool tooltip = true)
    {
        MainButtonWorker worker = def.Worker;
        if (worker.Disabled)
        {
            if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
            {
                Event.current.Use();
            }
            return;
        }
        if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && Mouse.IsOver(rect))
        {
            Event.current.Use();
            // on the game menu it opens this mod's options
            if (def == MainButtonDefOf.Menu)
            {
                Find.WindowStack.Add(new Dialog_ModSettings(LoadedModManager.GetMod<JustDesktopMod>()));
                return;
            }
            // right click puts the window back where new ones open, and opens it there
            WindowFrame.ResetPlace(def);
            if (Windows.IsOpen(def) && def.TabWindow != null)
            {
                Windows.BringToFront(def.TabWindow);
            }
            else
            {
                worker.InterfaceTryActivate();
            }
        }
        else if (Widgets.ButtonInvisible(rect))
        {
            worker.InterfaceTryActivate();
        }
        if (Mouse.IsOver(rect))
        {
            hovered = def;
            hoveredRect = rect;
        }
        if (!IsOpen(def) && !Find.WindowStack.NonImmediateDialogWindowOpen)
        {
            UIHighlighter.HighlightOpportunity(rect, def.cachedHighlightTagClosed);
        }
        if (tooltip && JustDesktopMod.Settings.hoverInfo == HoverInfo.Tooltip && Mouse.IsOver(rect))
        {
            string tip = def.LabelCap.Colorize(ColorLibrary.Yellow);
            if (def.hotKey != null && def.hotKey.MainKey != KeyCode.None)
            {
                tip += " (" + def.hotKey.MainKeyLabel + ")";
            }
            if (!def.description.NullOrEmpty())
            {
                tip += "\n\n" + def.description;
            }
            TooltipHandler.TipRegion(rect, tip);
        }
    }
}
