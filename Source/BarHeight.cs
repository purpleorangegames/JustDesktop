using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// the game keeps 35 pixels at the bottom for the button bar in a few places (gizmos, tab windows,
// the architect info pane...). There "UI.screenHeight - 35" becomes "UI.screenHeight - Taskbar.Reserved"
// so a bigger or smaller bar moves them too.
[HarmonyPatch]
public static class BarHeight
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        MethodBase[] methods =
        {
            AccessTools.PropertyGetter(typeof(ArchitectCategoryTab), nameof(ArchitectCategoryTab.InfoRect)),
            AccessTools.Method(typeof(ArchitectCategoryTab), nameof(ArchitectCategoryTab.DesignationTabOnGUI)),
            AccessTools.PropertyGetter(typeof(Designator_MoveGravship), "PaneTopY"),
            AccessTools.PropertyGetter(typeof(Designator_Plan_Copy), "PaneTopY"),
            AccessTools.PropertyGetter(typeof(Designator_Plan_CopySelectionPaste), "PaneTopY"),
            AccessTools.Method(typeof(GeneUIUtility), nameof(GeneUIUtility.DrawGenesInfo)),
            AccessTools.Method(typeof(ITab_Pawn_Gear), "FillTab"),
            AccessTools.Method(typeof(MainTabWindow), "SetInitialSizeAndPosition"),
            AccessTools.PropertyGetter(typeof(MainTabWindow), nameof(MainTabWindow.InitialSize)),
            AccessTools.PropertyGetter(typeof(MainTabWindow_Ideos), nameof(MainTabWindow_Ideos.InitialSize)),
            AccessTools.Method(typeof(MainTabWindow_PawnTable), "CreateTable"),
            AccessTools.PropertyGetter(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.InitialSize)),
            AccessTools.Method(typeof(GizmoGridDrawer), nameof(GizmoGridDrawer.DrawGizmoGrid))
        };
        foreach (MethodBase m in methods)
        {
            if (m != null)
            {
                yield return m;
            }
        }
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        FieldInfo screenHeight = AccessTools.Field(typeof(UI), nameof(UI.screenHeight));
        MethodInfo reserved = AccessTools.PropertyGetter(typeof(Taskbar), nameof(Taskbar.Reserved));
        var list = new List<CodeInstruction>(instructions);
        int found = 0;
        for (int i = 0; i + 2 < list.Count; i++)
        {
            if (list[i].LoadsField(screenHeight) && list[i + 1].LoadsConstant(35) && list[i + 2].opcode == OpCodes.Sub)
            {
                list[i + 1] = new CodeInstruction(OpCodes.Call, reserved).MoveLabelsFrom(list[i + 1]);
                found++;
            }
        }
        if (found == 0)
        {
            Log.Warning("[JustDesktop] the bar height isn't used in " + original.DeclaringType?.Name + "." + original.Name + " anymore, it keeps 35");
        }
        return list;
    }
}

// the same with the bar height written as a float: "(float)UI.screenHeight - 165f - 35f" and the like,
// where the selection pane draws its tab buttons (Log, Gear, Health...) and the search windows sit
[HarmonyPatch]
public static class BarHeightFloat
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        MethodBase[] methods =
        {
            AccessTools.PropertyGetter(typeof(MainTabWindow_Architect), nameof(MainTabWindow_Architect.PaneTopY)),
            AccessTools.PropertyGetter(typeof(RimWorld.Planet.WorldInspectPane), nameof(RimWorld.Planet.WorldInspectPane.PaneTopY)),
            AccessTools.Method(typeof(Dialog_Search<WorldSearchElement>), "SetInitialSizeAndPosition"),
            AccessTools.Method(typeof(Dialog_MapSearch), "SetInitialSizeAndPosition"),
            // the game's toggles, speed and date at the bottom right, when the mod's cards are off
            AccessTools.Method(typeof(GlobalControls), nameof(GlobalControls.GlobalControlsOnGUI)),
            AccessTools.Method(typeof(RimWorld.Planet.WorldGlobalControls), nameof(RimWorld.Planet.WorldGlobalControls.WorldGlobalControlsOnGUI))
        };
        foreach (MethodBase m in methods)
        {
            if (m != null)
            {
                yield return m;
            }
        }
    }

    public static float ReservedFloat => Taskbar.Reserved;

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        MethodInfo reserved = AccessTools.PropertyGetter(typeof(BarHeightFloat), nameof(ReservedFloat));
        int found = 0;
        foreach (CodeInstruction ins in instructions)
        {
            if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && f == 35f)
            {
                found++;
                yield return new CodeInstruction(OpCodes.Call, reserved).MoveLabelsFrom(ins);
            }
            else
            {
                yield return ins;
            }
        }
        if (found == 0)
        {
            Log.Warning("[JustDesktop] the bar height isn't used in " + original.DeclaringType?.Name + "." + original.Name + " anymore, it keeps 35");
        }
    }
}

// the selection pane's top, moved after any mod worked it out (RimHUD has its own version with 35 in it)
[HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.PaneTopY), MethodType.Getter)]
public static class InspectPaneTop
{
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(ref float __result)
    {
        // with our card the tab panels sit right on top of it
        __result = Pane.Shown ? Pane.CardRect.y + 30f : __result - (Taskbar.Reserved - 35f);
    }
}
