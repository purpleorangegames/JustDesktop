using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the text color picked on the Window look page, for a light window color with dark text. The game's GUI tints every
// label by GUI.contentColor (pictures drawn with DrawTexture stay as they are), so it's set while the modern windows
// and the cards at the screen edges draw, and back to white after
public static class TextColor
{
    private static Settings S => JustDesktopMod.Settings;

    public static bool On => S.textCustom && WindowFrame.Modern && Loading.Ready;

    public static bool Tinted(Window w)
    {
        return w is Window_Calendar || w is Window_Notifications || w is Window_Toggles || w is MainTabWindow_Menu
            || WindowFrame.Ours(w) || WindowFrame.Framed(w)
            || w is MainTabWindow_Inspect && Pane.Shown
            || w is MainTabWindow_Architect && Architect.On;
    }

    public static void Begin()
    {
        GUI.contentColor = S.textColor;
    }

    public static void End()
    {
        GUI.contentColor = Color.white;
    }
}

[HarmonyPatch(typeof(Window), "InnerWindowOnGUI")]
public static class TextColor_Window
{
    public static void Prefix(Window __instance)
    {
        if (TextColor.On && TextColor.Tinted(__instance))
        {
            TextColor.Begin();
        }
    }

    public static void Finalizer()
    {
        GUI.contentColor = Color.white;
    }
}

// the speed, date, weather, letters, resources, alerts, messages and what's under the mouse, on their cards
[HarmonyPatch]
public static class TextColor_Hud
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI));
        yield return AccessTools.Method(typeof(WorldGlobalControls), nameof(WorldGlobalControls.WorldGlobalControlsOnGUI));
        yield return AccessTools.Method(typeof(ResourceReadout), nameof(ResourceReadout.ResourceReadoutOnGUI));
        yield return AccessTools.Method(typeof(AlertsReadout), nameof(AlertsReadout.AlertsReadoutOnGUI));
        yield return AccessTools.Method(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI));
        yield return AccessTools.Method(typeof(Messages), nameof(Messages.MessagesDoGUI));
    }

    public static void Prefix()
    {
        if (TextColor.On && Hud.Cards)
        {
            TextColor.Begin();
        }
    }

    public static void Finalizer()
    {
        GUI.contentColor = Color.white;
    }
}
