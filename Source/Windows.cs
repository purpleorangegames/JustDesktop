using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;

namespace JustDesktop;

// several tab windows open at once, like programs. The selection pane, Architect and the game menu
// ("system" tabs) keep the game's rules: only one of them at a time, and they close each other.
public static class Windows
{
    public static bool Several => JustDesktopMod.Settings.severalWindows;

    private static readonly AccessTools.FieldRef<WindowStack, List<Window>> stackWindows = AccessTools.FieldRefAccess<WindowStack, List<Window>>("windows");
    private static readonly Action<WindowStack, Window> insertWindow = AccessTools.MethodDelegate<Action<WindowStack, Window>>(AccessTools.Method(typeof(WindowStack), "InsertAtCorrectPositionInList"));
    private static readonly Action<MainTabsRoot, bool> autoCloseInspect = AccessTools.MethodDelegate<Action<MainTabsRoot, bool>>(AccessTools.Method(typeof(MainTabsRoot), "AutoCloseInspectionTabIfNothingSelected"));

    public static bool IsSystem(MainTabWindow w) => w is MainTabWindow_Inspect or MainTabWindow_Architect or MainTabWindow_Menu;

    // tabs that open a window of their own (World doesn't)
    public static bool IsProgram(MainButtonDef def) => def != null && def.tabWindowClass != null && !IsSystem(def.TabWindow);

    // the tab window in front, optionally only programs or only system tabs
    public static MainTabWindow Front(bool? programs = null)
    {
        List<Window> list = stackWindows(Find.WindowStack);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is MainTabWindow t && t.def != null && (programs == null || programs == !IsSystem(t)))
            {
                return t;
            }
        }
        return null;
    }

    public static void BringToFront(Window w)
    {
        WindowStack stack = Find.WindowStack;
        stackWindows(stack).Remove(w);
        insertWindow(stack, w);
        stack.Notify_ManuallySetFocus(w);
    }

    // what the parts of the game that ask "is a tab open?" get to see: program windows can stay open while
    // playing, so selecting, gizmos, the mouse readout and the world button only look at system tabs
    public static MainButtonDef SystemTab(MainTabsRoot root)
    {
        return Several ? Front(programs: false)?.def : root.OpenTab;
    }

    public static bool IsOpen(MainButtonDef def)
    {
        return Several && IsProgram(def) ? def.TabWindow.IsOpen : Find.MainTabsRoot.OpenTab == def;
    }

    private static void Close(MainTabWindow w, bool sound)
    {
        Find.WindowStack.TryRemove(w, sound);
    }

    // MainTabsRoot.ToggleTab with several windows
    public static void Toggle(MainTabsRoot root, MainButtonDef tab, bool playSound)
    {
        if (!IsProgram(tab))
        {
            MainTabWindow system = Front(programs: false);
            if (system != null && system.def == tab)
            {
                Close(system, false);
                if (playSound)
                {
                    SoundDefOf.TabClose.PlayOneShotOnCamera();
                }
                return;
            }
            if (system != null)
            {
                Close(system, false);
            }
            Open(tab, playSound);
            return;
        }
        MainTabWindow window = tab.TabWindow;
        if (window.IsOpen)
        {
            // like a taskbar: the one in front closes, one behind comes to the front
            if (Front() == window)
            {
                Close(window, false);
                if (playSound)
                {
                    SoundDefOf.TabClose.PlayOneShotOnCamera();
                }
            }
            else
            {
                Find.Selector.ClearSelection();
                BringToFront(window);
            }
            return;
        }
        // a program closes Architect, the menu and the selection pane, like the game does, and lets go of
        // what was selected, so its card and gizmos don't stay over the window
        Find.Selector.ClearSelection();
        MainTabWindow open = Front(programs: false);
        if (open != null)
        {
            Close(open, false);
        }
        WindowFrame.Cascade(tab, Front(programs: true));
        Open(tab, playSound);
    }

    private static void Open(MainButtonDef tab, bool playSound)
    {
        Find.WindowStack.Add(tab.TabWindow);
        if (playSound)
        {
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
        }
        if (TutorSystem.TutorialMode)
        {
            TutorSystem.Notify_Event("MainTab-" + tab.defName + "-Open");
        }
    }

    // MainTabsRoot.HandleLowPriorityShortcuts with several windows: clicks on the map that no window took
    public static void MapShortcuts(MainTabsRoot root)
    {
        autoCloseInspect(root, true);
        Event e = Event.current;
        if (Find.Selector.NumSelected == 0 && e.type == EventType.MouseDown && e.button == 1 && !WorldRendererUtility.WorldSelected && (!SteamDeck.IsSteamDeck || !Input.GetMouseButton(2)))
        {
            e.Use();
            MainButtonDefOf.Architect.Worker.InterfaceTryActivate();
        }
        if (e.type != EventType.MouseDown || e.button == 2)
        {
            return;
        }
        bool closed = false;
        MainTabWindow system = Front(programs: false);
        if (system != null && system.def != MainButtonDefOf.Inspect)
        {
            Close(system, true);
            closed = true;
        }
        switch (JustDesktopMod.Settings.mapClick)
        {
            case MapClick.CloseFront when !closed:
                MainTabWindow front = Front(programs: true);
                if (front != null)
                {
                    Close(front, true);
                    closed = true;
                }
                break;
            case MapClick.CloseAll:
                for (MainTabWindow w = Front(programs: true); w != null; w = Front(programs: true))
                {
                    Close(w, false);
                    closed = true;
                }
                break;
        }
        if (closed && e.button == 0)
        {
            Find.Selector.ClearSelection();
            Find.WorldSelector.ClearSelection();
        }
    }
}

[HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.OpenTab), MethodType.Getter)]
public static class MainTabsRoot_OpenTab
{
    // the window in front instead of whichever the stack finds first
    public static bool Prefix(ref MainButtonDef __result)
    {
        if (!Windows.Several)
        {
            return true;
        }
        __result = Windows.Front()?.def;
        return false;
    }
}

[HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.ToggleTab))]
public static class MainTabsRoot_ToggleTab
{
    public static bool Prefix(MainTabsRoot __instance, MainButtonDef newTab, bool playSound)
    {
        // closing ("escape the current tab") keeps the game's code, it closes the one in front
        if (!Windows.Several || newTab == null)
        {
            return true;
        }
        Windows.Toggle(__instance, newTab, playSound);
        return false;
    }
}

[HarmonyPatch(typeof(MainTabsRoot), nameof(MainTabsRoot.HandleLowPriorityShortcuts))]
public static class MainTabsRoot_HandleLowPriorityShortcuts
{
    public static bool Prefix(MainTabsRoot __instance)
    {
        if (!Windows.Several)
        {
            return true;
        }
        Windows.MapShortcuts(__instance);
        return false;
    }
}

// in these the open tab means a system tab, so open programs don't hide gizmos, the selection pane or the readout
[HarmonyPatch]
public static class SystemTabUsers
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        MethodBase[] methods =
        {
            AccessTools.Method(typeof(Selector), nameof(Selector.SelectorOnGUI)),
            AccessTools.Method(typeof(MapGizmoUtility), nameof(MapGizmoUtility.MapUIOnGUI)),
            AccessTools.PropertyGetter(typeof(MouseoverReadout), "ShouldShow"),
            AccessTools.Method(typeof(WorldInterface), "CheckOpenOrCloseInspectPane"),
            AccessTools.Method(typeof(MainButtonWorker_ToggleWorld), nameof(MainButtonWorker_ToggleWorld.Activate)),
            AccessTools.PropertyGetter(typeof(ITab), "StillValid"),
            AccessTools.Method(typeof(InspectPaneUtility), nameof(InspectPaneUtility.OpenTab)),
            AccessTools.Method(typeof(CameraDriver), "CalculateCurInputDollyVect")
        };
        foreach (MethodBase m in methods)
        {
            if (m != null)
            {
                yield return m;
            }
        }
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo openTab = AccessTools.PropertyGetter(typeof(MainTabsRoot), nameof(MainTabsRoot.OpenTab));
        MethodInfo systemTab = AccessTools.Method(typeof(Windows), nameof(Windows.SystemTab));
        foreach (CodeInstruction ins in instructions)
        {
            if (ins.Calls(openTab))
            {
                yield return new CodeInstruction(OpCodes.Call, systemTab).MoveLabelsFrom(ins);
            }
            else
            {
                yield return ins;
            }
        }
    }
}

// the selection pane draws its tab buttons (Log, Gear, Health...) under every window, which is fine when
// nothing else is open. With program windows open they would cover it, so the buttons are drawn again in
// a small window of their own on top, the same buttons in the same place.
[HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.ExtraOnGUI))]
public static class InspectTabsOnTop
{
    private static float TabWidth => RimHud.TabWidth(72f);
    private static readonly Action<InspectTabBase, IInspectPane> toggle = AccessTools.MethodDelegate<Action<InspectTabBase, IInspectPane>>(AccessTools.Method(typeof(InspectPaneUtility), "InterfaceToggleTab"));
    private static readonly List<InspectTabBase> tabs = new();
    private static IInspectPane drawing;

    public static void Postfix(IInspectPane pane)
    {
        if (!Windows.Several || !pane.AnythingSelected || pane.CurTabs == null || Windows.Front(programs: true) == null || (pane is MainTabWindow_Inspect && Pane.Shown))
        {
            return;
        }
        tabs.Clear();
        foreach (InspectTabBase tab in pane.CurTabs)
        {
            if (tab.IsVisible && !tab.Hidden)
            {
                tabs.Add(tab);
            }
        }
        if (tabs.Count == 0)
        {
            return;
        }
        float width = TabWidth * tabs.Count;
        Rect rect = new Rect(InspectPaneUtility.PaneWidthFor(pane) - width, pane.PaneTopY - 30f, width, 30f);
        drawing = pane;
        Find.WindowStack.ImmediateWindow(73924561, rect, WindowLayer.GameUI, DrawButtons, doBackground: false, absorbInputAroundWindow: false, 0f);
    }

    private static void DrawButtons()
    {
        float width = TabWidth * tabs.Count;
        for (int i = 0; i < tabs.Count; i++)
        {
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(width - TabWidth * (i + 1), 0f, TabWidth, 30f), tabs[i].labelKey.Translate()))
            {
                toggle(tabs[i], drawing);
            }
        }
    }
}
