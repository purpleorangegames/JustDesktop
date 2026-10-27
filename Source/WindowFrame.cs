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

// title bar with maximize and close on the tab windows. Moves and maximize are
// remembered until the game is closed, nothing goes in the save.
public static class WindowFrame
{
    public const float TitleHeight = 30f;
    private const float ButtonWidth = 40f;

    private static readonly Dictionary<MainButtonDef, Vector2> moved = new();
    // where a window opened a bit above the one in front, only while it's open
    private static readonly Dictionary<MainButtonDef, Vector2> cascaded = new();
    private static readonly HashSet<MainButtonDef> maximized = new();
    private static MainTabWindow dragging;
    private static Vector2 dragOffset;

    private static readonly Action<Window> resetSize = AccessTools.MethodDelegate<Action<Window>>(AccessTools.Method(typeof(Window), "SetInitialSizeAndPosition"));

    // the selection pane, Architect (the start menu) and the game menu stay as they are
    public static bool Framed(Window w)
    {
        return JustDesktopMod.Settings.titleBars && w is MainTabWindow t && t.def != null
            && t is not MainTabWindow_Inspect && t is not MainTabWindow_Architect && t is not MainTabWindow_Menu;
    }

    private static readonly AccessTools.FieldRef<Dialog_ModSettings, Mod> settingsMod = AccessTools.FieldRefAccess<Dialog_ModSettings, Mod>("mod");

    // our options, the game's options window, the quests' reward preferences, saving and loading, and message boxes
    // (quit, review scenario, confirmations), and the pages of a new game (scenario, storyteller, world, colonists...)
    // and since then every other dialog of the game's (their class names start with Dialog_), and the pages
    public static bool Ours(Window w)
    {
        if (Loading.Ours(w) || w is Dialog_TaskbarSettings || w is Page)
        {
            return true;
        }
        // other mods' options keep their own look
        if (w is Dialog_ModSettings d)
        {
            return settingsMod(d) is JustDesktopMod;
        }
        string name = w.GetType().Name;
        return name.StartsWith("Dialog_") && !name.Contains("Debug");
    }

    // where windows sit: on the taskbar, raised by the option for a big dock
    // the windows whose square background we turned off for our rounded one
    public static readonly HashSet<Window> Carded = new();

    private static float MaxHeight => UI.screenHeight - Taskbar.Reserved;

    // called inside the window right after it worked out where its contents go
    public static Rect Content(Window w, Rect content)
    {
        // nothing of ours while the game is first starting, our textures aren't there yet
        if (!Loading.Ready)
        {
            return content;
        }
        ModernWidgets.Active = false;
        // the game menu and our options keep no title bar, but get the rounded background and the flat buttons
        if ((w is MainTabWindow_Menu || Ours(w)) && Modern)
        {
            // only the ones that had the game's background: the starting site page sits over the world, with none
            if (Carded.Contains(w))
            {
                Pane.CardBack(w.windowRect.AtZero());
            }
            ModernWidgets.Active = ModernWidgets.Enabled;
            return content;
        }
        if (!Framed(w))
        {
            return content;
        }
        ModernWidgets.Active = ModernWidgets.Enabled;
        KeepOnScreen(w);
        if (Modern)
        {
            // the game's background is off for these (see Window_WindowOnGUI), this one has rounded corners
            Rect all = w.windowRect.AtZero();
            GUI.color = JustDesktopMod.Settings.windowColor;
            Widgets.DrawAtlas(all, Style.Round);
            GUI.color = Style.Border;
            Widgets.DrawAtlas(all, Style.RoundLine);
            GUI.color = Color.white;
        }
        DrawBar((MainTabWindow)w, new Rect(0f, 0f, w.windowRect.width, TitleHeight));
        content.yMin += TitleHeight;
        return content;
    }

    // some windows change their own size while open (Research takes the whole width), so they can't run off the screen
    private static void KeepOnScreen(Window w)
    {
        Rect r = w.windowRect;
        if (r.xMax > UI.screenWidth)
        {
            r.x = Mathf.Max(0f, UI.screenWidth - r.width);
        }
        if (r.yMax > MaxHeight)
        {
            r.y = Mathf.Max(0f, MaxHeight - r.height);
        }
        w.windowRect = r;
    }

    public static bool Modern => JustDesktopMod.Settings.titleBars && JustDesktopMod.Settings.modernWindows;

    // the game sets the size and place again whenever a tab changes (pawn tables do it a lot)
    public static void AfterSize(MainTabWindow t)
    {
        // the game menu opens at the right; with windows opening in the middle it goes there too
        if (t is MainTabWindow_Menu && JustDesktopMod.Settings.openAt == OpenAt.Center)
        {
            t.windowRect.x = Mathf.Round((UI.screenWidth - t.windowRect.width) / 2f);
        }
        if (t is MainTabWindow_Menu)
        {
            t.windowRect.y = Mathf.Max(0f, MaxHeight - t.windowRect.height);
        }
        if (!Framed(t))
        {
            return;
        }
        Rect r = t.windowRect;
        r.height = Mathf.Min(r.height + TitleHeight, MaxHeight);
        r.y = MaxHeight - r.height;
        if (maximized.Contains(t.def))
        {
            r = new Rect(0f, 0f, UI.screenWidth, MaxHeight);
        }
        else if (moved.TryGetValue(t.def, out Vector2 p) || cascaded.TryGetValue(t.def, out p))
        {
            r.x = Mathf.Clamp(p.x, 0f, Mathf.Max(0f, UI.screenWidth - r.width));
            r.y = Mathf.Clamp(p.y, 0f, Mathf.Max(0f, MaxHeight - r.height));
        }
        else if (JustDesktopMod.Settings.openAt == OpenAt.Center)
        {
            // centered left to right, still sitting on the taskbar
            r.x = (UI.screenWidth - r.width) / 2f;
        }
        t.windowRect = r;
    }

    private static void ToggleMaximize(MainTabWindow t)
    {
        if (!maximized.Remove(t.def))
        {
            maximized.Add(t.def);
        }
        resetSize(t);
    }

    // a window opening while others are open goes a bit up and right of the one in front, unless it was moved before
    public static void Cascade(MainButtonDef def, MainTabWindow front)
    {
        if (JustDesktopMod.Settings.openAt != OpenAt.Cascade || front == null || moved.ContainsKey(def) || maximized.Contains(def) || !Framed(front))
        {
            return;
        }
        cascaded[def] = front.windowRect.position + new Vector2(36f, -36f);
    }

    public static void Closed(MainButtonDef def) => cascaded.Remove(def);

    // right click on the taskbar: forget where it was moved, back to where new windows open
    public static void ResetPlace(MainButtonDef def)
    {
        moved.Remove(def);
        maximized.Remove(def);
        cascaded.Remove(def);
        if (def.TabWindow is { IsOpen: true } window)
        {
            resetSize(window);
        }
    }

    public static void Maximize(MainTabWindow t)
    {
        if (!maximized.Contains(t.def))
        {
            ToggleMaximize(t);
        }
    }

    private static void DrawBar(MainTabWindow t, Rect bar)
    {
        if (Modern)
        {
            Widgets.DrawBoxSolid(new Rect(bar.x + 10f, bar.yMax - 1f, bar.width - 20f, 1f), Style.Border);
        }
        else
        {
            Widgets.DrawBoxSolid(bar, Style.Title);
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f), Style.Faded(0.45f));
        }
        Text.Font = GameFont.Small;
        bool mac = JustDesktopMod.Settings.style == BarStyle.Dock;
        Rect close, max, grip;
        if (mac)
        {
            // like macOS: close and maximize as colored dots on the left, the name in the middle
            close = new Rect(bar.x + 6f, bar.y, 22f, bar.height);
            max = new Rect(close.xMax, bar.y, 22f, bar.height);
            grip = new Rect(max.xMax, bar.y, bar.xMax - max.xMax, bar.height);
            var (tex, line) = Style.IconFor(t.def);
            float w = Text.CalcSize(t.def.LabelCap).x;
            float x = bar.center.x - (w + 24f) / 2f;
            GUI.color = line ? Style.Text : Color.white;
            GUI.DrawTexture(new Rect(x, bar.center.y - 9f, 18f, 18f), tex);
            GUI.color = Style.Text;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x + 24f, bar.y, w + 4f, bar.height), t.def.LabelCap);
        }
        else
        {
            close = new Rect(bar.xMax - ButtonWidth, bar.y, ButtonWidth, bar.height);
            max = new Rect(close.x - ButtonWidth, bar.y, ButtonWidth, bar.height);
            grip = new Rect(bar.x, bar.y, max.x - bar.x, bar.height);
            var (tex, line) = Style.IconFor(t.def);
            GUI.color = line ? Style.Accent : Color.white;
            GUI.DrawTexture(new Rect(bar.x + 10f, bar.center.y - 9f, 18f, 18f), tex);
            GUI.color = Style.Text;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(bar.x + 36f, bar.y, max.x - bar.x - 40f, bar.height), t.def.LabelCap);
        }
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;

        bool lights = mac && Mouse.IsOver(new Rect(close.x, bar.y, max.xMax - close.x, bar.height));
        if (mac ? Light(close, Style.MacClose, Style.Close, lights) : Glyph(close, Style.Close, Style.CloseHover))
        {
            Find.WindowStack.TryRemove(t);
            return;
        }
        Texture2D maxGlyph = maximized.Contains(t.def) ? Style.Restore : Style.Maximize;
        if (mac ? Light(max, Style.MacMaximize, maxGlyph, lights) : Glyph(max, maxGlyph, Style.Hover))
        {
            ToggleMaximize(t);
        }

        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(grip))
        {
            if (Event.current.clickCount == 2)
            {
                ToggleMaximize(t);
            }
            else
            {
                dragging = t;
                dragOffset = UI.MousePositionOnUIInverted - t.windowRect.position;
            }
            Event.current.Use();
        }
    }

    // a 12px colored dot, its symbol shows while the mouse is over either of them
    private static bool Light(Rect rect, Color color, Texture2D glyph, bool showGlyph)
    {
        Rect dot = new Rect(rect.center.x - 6f, rect.center.y - 6f, 12f, 12f);
        GUI.color = color;
        GUI.DrawTexture(dot, Style.Circle);
        if (showGlyph)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(dot.ContractedBy(2.5f), glyph);
        }
        GUI.color = Color.white;
        return Widgets.ButtonInvisible(rect);
    }

    private static bool Glyph(Rect rect, Texture2D tex, Color hover)
    {
        bool over = Mouse.IsOver(rect);
        if (over)
        {
            Widgets.DrawBoxSolid(rect, hover);
        }
        GUI.color = over ? Color.white : Style.Text;
        GUI.DrawTexture(new Rect(rect.center.x - 8f, rect.center.y - 8f, 16f, 16f), tex);
        GUI.color = Color.white;
        return Widgets.ButtonInvisible(rect);
    }

    // follows the mouse outside the window too, called from the taskbar every frame
    public static void UpdateDrag()
    {
        if (dragging == null)
        {
            return;
        }
        if (!Input.GetMouseButton(0) || !dragging.IsOpen)
        {
            dragging = null;
            return;
        }
        MainButtonDef def = dragging.def;
        if (maximized.Remove(def))
        {
            // dragging a maximized window puts it back to its size, under the mouse
            resetSize(dragging);
            dragOffset = new Vector2(dragging.windowRect.width / 2f, TitleHeight / 2f);
        }
        Vector2 p = UI.MousePositionOnUIInverted - dragOffset;
        p.x = Mathf.Clamp(p.x, 0f, Mathf.Max(0f, UI.screenWidth - dragging.windowRect.width));
        p.y = Mathf.Clamp(p.y, 0f, Mathf.Max(0f, MaxHeight - dragging.windowRect.height));
        dragging.windowRect.position = p;
        moved[def] = p;
    }
}

[HarmonyPatch(typeof(MainTabWindow), "SetInitialSizeAndPosition")]
public static class MainTabWindow_SetInitialSizeAndPosition
{
    public static void Postfix(MainTabWindow __instance) => WindowFrame.AfterSize(__instance);
}

// Window.InnerWindowOnGUI: right after "Rect rect3 = rect.ContractedBy(Margin);" the title bar is drawn
// and the content rect moved down under it
[HarmonyPatch(typeof(Window), "InnerWindowOnGUI")]
public static class Window_InnerWindowOnGUI
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo contracted = AccessTools.Method(typeof(GenUI), nameof(GenUI.ContractedBy), new[] { typeof(Rect), typeof(float) });
        MethodInfo hook = AccessTools.Method(typeof(WindowFrame), nameof(WindowFrame.Content));
        var list = new List<CodeInstruction>(instructions);
        bool done = false;
        for (int i = 0; i < list.Count; i++)
        {
            yield return list[i];
            if (!done && list[i].Calls(contracted) && i + 1 < list.Count && list[i + 1].IsStloc())
            {
                int local = list[i + 1].LocalIndex();
                yield return list[++i];
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return CodeInstruction.LoadLocal(local);
                yield return new CodeInstruction(OpCodes.Call, hook);
                yield return CodeInstruction.StoreLocal(local);
                done = true;
            }
        }
        if (!done)
        {
            Log.Error("[JustDesktop] could not find where windows place their contents, title bars are off");
        }
    }
}

[HarmonyPatch(typeof(Window), nameof(Window.WindowOnGUI))]
public static class Window_WindowOnGUI
{
    // the modern look draws its own background, the game's square one goes off for framed tab windows
    public static void Prefix(Window __instance)
    {
        if (!Loading.Ready)
        {
            return;
        }
        if (__instance is MainTabWindow_Inspect inspect)
        {
            // our card draws its own background and goes where the setting says, every frame since it can follow the pawn
            bool card = Pane.Shown;
            inspect.doWindowBackground = !card;
            if (card)
            {
                inspect.windowRect = Pane.CardRect;
            }
        }
        else if (__instance is WorldInspectPane world)
        {
            bool card = WorldCard.Shown;
            world.doWindowBackground = !card;
            if (card)
            {
                world.windowRect = WorldCard.Rect;
            }
        }
        else if (__instance is MainTabWindow_Architect)
        {
            __instance.doWindowBackground = !Architect.On;
            if (ArchitectSides.On)
            {
                ArchitectSides.Place(__instance);
            }
        }
        else if (__instance is MainTabWindow_Menu || WindowFrame.Ours(__instance))
        {
            if (WindowFrame.Modern)
            {
                if (__instance.doWindowBackground)
                {
                    WindowFrame.Carded.Add(__instance);
                    __instance.doWindowBackground = false;
                }
            }
            else if (WindowFrame.Carded.Remove(__instance))
            {
                __instance.doWindowBackground = true;
            }
        }
        else if (__instance is MainTabWindow && WindowFrame.Framed(__instance))
        {
            __instance.doWindowBackground = !WindowFrame.Modern;
        }
    }
}

[HarmonyPatch(typeof(Window), nameof(Window.PostClose))]
public static class Window_PostClose
{
    public static void Postfix(Window __instance)
    {
        // its background back as the game had it: pages are shown again when going back
        if (WindowFrame.Carded.Remove(__instance))
        {
            __instance.doWindowBackground = true;
        }
        if (__instance is MainTabWindow { def: not null } t)
        {
            WindowFrame.Closed(t.def);
        }
    }
}
