using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the loading screen in the modern look: the status, the tip and the list of content as rounded cards, the
// content's entries as soft rounded tiles
public static class Loading
{
    // not while the game is first starting: our textures aren't loaded yet, and asking for them then breaks them
    // for the whole session
    // (the game's own assets can be there while the mods' textures still aren't, so it looks for one of ours, quietly)
    private static bool ready;
    public static bool Ready => ready || (ready = StaticConstructorOnStartupUtility.coreStaticAssetsLoaded && ContentFinder<Texture2D>.Get("UI/JustDesktop/RoundLine", false) != null);

    private static bool On => Ready && ModernWidgets.Enabled;

    // the windows the game opens for these when it draws them through the window stack
    public static bool Ours(Window w) => Ready && w is ImmediateWindow && (w.ID == 62893994 || w.ID == 62893996 || w.ID == 62893997);

    public static void Back(Rect rect)
    {
        if (!On)
        {
            Widgets.DrawWindowBackground(rect);
            return;
        }
        Pane.CardBack(rect);
    }

    public static void Shadow(Rect rect)
    {
        if (!On)
        {
            Widgets.DrawShadowAround(rect);
        }
    }

    private static readonly Color ListBack = new(0.13f, 0.13f, 0.13f);
    private static readonly Color Item = new(0.32f, 0.32f, 0.32f);
    private static readonly Color Incompatible = new(0.31f, 0.29f, 0.15f);

    // the boxes in the content list: the list's background and each entry
    public static void Box(Rect rect, Color color)
    {
        if (!On)
        {
            Widgets.DrawBoxSolid(rect, color);
            return;
        }
        Color c = color == ListBack ? new Color(0f, 0f, 0f, 0.22f)
            : color == Item ? new Color(1f, 1f, 1f, 0.07f)
            : color == Incompatible ? new Color(0.95f, 0.8f, 0.3f, 0.14f)
            : new Color(0f, 0f, 0f, 0.3f);
        GUI.color = c;
        Widgets.DrawAtlas(rect, ModernWidgets.Fill);
        GUI.color = Color.white;
    }

    private static readonly MethodInfo windowBack = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawWindowBackground), new[] { typeof(Rect) });
    private static readonly MethodInfo shadow = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawShadowAround));
    private static readonly MethodInfo boxSolid = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawBoxSolid), new[] { typeof(Rect), typeof(Color) });

    [HarmonyPatch]
    public static class Windows
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI));
            yield return AccessTools.Method(typeof(GameplayTipWindow), nameof(GameplayTipWindow.DrawWindow));
            yield return AccessTools.Method(typeof(ModSummaryWindow), nameof(ModSummaryWindow.DrawWindow));
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            instructions = Gizmos.Swap(instructions, windowBack, AccessTools.Method(typeof(Loading), nameof(Back)));
            return Gizmos.Swap(instructions, shadow, AccessTools.Method(typeof(Loading), nameof(Shadow)));
        }
    }

    // the starting site's buttons at the bottom of the world: a rounded card, flat buttons
    [HarmonyPatch(typeof(RimWorld.Page_SelectStartingSite), "DoCustomBottomButtons")]
    public static class SiteButtons
    {
        public static void Prefix(out bool __state)
        {
            __state = ModernWidgets.Active;
            if (On)
            {
                ModernWidgets.Active = true;
            }
        }

        public static void Postfix(bool __state)
        {
            ModernWidgets.Active = __state;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return Gizmos.Swap(instructions, windowBack, AccessTools.Method(typeof(Loading), nameof(Back)));
        }
    }

    [HarmonyPatch(typeof(ModSummaryWindow), "DrawContents")]
    public static class List
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return Gizmos.Swap(instructions, boxSolid, AccessTools.Method(typeof(Loading), nameof(Box)));
        }
    }
}
