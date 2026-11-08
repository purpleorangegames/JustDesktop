using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// a minimize button at the top right of an opened letter: the window closes and the letter stays on the side of the
// screen, to read again later. The game already keeps it when its window closes without picking one of its buttons
public static class LetterMinimize
{
    // the letter whose window is being opened right now, and the windows letters opened
    public static Letter Opening;
    private static readonly Dictionary<Window, Letter> windows = new();

    public static void Opened(Window w)
    {
        if (Opening != null)
        {
            windows[w] = Opening;
        }
    }

    public static void Closed(Window w) => windows.Remove(w);

    private static bool Mac => JustDesktopMod.Settings.style == BarStyle.Dock;

    // like the program windows' title bar: with the dock a yellow dot at the top left, where macOS has it, kept above
    // the letter's text; with the taskbar a line icon at the top right, left of the window's own X when it has one
    private static Rect Button(Window w) => Mac
        ? new Rect(6f, 2f, 22f, 20f)
        : new Rect(w.windowRect.width - 34f - (w.doCloseX ? 26f : 0f), 0f, 34f, 18f);

    private static bool Has(Window w) => windows.TryGetValue(w, out Letter letter) && Find.LetterStack.LettersListForReading.Contains(letter);

    // the click is taken before the window draws, like the game's own close X: by the end of its drawing the window
    // has used up every click inside it
    public static void Click(Window w)
    {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && Has(w) && Button(w).Contains(e.mousePosition))
        {
            e.Use();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            w.Close(false);
        }
    }

    public static void Draw(Window w)
    {
        if (Event.current.type != EventType.Repaint || !Has(w))
        {
            return;
        }
        Rect r = Button(w);
        bool over = Mouse.IsOver(r);
        if (Mac)
        {
            // a 12px dot, its symbol shows with the mouse over it
            Rect dot = new Rect(r.center.x - 6f, r.center.y - 6f, 12f, 12f);
            GUI.color = Style.MacMinimize;
            GUI.DrawTexture(dot, Style.Circle);
            if (over)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(dot.ContractedBy(2.5f), Style.Minimize);
            }
        }
        else
        {
            if (over)
            {
                Widgets.DrawBoxSolid(r, Style.Hover);
            }
            GUI.color = over ? Color.white : Style.Text;
            GUI.DrawTexture(new Rect(r.center.x - 8f, r.center.y - 8f, 16f, 16f), Style.Minimize);
        }
        GUI.color = Color.white;
        TooltipHandler.TipRegion(r, "Minimize: the letter goes back to the side of the screen, to read later");
    }
}

// every letter kind's OpenLetter, so the window it adds is known to be that letter's
[HarmonyPatch]
public static class LetterMinimize_Open
{
    public static IEnumerable<MethodBase> TargetMethods() => typeof(Letter).AllSubclasses().Concat(new[] { typeof(Letter) })
        .Select(t => t.GetMethod(nameof(Letter.OpenLetter), BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        .Where(m => m != null && !m.IsAbstract).Distinct();

    public static void Prefix(Letter __instance) => LetterMinimize.Opening = __instance;

    public static void Finalizer() => LetterMinimize.Opening = null;
}

[HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
public static class LetterMinimize_Add
{
    public static void Postfix(Window window) => LetterMinimize.Opened(window);
}

[HarmonyPatch(typeof(Window), "InnerWindowOnGUI")]
public static class LetterMinimize_Draw
{
    public static void Prefix(Window __instance) => LetterMinimize.Click(__instance);

    public static void Postfix(Window __instance) => LetterMinimize.Draw(__instance);
}

[HarmonyPatch(typeof(Window), nameof(Window.PostClose))]
public static class LetterMinimize_Close
{
    public static void Postfix(Window __instance) => LetterMinimize.Closed(__instance);
}
