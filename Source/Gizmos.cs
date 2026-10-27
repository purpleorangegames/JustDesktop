using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace JustDesktop;

// gizmos (the buttons next to the selection pane, and the Architect's build buttons) as flat rounded tiles like
// the card. The game still draws them, its picture, hotkey, tooltip and cooldown stay; only the square background,
// the gray strips behind the label and the top right number, and the toggle's check or cross change
[StaticConstructorOnStartup]
public static class Gizmos
{
    public static bool On => JustDesktopMod.Settings.modernGizmos;

    private static readonly Color Pill = new(0.05f, 0.06f, 0.075f, 0.88f);
    private static readonly Color Hover = new(1f, 1f, 1f, 0.08f);
    // abilities get their own background in the game, here a hint of color keeps them apart
    private static readonly Color AbilityTint = new(0.55f, 0.45f, 1f, 0.1f);

    private static void Draw(Rect rect, Texture2D atlas, Color color)
    {
        GUI.color = color;
        Widgets.DrawAtlas(rect, atlas);
    }

    // in place of GenUI.DrawTextureWithMaterial for the button's background; the select brackets become corners on
    // our tile, other backgrounds that are a picture (psychic rituals) stay
    public static void Background(Rect rect, Texture tex, Material mat, Rect texCoords)
    {
        // the small versions too, which is how they're drawn in the grid
        bool ability = tex == Command_Ability.BGTex || tex == Command_Ability.BGTexShrunk;
        bool select = tex == TexCommand.SelectShelf;
        if (!On || (tex != Command.BGTex && tex != Command.BGTexShrunk && !ability && !select))
        {
            GenUI.DrawTextureWithMaterial(rect, tex, mat, texCoords);
            return;
        }
        Color old = GUI.color;
        // the game asks for gray when the button is disabled or dimmed
        bool low = mat != null;
        bool over = Mouse.IsOver(rect) && !low;
        Color back = JustDesktopMod.Settings.windowColor;
        back.a = low ? 0.6f : 0.92f;
        Draw(rect, ModernWidgets.Fill, back);
        if (ability)
        {
            Draw(rect, ModernWidgets.Fill, AbilityTint);
        }
        if (over)
        {
            Draw(rect, ModernWidgets.Fill, Input.GetMouseButton(0) ? Style.Faded(0.3f) : Hover);
        }
        Draw(rect, ModernWidgets.Line, over ? Style.Faded(0.6f) : Style.Border);
        if (select)
        {
            Corners(rect, over ? Style.Accent : Style.Dim);
        }
        GUI.color = old;
    }

    // the selection brackets the game puts on this button (select what's carried, or the shelf), as four corners
    private static void Corners(Rect rect, Color color)
    {
        GUI.color = color;
        float inset = Mathf.Round(rect.width * 0.09f), length = Mathf.Round(rect.width * 0.16f), thick = 2f;
        Rect r = rect.ContractedBy(inset);
        foreach ((float x, float y, float dx, float dy) in new[] { (r.x, r.y, 1f, 1f), (r.xMax, r.y, -1f, 1f), (r.x, r.yMax, 1f, -1f), (r.xMax, r.yMax, -1f, -1f) })
        {
            GUI.DrawTexture(new Rect(dx > 0f ? x : x - length, dy > 0f ? y : y - thick, length, thick), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(dx > 0f ? x : x - thick, dy > 0f ? y : y - length, thick, length), BaseContent.WhiteTex);
        }
    }

    // in place of GUI.DrawTexture: the gray strips behind the label, the top right number and the cooldown become pills
    public static void Strip(Rect rect, Texture tex)
    {
        if (!On || tex != TexUI.GrayTextBG)
        {
            GUI.DrawTexture(rect, tex);
            return;
        }
        Color old = GUI.color;
        Draw(rect, ModernWidgets.Fill, Pill);
        GUI.color = old;
    }

    // in place of GUI.DrawTexture for the toggle's corner: our checkbox instead of the green check and red cross
    public static void Check(Rect rect, Texture tex)
    {
        if (!On || (tex != Widgets.CheckboxOnTex && tex != Widgets.CheckboxOffTex))
        {
            GUI.DrawTexture(rect, tex);
            return;
        }
        ModernWidgets.CheckBox(rect.ContractedBy(Mathf.Round(rect.width * 0.18f)), tex == Widgets.CheckboxOnTex, true);
    }

    // in place of Widgets.FillableBar for a cooldown (abilities, rituals...): the button dimmed while it's cooling
    // down, and a thin bar along its bottom that fills as it gets ready
    public static Rect Cooldown(Rect rect, float fill, Texture2D fillTex, Texture2D bgTex, bool doBorder)
    {
        if (!On)
        {
            return Widgets.FillableBar(rect, fill, fillTex, bgTex, doBorder);
        }
        // in the grid the game still says the button is 75 high, the square is smaller
        if (GizmoGrid.Small)
        {
            rect.height = Mathf.Min(rect.height, GizmoGrid.Size);
        }
        Color old = GUI.color;
        Draw(rect, ModernWidgets.Fill, new Color(0f, 0f, 0f, 0.45f));
        Rect bar = new Rect(rect.x + 5f, rect.yMax - 7f, rect.width - 10f, 3f);
        GUI.color = new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(bar, BaseContent.WhiteTex);
        GUI.color = Style.Accent;
        GUI.DrawTexture(new Rect(bar.x, bar.y, Mathf.Round(bar.width * Mathf.Clamp01(fill)), bar.height), BaseContent.WhiteTex);
        GUI.color = old;
        return rect;
    }

    public static readonly MethodInfo FillableBar = AccessTools.Method(typeof(Widgets), nameof(Widgets.FillableBar), new[] { typeof(Rect), typeof(float), typeof(Texture2D), typeof(Texture2D), typeof(bool) });

    // every call to one method in the patched method goes to ours, which takes the same arguments
    public static IEnumerable<CodeInstruction> Swap(IEnumerable<CodeInstruction> instructions, MethodInfo from, MethodInfo to)
    {
        foreach (CodeInstruction ins in instructions)
        {
            yield return ins.Calls(from) ? new CodeInstruction(OpCodes.Call, to).MoveLabelsFrom(ins).MoveBlocksFrom(ins) : ins;
        }
    }

    public static readonly MethodInfo DrawTexture = AccessTools.Method(typeof(GUI), nameof(GUI.DrawTexture), new[] { typeof(Rect), typeof(Texture) });
}

[HarmonyPatch(typeof(Command), "GizmoOnGUIInt")]
public static class Gizmos_Command
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo background = AccessTools.Method(typeof(GenUI), nameof(GenUI.DrawTextureWithMaterial));
        instructions = Gizmos.Swap(instructions, background, AccessTools.Method(typeof(Gizmos), nameof(Gizmos.Background)));
        return Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(Gizmos), nameof(Gizmos.Strip)));
    }
}

// the time left on an ability's cooldown, and its cooldown bar
[HarmonyPatch(typeof(Command_Ability), nameof(Command_Ability.GizmoOnGUI))]
public static class Gizmos_Ability
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        instructions = Gizmos.Swap(instructions, Gizmos.FillableBar, AccessTools.Method(typeof(Gizmos), nameof(Gizmos.Cooldown)));
        return Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(Gizmos), nameof(Gizmos.Strip)));
    }
}

[HarmonyPatch(typeof(Command_ActionWithCooldown), nameof(Command_ActionWithCooldown.GizmoOnGUI))]
public static class Gizmos_ActionCooldown
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Gizmos.Swap(instructions, Gizmos.FillableBar, AccessTools.Method(typeof(Gizmos), nameof(Gizmos.Cooldown)));
    }
}

[HarmonyPatch(typeof(Command_Ritual), nameof(Command_Ritual.DrawIcon))]
public static class Gizmos_RitualCooldown
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Gizmos.Swap(instructions, Gizmos.FillableBar, AccessTools.Method(typeof(Gizmos), nameof(Gizmos.Cooldown)));
    }
}

[HarmonyPatch(typeof(Command_Toggle), nameof(Command_Toggle.GizmoOnGUI))]
public static class Gizmos_Toggle
{
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return Gizmos.Swap(instructions, Gizmos.DrawTexture, AccessTools.Method(typeof(Gizmos), nameof(Gizmos.Check)));
    }
}
