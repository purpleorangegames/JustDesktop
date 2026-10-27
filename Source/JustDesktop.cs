using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace JustDesktop;

public enum BarStyle
{
    Taskbar,
    Dock
}

public enum BarAlign
{
    Left,
    Center,
    Right
}

public enum BackMode
{
    Bar,
    Icons,
    None
}

public enum LineSide
{
    Below,
    Above
}

public enum HoverInfo
{
    Tooltip,
    Name
}

public enum HoverLook
{
    Lighter,
    Solid,
    Clear
}

public enum OpenAt
{
    Cascade,
    GamePlace,
    Center
}

public enum MapClick
{
    KeepOpen,
    CloseFront,
    CloseAll
}

public class Settings : ModSettings
{
    public BarStyle style = BarStyle.Dock;
    public BarAlign align = BarAlign.Center;
    public float size = 2f;
    public bool labels = true;
    public bool magnify;
    public bool titleBars = true;
    public bool severalWindows;
    public float windowLift = 12f;
    public MapClick mapClick = MapClick.CloseAll;
    public OpenAt openAt = OpenAt.Center;
    public bool modernWindows = true;
    public bool modernWidgets = true;
    public bool paneOn = true;
    public PanePlace panePlace = PanePlace.Center;
    public float paneSize = 1f;
    public bool paneBars = true;
    public bool paneSkills = true;
    public SkillNames paneSkillNames = SkillNames.None;
    public bool paneActivity = true;
    public bool paneWithRimHud;
    public bool paneThings = true;
    public bool paneMulti = true;
    public bool modernGizmos = true;
    public bool builderPick = true;
    public bool gizmoGrid = true;
    public bool compactRight;
    public bool paneDraftedRow = true;
    public bool modernTabs = true;
    public bool modernArchitect = true;
    public bool modernResearch = true;
    public bool hudCards = true;
    public bool hudBesideDock = true;
    public bool hudSpeedAbove = true;
    public bool modernSpeed = true;
    public bool hideSuggested;
    public bool minimap;
    public float minimapScale = 1f;
    public bool minimapColor = true;
    public float hudOpacity = 0.84f;
    // resources the list at the top left leaves out, "thing:<defName>" or "cat:<defName>"
    public List<string> hiddenResources = new();
    public bool calendar = true;
    public bool calendarKeep;
    public bool notifications = true;
    public bool tradeAnimalGender = true;
    public bool tradeAnimalAge = true;
    public bool tradeColonistGender;
    public bool tradeColonistAge;
    public bool tradeFollow;
    public bool colonistBar = true;
    public float colonistBarSize = 1f;
    public float colonistBarZoom = 1.5f;
    public bool colonistBarMood = true;
    public Color colonistBarGroupHere = new(0.074f, 0.081f, 0.082f, 0.797f);
    public Color colonistBarGroupAway = new(0.644f, 0.644f, 0.644f, 0.165f);
    public bool colonistBarBarsBelow;
    public bool colonistBarBarBorder = true;
    public bool colonistBarHealth = true;
    public bool colonistBarIcons = true;
    public BarNames colonistBarNames = BarNames.Always;
    public bool architectCentered = true;
    public float architectItemSize = 54f;
    public bool architectNames;
    public bool architectShare = true;
    public List<string> architectFavorites = new();
    public List<string> architectRecent = new();
    public Color windowColor = new(0.102f, 0.102f, 0.102f, 1f);
    public bool textCustom;
    public Color textColor = new(0.86f, 0.9f, 0.94f, 1f);
    public bool separators;
    public BackMode back = BackMode.Icons;
    public Color backColor = new(0f, 0f, 0f, 0.455f);
    public bool lines = true;
    public Color lineColor = new(1f, 0.4f, 0.35f, 1f);
    public bool lineCustom;
    public LineSide lineSide = LineSide.Below;
    public HoverInfo hoverInfo = HoverInfo.Name;
    public HoverLook hoverLook = HoverLook.Solid;
    public float lineOffset = 5f;
    // icon picked for each button, by def name, one set per style
    public Dictionary<string, string> taskbarIcons = new();
    public Dictionary<string, string> dockIcons = new();

    public void ResetWindows()
    {
        var fresh = new Settings();
        titleBars = fresh.titleBars;
        severalWindows = fresh.severalWindows;
        windowLift = fresh.windowLift;
        mapClick = fresh.mapClick;
        openAt = fresh.openAt;
        modernWindows = fresh.modernWindows;
        modernWidgets = fresh.modernWidgets;
        windowColor = fresh.windowColor;
        textCustom = fresh.textCustom;
        textColor = fresh.textColor;
    }

    public void ResetTaskbar()
    {
        var fresh = new Settings();
        style = fresh.style;
        align = fresh.align;
        size = fresh.size;
        labels = fresh.labels;
        magnify = fresh.magnify;
        separators = fresh.separators;
        back = fresh.back;
        backColor = fresh.backColor;
        lines = fresh.lines;
        lineColor = fresh.lineColor;
        lineCustom = fresh.lineCustom;
        lineSide = fresh.lineSide;
        hoverInfo = fresh.hoverInfo;
        hoverLook = fresh.hoverLook;
        lineOffset = fresh.lineOffset;
    }

    public override void ExposeData()
    {
        Scribe_Values.Look(ref style, "style", BarStyle.Dock);
        Scribe_Values.Look(ref align, "align", BarAlign.Center);
        Scribe_Values.Look(ref size, "size", 2f);
        Scribe_Values.Look(ref labels, "labels", true);
        Scribe_Values.Look(ref magnify, "magnify", false);
        Scribe_Values.Look(ref titleBars, "titleBars", true);
        Scribe_Values.Look(ref severalWindows, "severalWindows", false);
        Scribe_Values.Look(ref windowLift, "windowLift", 12f);
        Scribe_Values.Look(ref mapClick, "mapClick", MapClick.CloseAll);
        Scribe_Values.Look(ref openAt, "openAt", OpenAt.Center);
        Scribe_Values.Look(ref modernWindows, "modernWindows", true);
        Scribe_Values.Look(ref modernWidgets, "modernWidgets", true);
        Scribe_Values.Look(ref paneOn, "paneOn", true);
        Scribe_Values.Look(ref panePlace, "panePlace", PanePlace.Center);
        Scribe_Values.Look(ref paneSize, "paneSize", 1f);
        Scribe_Values.Look(ref paneBars, "paneBars", true);
        Scribe_Values.Look(ref paneSkills, "paneSkills", true);
        Scribe_Values.Look(ref paneSkillNames, "paneSkillNames", SkillNames.None);
        Scribe_Values.Look(ref paneActivity, "paneActivity", true);
        Scribe_Values.Look(ref paneWithRimHud, "paneWithRimHud", false);
        Scribe_Values.Look(ref paneThings, "paneThings", true);
        Scribe_Values.Look(ref paneMulti, "paneMulti", true);
        Scribe_Values.Look(ref modernGizmos, "modernGizmos", true);
        Scribe_Values.Look(ref builderPick, "builderPick", true);
        Scribe_Values.Look(ref gizmoGrid, "gizmoGrid", true);
        Scribe_Values.Look(ref compactRight, "compactRight", false);
        Scribe_Values.Look(ref paneDraftedRow, "paneDraftedRow", true);
        Scribe_Values.Look(ref modernTabs, "modernTabs", true);
        Scribe_Values.Look(ref modernArchitect, "modernArchitect", true);
        Scribe_Values.Look(ref modernResearch, "modernResearch", true);
        Scribe_Values.Look(ref hudCards, "hudCards", true);
        Scribe_Values.Look(ref hudBesideDock, "hudBesideDock", true);
        Scribe_Values.Look(ref hudSpeedAbove, "hudSpeedAbove", true);
        Scribe_Values.Look(ref modernSpeed, "modernSpeed", true);
        Scribe_Values.Look(ref hideSuggested, "hideSuggested", false);
        Scribe_Values.Look(ref minimap, "minimap", false);
        Scribe_Values.Look(ref minimapScale, "minimapScale", 1f);
        Scribe_Values.Look(ref minimapColor, "minimapColor", true);
        Scribe_Values.Look(ref hudOpacity, "hudOpacity", 0.84f);
        Scribe_Collections.Look(ref hiddenResources, "hiddenResources", LookMode.Value);
        hiddenResources ??= new List<string>();
        Scribe_Values.Look(ref calendar, "calendar", true);
        Scribe_Values.Look(ref calendarKeep, "calendarKeep", false);
        Scribe_Values.Look(ref notifications, "notifications", true);
        Scribe_Values.Look(ref tradeAnimalGender, "tradeAnimalGender", true);
        Scribe_Values.Look(ref tradeAnimalAge, "tradeAnimalAge", true);
        Scribe_Values.Look(ref tradeColonistGender, "tradeColonistGender", false);
        Scribe_Values.Look(ref tradeColonistAge, "tradeColonistAge", false);
        Scribe_Values.Look(ref tradeFollow, "tradeFollow", false);
        Scribe_Values.Look(ref colonistBar, "colonistBar", true);
        Scribe_Values.Look(ref colonistBarSize, "colonistBarSize", 1f);
        Scribe_Values.Look(ref colonistBarZoom, "colonistBarZoom", 1.5f);
        Scribe_Values.Look(ref colonistBarMood, "colonistBarMood", true);
        Scribe_Values.Look(ref colonistBarGroupHere, "colonistBarGroupHere", new Color(0.074f, 0.081f, 0.082f, 0.797f));
        Scribe_Values.Look(ref colonistBarGroupAway, "colonistBarGroupAway", new Color(0.644f, 0.644f, 0.644f, 0.165f));
        Scribe_Values.Look(ref colonistBarBarsBelow, "colonistBarBarsBelow", false);
        Scribe_Values.Look(ref colonistBarBarBorder, "colonistBarBarBorder", true);
        Scribe_Values.Look(ref colonistBarHealth, "colonistBarHealth", true);
        Scribe_Values.Look(ref colonistBarIcons, "colonistBarIcons", true);
        Scribe_Values.Look(ref colonistBarNames, "colonistBarNames", BarNames.Always);
        Scribe_Values.Look(ref architectCentered, "architectCentered", true);
        Scribe_Values.Look(ref architectItemSize, "architectItemSize", 54f);
        Scribe_Values.Look(ref architectNames, "architectNames", false);
        Scribe_Values.Look(ref architectShare, "architectShare", true);
        Scribe_Collections.Look(ref architectFavorites, "architectFavorites", LookMode.Value);
        Scribe_Collections.Look(ref architectRecent, "architectRecent", LookMode.Value);
        architectFavorites ??= new List<string>();
        architectRecent ??= new List<string>();
        Scribe_Values.Look(ref windowColor, "windowColor", new Color(0.102f, 0.102f, 0.102f, 1f));
        Scribe_Values.Look(ref textCustom, "textCustom", false);
        Scribe_Values.Look(ref textColor, "textColor", new Color(0.86f, 0.9f, 0.94f, 1f));
        Scribe_Values.Look(ref separators, "separators", false);
        Scribe_Values.Look(ref back, "back", BackMode.Icons);
        Scribe_Values.Look(ref backColor, "backColor", new Color(0f, 0f, 0f, 0.455f));
        Scribe_Values.Look(ref lines, "lines", true);
        Scribe_Values.Look(ref lineColor, "lineColor", new Color(1f, 0.4f, 0.35f, 1f));
        Scribe_Values.Look(ref lineCustom, "lineCustom", false);
        Scribe_Values.Look(ref lineSide, "lineSide", LineSide.Below);
        Scribe_Values.Look(ref hoverInfo, "hoverInfo", HoverInfo.Name);
        Scribe_Values.Look(ref hoverLook, "hoverLook", HoverLook.Solid);
        Scribe_Values.Look(ref lineOffset, "lineOffset", 5f);
        Scribe_Collections.Look(ref taskbarIcons, "taskbarIcons", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref dockIcons, "dockIcons", LookMode.Value, LookMode.Value);
        taskbarIcons ??= new Dictionary<string, string>();
        dockIcons ??= new Dictionary<string, string>();
    }
}

public class JustDesktopMod : Mod
{
    public static Settings Settings;
    private static bool draggingWindow, draggingText, draggingGroupHere, draggingGroupAway;

    public JustDesktopMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<Settings>();
        new Harmony("tiagocc0.justdesktop").PatchAll();
    }

    public override string SettingsCategory() => "JustDesktop";

    private enum Page
    {
        Taskbar,
        Windows,
        Look,
        Gizmos,
        Architect,
        Pane,
        ColonistBar,
        Hud,
        Trade,
        Profiles
    }

    private static Page page;
    private static Vector2 scroll;
    private static float height;
    private static string search = "";
    private static bool indexed;
    // where a search result was clicked, lit up for a moment
    private static Page flashPage;
    private static float flashY, flashTime = -10f;

    private static readonly (Page page, string label)[] pages =
    {
        (Page.Taskbar, "Taskbar"), (Page.Windows, "Windows"), (Page.Look, "Window look"), (Page.Gizmos, "Gizmos"),
        (Page.Architect, "Architect"), (Page.Pane, "Selection pane"), (Page.ColonistBar, "Colonist bar"),
        (Page.Hud, "Screen edges"),
        (Page.Trade, "Caravans and trade"),
        (Page.Profiles, "Profiles")
    };

    // a list of pages on the left like a settings app, the one picked on the right
    public override void DoSettingsWindowContents(Rect inRect)
    {
        // the modern buttons, checkboxes and sliders in here too
        bool active = ModernWidgets.Active;
        ModernWidgets.Active = ModernWidgets.Enabled;
        try
        {
            Contents(inRect);
        }
        finally
        {
            ModernWidgets.Active = active;
        }
    }

    public override void WriteSettings()
    {
        base.WriteSettings();
        Colonists.Recache();
    }

    private static void Contents(Rect inRect)
    {
        Settings s = Settings;
        Rect nav = new Rect(inRect.x, inRect.y, 180f, inRect.height);
        Text.Font = GameFont.Small;
        SearchBox(new Rect(nav.x, nav.y, nav.width, 30f));
        float y = nav.y + 40f;
        foreach ((Page p, string label) in pages)
        {
            Rect r = new Rect(nav.x, y, nav.width, 34f);
            bool on = page == p && search.Length == 0, over = Mouse.IsOver(r);
            GUI.color = on ? Style.Faded(0.3f) : over ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.04f);
            Widgets.DrawAtlas(r, ModernWidgets.Fill);
            if (on)
            {
                GUI.color = Style.Accent;
                GUI.DrawTexture(new Rect(r.x + 4f, r.y + 8f, 3f, r.height - 16f), BaseContent.WhiteTex);
            }
            GUI.color = on || over ? Color.white : Style.Text;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(r.x + 16f, r.y, r.width - 20f, r.height), label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (Widgets.ButtonInvisible(r) && (page != p || search.Length > 0))
            {
                page = p;
                search = "";
                scroll = Vector2.zero;
            }
            y += r.height + 4f;
        }

        Rect outRect = new Rect(nav.xMax + 24f, inRect.y, inRect.width - nav.width - 24f, inRect.height);
        if (search.Length > 0)
        {
            Results(outRect, s);
            return;
        }
        Rect view = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(height, outRect.height));
        Widgets.BeginScrollView(outRect, ref scroll, view);
        // the listing starts a new column off to the side when it runs out of height, so it gets all it could need
        var list = new Listing_Standard();
        list.Begin(new Rect(0f, 0f, Mathf.Min(view.width, 620f), 99999f));
        Text.Font = GameFont.Medium;
        list.Label(Array.Find(pages, x => x.page == page).label);
        Text.Font = GameFont.Small;
        list.Gap(6f);
        float since = Time.realtimeSinceStartup - flashTime;
        if (flashPage == page && since < 1.6f)
        {
            GUI.color = Style.Faded(0.3f * Mathf.Clamp01(1.6f - since));
            Widgets.DrawAtlas(new Rect(-6f, flashY - 3f, list.ColumnWidth + 12f, 36f), ModernWidgets.Fill);
            GUI.color = Color.white;
        }
        DrawPage(page, list, s);
        height = list.CurHeight + 10f;
        list.End();
        Widgets.EndScrollView();
    }

    private static void DrawPage(Page p, Listing_Standard list, Settings s)
    {
        switch (p)
        {
            case Page.Taskbar: Dialog_TaskbarSettings.Left(list, s); list.GapLine(20f); Dialog_TaskbarSettings.Right(list, s); break;
            case Page.Windows: Windows(list, s); break;
            case Page.Look: Look(list, s); break;
            case Page.Gizmos: Gizmos(list, s); break;
            case Page.Architect: Architect(list, s); break;
            case Page.Pane: Pane(list, s); break;
            case Page.ColonistBar: ColonistBar(list, s); break;
            case Page.Hud: Hud(list, s); break;
            case Page.Trade: Trade(list, s); break;
            case Page.Profiles: Profiles.Draw(list); break;
        }
    }

    private static void SearchBox(Rect r)
    {
        GUI.SetNextControlName("JustDesktopSearch");
        string typed = Widgets.TextField(r, search);
        if (typed != search)
        {
            search = typed;
            scroll = Vector2.zero;
            if (search.Length == 0)
            {
                indexed = false;
            }
        }
        if (search.Length == 0 && GUI.GetNameOfFocusedControl() != "JustDesktopSearch")
        {
            GUI.color = Style.Dim;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(r.x + 8f, r.y, r.width - 8f, r.height), "Search");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        else if (search.Length > 0)
        {
            Rect clear = new Rect(r.xMax - 26f, r.y + 3f, 24f, 24f);
            GUI.color = Mouse.IsOver(clear) ? Color.white : Style.Dim;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(clear, "×");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (Widgets.ButtonInvisible(clear))
            {
                search = "";
                indexed = false;
                GUI.FocusControl(null);
            }
        }
    }

    // every page drawn once out of sight, while their labels are written down
    private static void BuildIndex(Settings s)
    {
        SettingsSearch.Index.Clear();
        Widgets.BeginGroup(new Rect(-20000f, -20000f, 620f, 99999f));
        try
        {
            foreach ((Page p, string _) in pages)
            {
                SettingsSearch.Recording = (int)p;
                var list = new Listing_Standard();
                list.Begin(new Rect(0f, 0f, 620f, 99999f));
                // the same start as a page has: its title and the gap under it
                Text.Font = GameFont.Medium;
                list.Gap(Text.LineHeight + list.verticalSpacing + 6f);
                Text.Font = GameFont.Small;
                try
                {
                    DrawPage(p, list, s);
                }
                finally
                {
                    list.End();
                }
            }
        }
        finally
        {
            SettingsSearch.Recording = -1;
            Text.Font = GameFont.Small;
            Widgets.EndGroup();
        }
        indexed = true;
    }

    private static void Results(Rect outRect, Settings s)
    {
        // only while repainting, so nothing on the hidden pages can take a click
        if (!indexed && Event.current.type == EventType.Repaint)
        {
            BuildIndex(s);
        }
        string query = search.Trim();
        var found = SettingsSearch.Index.Where(e => SettingsSearch.Matches(e, query)).GroupBy(e => (e.page, e.label)).Select(g => g.First()).ToList();
        Rect view = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, 44f + found.Count * 34f));
        Widgets.BeginScrollView(outRect, ref scroll, view);
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, 0f, view.width, 36f), "Search");
        Text.Font = GameFont.Small;
        float y = 44f;
        if (indexed && found.Count == 0)
        {
            GUI.color = Style.Dim;
            Widgets.Label(new Rect(0f, y, view.width, 30f), "Nothing found");
            GUI.color = Color.white;
        }
        float width = Mathf.Min(view.width, 620f);
        foreach (SettingsSearch.Entry e in found)
        {
            Rect r = new Rect(0f, y, width, 30f);
            string pageLabel = Array.Find(pages, x => (int)x.page == e.page).label;
            if (Mouse.IsOver(r))
            {
                Widgets.DrawHighlight(r);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            float pageW = Text.CalcSize(pageLabel).x + 8f;
            GUI.color = Style.Text;
            Widgets.Label(new Rect(r.x + 8f, r.y, r.width - pageW - 20f, r.height), e.label.Truncate(r.width - pageW - 20f));
            GUI.color = Style.Dim;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(r.xMax - pageW - 4f, r.y, pageW, r.height), pageLabel);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (!e.tip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(r, e.tip);
            }
            if (Widgets.ButtonInvisible(r))
            {
                page = (Page)e.page;
                search = "";
                indexed = false;
                GUI.FocusControl(null);
                scroll = new Vector2(0f, Mathf.Max(0f, e.y - 80f));
                flashPage = page;
                flashY = e.y;
                flashTime = Time.realtimeSinceStartup;
            }
            y += 34f;
        }
        Widgets.EndScrollView();
    }

    private static void Windows(Listing_Standard list, Settings s)
    {
        list.CheckboxLabeled("Several windows open at once", ref s.severalWindows, "Like an OS: opening a tab doesn't close the others, clicking an open one in the taskbar brings it to the front. Off is the game's way, one at a time");
        s.windowLift = Mathf.Round(list.SliderLabeled("Windows and the selection sit this far above the taskbar: " + s.windowLift + " px", s.windowLift, 0f, 150f, 0.5f, "For a big dock that reaches above the bar: the windows, the selection card, its gizmos and the Architect menu move up. Windows already open move when opened again"));
        list.CheckboxLabeled("Title bar on the tab windows", ref s.titleBars, "Maximize and close buttons, and you can drag the window by it");
        if (s.titleBars)
        {
            list.Gap(6f);
            list.Label("New windows open", tooltip: "A window you moved opens where you left it. Right click its button on the taskbar to put it back");
            if (list.RadioButton("A bit above the one in front", s.openAt == OpenAt.Cascade, 12f))
            {
                s.openAt = OpenAt.Cascade;
            }
            if (list.RadioButton("Where the game puts them", s.openAt == OpenAt.GamePlace, 12f))
            {
                s.openAt = OpenAt.GamePlace;
            }
            if (list.RadioButton("Centered, above the taskbar", s.openAt == OpenAt.Center, 12f))
            {
                s.openAt = OpenAt.Center;
            }
        }
        if (s.severalWindows)
        {
            list.Gap(6f);
            list.Label("Clicking the map");
            if (list.RadioButton("Keeps the windows open", s.mapClick == MapClick.KeepOpen, 12f))
            {
                s.mapClick = MapClick.KeepOpen;
            }
            if (list.RadioButton("Closes the window in front", s.mapClick == MapClick.CloseFront, 12f))
            {
                s.mapClick = MapClick.CloseFront;
            }
            if (list.RadioButton("Closes all of them", s.mapClick == MapClick.CloseAll, 12f, "Like the game does with its one window"))
            {
                s.mapClick = MapClick.CloseAll;
            }
        }
        list.Gap();
        if (list.ButtonText("Reset the window options"))
        {
            s.ResetWindows();
        }
    }

    private static void Look(Listing_Standard list, Settings s)
    {
        list.Label("Accent color", tooltip: "Lines, highlights and selected things everywhere in the mod; the same as on the Taskbar page");
        Dialog_TaskbarSettings.Accent(list, s);
        list.GapLine(20f);
        if (!s.titleBars)
        {
            GUI.color = Style.Dim;
            list.Label("Needs the title bar on the tab windows");
            GUI.color = Color.white;
        }
        else
        {
            list.CheckboxLabeled("Modern look", ref s.modernWindows, "Flat background with rounded corners, the title bar blends into the window");
            if (s.modernWindows)
            {
                list.CheckboxLabeled("Modern buttons, checkboxes and tabs inside", ref s.modernWidgets, "Only in the tab windows, the rest of the game keeps its look");
                if (s.modernWidgets)
                {
                    list.CheckboxLabeled("Research window redone", ref s.modernResearch, "The whole height of the screen, projects as cards, a queue (Shift+click a project) with the time left");
                }
                list.Gap(6f);
                list.Label("Window color");
                Dialog_TaskbarSettings.ColorEditor(list, ref s.windowColor, ref draggingWindow);
                list.Gap(6f);
                list.CheckboxLabeled("Text color", ref s.textCustom, "The text in the modern windows and on the cards at the screen edges in a color of your own. The dimmer text goes see-through so it stays dimmer");
                if (s.textCustom)
                {
                    Dialog_TaskbarSettings.ColorEditor(list, ref s.textColor, ref draggingText);
                }
            }
        }
    }

    private static void Gizmos(Listing_Standard list, Settings s)
    {
        list.CheckboxLabeled("Modern gizmos", ref s.modernGizmos, "The buttons next to the selection pane and the Architect's build buttons as flat rounded tiles, their pictures stay");
        list.CheckboxLabeled("In a grid next to the card", ref s.gizmoGrid, "Small squares three high, filled from the bottom up and then to the right; the name shows when you hover one. A building's room goes above the card. Needs the smaller selection pane");
        if (s.gizmoGrid)
        {
            list.CheckboxLabeled("  Hemogen, deathrest and other bars always with the gizmos", ref s.compactRight, "Off puts them at the left of the card, and only with the gizmos while a tab panel (Needs, Health...) is open there. On the world map a caravan's mass, speed and food go there the same way");
            list.CheckboxLabeled("  One row in the middle when drafted", ref s.paneDraftedRow, "With a drafted pawn selected the card goes to the left side and the gizmos line up in one row in the middle, above the dock, spreading out to both sides");
        }
        list.CheckboxLabeled("Pick who builds", ref s.builderPick, "A button on the plan of anything that gets a quality (and on its frame) to pick the colonist that builds it, from the ones with construction on in the work tab. Only they build it then, anyone can still bring the materials. Kept with the save");
    }

    private static void Architect(Listing_Standard list, Settings s)
    {
        list.CheckboxLabeled("Modern Architect menu", ref s.modernArchitect, "The categories as rounded buttons with icons, Favorites and Recent at the top, and the menu and its info box on rounded cards");
        if (s.modernArchitect)
        {
            if (list.RadioButton("Items along the bottom, like the game", !s.architectCentered, 12f))
            {
                s.architectCentered = false;
            }
            if (list.RadioButton("Menu in the middle, items to its sides", s.architectCentered, 12f, "Left click a category to open it on the left, right click to open one on the right, click again to close it. With one open its items fill both sides"))
            {
                s.architectCentered = true;
            }
            if (s.architectCentered)
            {
                s.architectItemSize = Mathf.Round(list.SliderLabeled("Item size: " + s.architectItemSize.ToString("0"), s.architectItemSize, 40f, 96f, 0.35f));
                list.CheckboxLabeled("Item names under them", ref s.architectNames, "Off shows the name only when you hover an item");
                list.CheckboxLabeled("One category open uses both sides", ref s.architectShare, "Its items are shared between the left and the right. Off keeps them on the side it was opened on");
            }
        }
    }

    private static void Pane(Listing_Standard list, Settings s)
    {
        list.CheckboxLabeled("Smaller selection pane", ref s.paneOn, "Health, mood and needs as bars, skills, and the tabs as small icons. Several things selected at once keep the game's pane");
        if (s.paneOn)
        {
            if (list.RadioButton("Where the game puts it", s.panePlace == PanePlace.GamePlace, 12f))
            {
                s.panePlace = PanePlace.GamePlace;
            }
            if (list.RadioButton("Centered, above the taskbar", s.panePlace == PanePlace.Center, 12f))
            {
                s.panePlace = PanePlace.Center;
            }
            if (list.RadioButton("Next to what's selected", s.panePlace == PanePlace.NearPawn, 12f))
            {
                s.panePlace = PanePlace.NearPawn;
            }
            s.paneSize = Mathf.Round(list.SliderLabeled("Size: " + s.paneSize.ToStringPercent(), s.paneSize, 0.8f, 1.6f, 0.35f) * 20f) / 20f;
            list.CheckboxLabeled("Health, mood and needs", ref s.paneBars);
            list.CheckboxLabeled("Skills", ref s.paneSkills);
            if (s.paneSkills)
            {
                if (list.RadioButton("Icons and full names", s.paneSkillNames == SkillNames.Full, 24f))
                {
                    s.paneSkillNames = SkillNames.Full;
                }
                if (list.RadioButton("Icons and the first three letters", s.paneSkillNames == SkillNames.Short, 24f))
                {
                    s.paneSkillNames = SkillNames.Short;
                }
                if (list.RadioButton("Only icons, bigger", s.paneSkillNames == SkillNames.None, 24f, "The name shows when you hover a skill"))
                {
                    s.paneSkillNames = SkillNames.None;
                }
            }
            list.CheckboxLabeled("Title and what they are doing", ref s.paneActivity);
            list.CheckboxLabeled("Buildings, items and plants too", ref s.paneThings, "Hit points, fuel or growth as bars, what the game says about it, and its tabs as small icons");
            list.CheckboxLabeled("Several selected too", ref s.paneMulti, "A row with health and mood for each pawn (click a name to select only them), and the hit points of the rest together");
            if (RimHud.Loaded)
            {
                list.CheckboxLabeled("Use it with RimHUD too", ref s.paneWithRimHud, "Off leaves the pane to RimHUD");
            }
            list.CheckboxLabeled("Modern tab panels", ref s.modernTabs, "Needs, Character, Health, Social, Gear and Log next to the card, in its style and laid out anew");
        }
    }

    private static void Hud(Listing_Standard list, Settings s)
    {
        list.CheckboxLabeled("Minimap", ref s.minimap, "A button at the top right opens a map of where you are: the ground, water, rock and what's built, without pawns or items. Click or drag on it to move the camera there, right click it or the button to close it");
        if (s.minimap)
        {
            list.CheckboxLabeled("  Minimap in color", ref s.minimapColor, "Off: black, with rock and buildings in white");
        }
        list.CheckboxLabeled("Modern speed buttons", ref s.modernSpeed, "Flat icons for pause and the speeds. While a window pauses the game, the speeds you can't pick are dimmed instead of crossed out with a line");
        list.CheckboxLabeled("Cards behind them", ref s.hudCards, "Game speed, date, weather, the toggles, letters, alerts, resources, messages and what's under the mouse each on a rounded card in the window color");
        if (s.hudCards)
        {
            s.hudOpacity = Mathf.Round(list.SliderLabeled("Card opacity: " + s.hudOpacity.ToStringPercent(), s.hudOpacity, 0.2f, 1f, 0.35f) * 50f) / 50f;
            list.CheckboxLabeled("Next to the dock", ref s.hudBesideDock, "Only with the dock in the middle: the toggles, game speed and date at its right, the weather and temperature at its left");
            list.CheckboxLabeled("Game speed above the dock", ref s.hudSpeedAbove, "The speed buttons on their own card in the middle of the screen, right above the dock or taskbar");
            list.CheckboxLabeled("Notification center", ref s.notifications, "A bell under the letters: the letters waiting now, and the ones already opened or dismissed from today and yesterday (at least the last ten), to open again or put back. They come from the same archive as the calendar");
            list.CheckboxLabeled("Calendar when clicking the date", ref s.calendar, "The quadrum's 15 days, with the days something happened colored by its letter.\n\nThose come from the game's archive (History > Messages), which keeps only about 200 entries, messages included, and lets go of the oldest. So earlier quadrums can look emptier than they were. Pinned letters stay");
            if (s.calendar)
            {
                list.CheckboxLabeled("  Keep its events in the save", ref s.calendarKeep, "A copy of each letter (its day, title, kind and text) is kept in your save as it arrives, so the calendar still shows the days the game's archive let go of, and you can open them again.\n\nOnly letters from when this is on, plus the ones still in the archive when you turn it on. Off stops keeping new ones, the ones kept stay");
            }
        }
    }

    private static void Trade(Listing_Standard list, Settings s)
    {
        if (!Transfer.On)
        {
            GUI.color = Style.Dim;
            list.Label("Animals Gender On Caravan is active, it shows these instead. Turn it off to use the ones here");
            GUI.color = Color.white;
            list.Gap(6f);
        }
        list.Label("Icons on the rows when forming a caravan, loading transport pods and trading");
        list.CheckboxLabeled("Animals' gender", ref s.tradeAnimalGender);
        list.CheckboxLabeled("Animals' age (baby, young, adult)", ref s.tradeAnimalAge);
        list.CheckboxLabeled("Animals that follow their master when drafted", ref s.tradeFollow);
        list.CheckboxLabeled("Colonists' gender", ref s.tradeColonistGender);
        list.CheckboxLabeled("Colonists' age", ref s.tradeColonistAge);
    }

    private static void ColonistBar(Listing_Standard list, Settings s)
    {
        bool was = s.colonistBar;
        list.CheckboxLabeled("Modern colonist bar", ref s.colonistBar, "Each colonist a rounded tile with thin mood and health bars, status icons as small badges and the name in a pill. Turn it off to use the game's bar or another mod's");
        if (s.colonistBar)
        {
            float size = Mathf.Round(list.SliderLabeled("Size: " + s.colonistBarSize.ToStringPercent(), s.colonistBarSize, 0.6f, 1.6f, 0.35f, "The biggest it gets; with many colonists it still shrinks to fit") * 20f) / 20f;
            if (size != s.colonistBarSize)
            {
                s.colonistBarSize = size;
                Colonists.Recache();
            }
            s.colonistBarZoom = Mathf.Round(list.SliderLabeled("Zoom on the face: " + s.colonistBarZoom.ToStringPercent(), s.colonistBarZoom, 1f, 2.5f, 0.35f, "100% shows them like the game, from the shoulders up and over the top of the tile. Higher brings the face to the middle of the tile, closer") * 20f) / 20f;
            list.CheckboxLabeled("Mood bar", ref s.colonistBarMood, "Marked where mental breaks start; it turns orange and red, with a glow around the tile, as one gets close");
            list.CheckboxLabeled("Health bar when hurt or sick", ref s.colonistBarHealth);
            if (s.colonistBarMood || s.colonistBarHealth)
            {
                list.CheckboxLabeled("  Bars under the tile", ref s.colonistBarBarsBelow, "Off keeps them inside the tile, along its bottom");
                list.CheckboxLabeled("  Black border around the bars", ref s.colonistBarBarBorder, "Easier to see over the map");
            }
            list.CheckboxLabeled("Status badges", ref s.colonistBarIcons, "Sleeping, idle, fighting, burning, inspired, their role...");
            list.Gap(6f);
            list.Label("Names");
            if (list.RadioButton("Always", s.colonistBarNames == BarNames.Always, 12f))
            {
                s.colonistBarNames = BarNames.Always;
            }
            if (list.RadioButton("When hovering a colonist", s.colonistBarNames == BarNames.Hover, 12f))
            {
                s.colonistBarNames = BarNames.Hover;
            }
            if (list.RadioButton("Never", s.colonistBarNames == BarNames.Never, 12f))
            {
                s.colonistBarNames = BarNames.Never;
            }
            list.Gap();
            list.Label("Background of the colony you're looking at", tooltip: "The panel behind each group of colonists");
            Dialog_TaskbarSettings.ColorEditor(list, ref s.colonistBarGroupHere, ref draggingGroupHere);
            list.Gap();
            list.Label("Background of other maps and caravans");
            Dialog_TaskbarSettings.ColorEditor(list, ref s.colonistBarGroupAway, ref draggingGroupAway);
            if (list.ButtonText("Reset the background colors"))
            {
                var fresh = new Settings();
                s.colonistBarGroupHere = fresh.colonistBarGroupHere;
                s.colonistBarGroupAway = fresh.colonistBarGroupAway;
            }
        }
        // the game works out the bar's size only when something changes
        if (was != s.colonistBar)
        {
            Colonists.Recache();
        }
    }
}

// everything about how the taskbar and dock look, in its own window so the mod options stay short
public class Dialog_TaskbarSettings : Window
{
    private static bool draggingBack, draggingLine;

    private static readonly List<Color> lineColors = new()
    {
        new Color(0.31f, 0.82f, 0.91f), new Color(0.35f, 0.6f, 1f), new Color(0.6f, 0.45f, 1f), new Color(1f, 0.45f, 0.75f),
        new Color(1f, 0.4f, 0.35f), new Color(1f, 0.65f, 0.25f), new Color(1f, 0.85f, 0.3f), new Color(0.45f, 0.85f, 0.4f),
        new Color(0.9f, 0.92f, 0.95f), new Color(0.55f, 0.6f, 0.66f)
    };

    public override Vector2 InitialSize => new(900f, 760f);

    public Dialog_TaskbarSettings()
    {
        doCloseButton = true;
        doCloseX = true;
        absorbInputAroundWindow = true;
        optionalTitle = "Taskbar and dock";
    }

    public override void PostClose()
    {
        base.PostClose();
        JustDesktopMod.Settings.Write();
    }

    public override void DoWindowContents(Rect inRect)
    {
        inRect.height -= CloseButSize.y + 10f;
        Settings s = JustDesktopMod.Settings;
        float half = (inRect.width - 30f) / 2f;
        bool active = ModernWidgets.Active;
        ModernWidgets.Active = ModernWidgets.Enabled;
        var list = new Listing_Standard();
        list.Begin(new Rect(inRect.x, inRect.y, half, inRect.height));
        Left(list, s);
        list.End();
        list = new Listing_Standard();
        list.Begin(new Rect(inRect.x + half + 30f, inRect.y, half, inRect.height));
        Right(list, s);
        list.End();
        ModernWidgets.Active = active;
    }

    // what the bar is and does, here and on the Taskbar page of the mod options
    public static void Left(Listing_Standard list, Settings s)
    {
        list.Label("Style");
        if (list.RadioButton("Taskbar", s.style == BarStyle.Taskbar, 12f, "A bar across the bottom with the names next to the icons, Architect is the start button"))
        {
            s.style = BarStyle.Taskbar;
        }
        if (list.RadioButton("Dock", s.style == BarStyle.Dock, 12f, "Only the icons, the name shows when you hover them"))
        {
            s.style = BarStyle.Dock;
        }
        list.Gap();
        list.Label("Position");
        if (list.RadioButton("Left", s.align == BarAlign.Left, 12f))
        {
            s.align = BarAlign.Left;
        }
        if (list.RadioButton("Center", s.align == BarAlign.Center, 12f))
        {
            s.align = BarAlign.Center;
        }
        if (list.RadioButton("Right", s.align == BarAlign.Right, 12f))
        {
            s.align = BarAlign.Right;
        }
        list.Gap();
        s.size = Mathf.Round(list.SliderLabeled("Size: " + s.size.ToStringPercent(), s.size, 0.75f, 2f, 0.35f) * 20f) / 20f;
        list.Gap();
        if (s.style == BarStyle.Taskbar)
        {
            list.CheckboxLabeled("Show the names next to the icons", ref s.labels, "Off shows only the icons, the name shows when you hover them");
        }
        else
        {
            list.CheckboxLabeled("Icons grow under the mouse", ref s.magnify);
        }
        list.CheckboxLabeled("Lines between the groups", ref s.separators);
        list.Gap();
        list.Label("When the mouse is over a button");
        if (list.RadioButton("Show the game's tooltip", s.hoverInfo == HoverInfo.Tooltip, 12f, "The name, shortcut and description, like the normal bottom bar"))
        {
            s.hoverInfo = HoverInfo.Tooltip;
        }
        if (list.RadioButton("Show only the name and shortcut", s.hoverInfo == HoverInfo.Name, 12f, "A small label above the button"))
        {
            s.hoverInfo = HoverInfo.Name;
        }
        list.Gap(6f);
        bool perIcon = s.back == BackMode.Icons;
        if (list.RadioButton("Button gets lighter", s.hoverLook == HoverLook.Lighter, 12f))
        {
            s.hoverLook = HoverLook.Lighter;
        }
        if (list.RadioButton("Its background gets fully solid", s.hoverLook == HoverLook.Solid, 12f, perIcon ? null : "Only with a background behind each icon", null, !perIcon))
        {
            s.hoverLook = HoverLook.Solid;
        }
        if (list.RadioButton("Its background disappears", s.hoverLook == HoverLook.Clear, 12f, perIcon ? null : "Only with a background behind each icon", null, !perIcon))
        {
            s.hoverLook = HoverLook.Clear;
        }
        list.Gap();
        list.Label("Icons");
        Rect iconRow = list.GetRect(30f);
        if (Widgets.ButtonText(new Rect(iconRow.x, iconRow.y, iconRow.width / 2f - 4f, iconRow.height), "Taskbar icons"))
        {
            Find.WindowStack.Add(new Dialog_Icons(BarStyle.Taskbar));
        }
        if (Widgets.ButtonText(new Rect(iconRow.x + iconRow.width / 2f + 4f, iconRow.y, iconRow.width / 2f - 4f, iconRow.height), "Dock icons"))
        {
            Find.WindowStack.Add(new Dialog_Icons(BarStyle.Dock));
        }
        list.Gap();
        if (list.ButtonText("Reset taskbar settings"))
        {
            s.ResetTaskbar();
        }
    }

    // how it looks
    public static void Right(Listing_Standard list, Settings s)
    {
        list.Label("Background");
        if (list.RadioButton("Behind the whole bar", s.back == BackMode.Bar, 12f))
        {
            s.back = BackMode.Bar;
        }
        if (list.RadioButton("Behind each icon", s.back == BackMode.Icons, 12f))
        {
            s.back = BackMode.Icons;
        }
        if (list.RadioButton("None", s.back == BackMode.None, 12f, "Only the icons over the map"))
        {
            s.back = BackMode.None;
        }
        if (s.back != BackMode.None)
        {
            ColorEditor(list, ref s.backColor, ref draggingBack);
        }
        list.Gap();
        list.CheckboxLabeled("Line on each icon", ref s.lines, "The open one always gets one, brighter");
        Rect sides = list.GetRect(24f);
        if (Widgets.RadioButtonLabeled(new Rect(sides.x + 12f, sides.y, sides.width / 2f - 20f, sides.height), "Below the icon", s.lineSide == LineSide.Below))
        {
            s.lineSide = LineSide.Below;
        }
        if (Widgets.RadioButtonLabeled(new Rect(sides.x + sides.width / 2f + 8f, sides.y, sides.width / 2f - 8f, sides.height), "Above the icon", s.lineSide == LineSide.Above))
        {
            s.lineSide = LineSide.Above;
        }
        s.lineOffset = Mathf.Round(list.SliderLabeled("Move it toward the icon: " + s.lineOffset + " px", s.lineOffset, 0f, 16f, 0.5f));
        list.Label("Accent color (lines, highlights, selected things)");
        Accent(list, s);
    }

    // the accent: used for the taskbar's lines and everything highlighted or selected in the mod
    public static void Accent(Listing_Standard list, Settings s)
    {
        Rect swatches = list.GetRect(26f);
        Color picked = s.lineColor;
        if (Widgets.ColorSelector(swatches, ref picked, lineColors, out _))
        {
            s.lineColor = picked;
            s.lineCustom = false;
        }
        if (list.RadioButton("Custom color", s.lineCustom, 12f))
        {
            s.lineCustom = true;
        }
        if (s.lineCustom)
        {
            ColorEditor(list, ref s.lineColor, ref draggingLine);
        }
    }

    // a color wheel with a preview, brightness and opacity sliders under it
    public static void ColorEditor(Listing_Standard list, ref Color color, ref bool dragging)
    {
        Rect row = list.GetRect(110f);
        Color.RGBToHSV(color, out float h, out float sat, out float v);
        Color hue = Color.HSVToRGB(h, sat, 1f);
        Widgets.HSVColorWheel(new Rect(row.x + 12f, row.y, 110f, 110f), ref hue, ref dragging, 1f);
        Color.RGBToHSV(hue, out h, out sat, out _);
        Rect preview = new Rect(row.x + 140f, row.y + 15f, 80f, 80f);
        Widgets.DrawBoxSolid(preview, Color.gray);
        Widgets.DrawBoxSolid(preview, color);
        Widgets.DrawBox(preview);
        list.Gap(4f);
        v = list.SliderLabeled("Brightness: " + v.ToStringPercent(), v, 0f, 1f, 0.35f);
        float alpha = list.SliderLabeled("Opacity: " + color.a.ToStringPercent(), color.a, 0f, 1f, 0.35f);
        Color result = Color.HSVToRGB(h, sat, v);
        result.a = alpha;
        color = result;
    }
}
