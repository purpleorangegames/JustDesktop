using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace JustDesktop;

// text fields and scroll bars everywhere, drawn by the game's GUI layer in 1.7 (it doesn't use the old skin pictures):
// text fields get a soft rounded fill and outline, the accent while typing in them; scroll bars lose their dark track
// and get a thin rounded thumb that brightens under the mouse
public static class Fields
{
    public static bool On => ModernWidgets.Enabled && Loading.Ready;

    private static readonly Color FieldFill = new(1f, 1f, 1f, 0.06f);
    private static readonly Color FieldLine = new(1f, 1f, 1f, 0.14f);

    public static void FieldBack(Rect rect, bool focused)
    {
        Color old = GUI.color;
        GUI.color = FieldFill;
        Widgets.DrawAtlas(rect, ModernWidgets.Fill);
        GUI.color = focused ? Style.Faded(0.85f) : FieldLine;
        Widgets.DrawAtlas(rect, ModernWidgets.Line);
        GUI.color = old;
    }

    private static MethodInfo fillRect, fillRounded;

    public static void SetBackendCalls(MethodInfo fill, MethodInfo rounded)
    {
        fillRect = fill;
        fillRounded = rounded;
    }

    // the scroll bar's track: nothing, the thumb shows where it is
    public static void Track(object backend, Rect rect, Color color)
    {
        if (!On)
        {
            fillRect.Invoke(backend, new object[] { rect, color });
        }
    }

    public static void Thumb(object backend, Rect rect, Color color, float rounding)
    {
        if (!On)
        {
            fillRounded.Invoke(backend, new object[] { rect, color, rounding });
            return;
        }
        bool vertical = rect.height >= rect.width;
        Rect hit = vertical ? new Rect(rect.x - 2f, rect.y, rect.width + 4f, rect.height) : new Rect(rect.x, rect.y - 2f, rect.width, rect.height + 4f);
        bool over = Mouse.IsOver(hit);
        Rect thumb = vertical ? new Rect(Mathf.Round(rect.center.x - 3f), rect.y + 2f, 6f, rect.height - 4f) : new Rect(rect.x + 2f, Mathf.Round(rect.center.y - 3f), rect.width - 4f, 6f);
        if (over)
        {
            thumb = thumb.ExpandedBy(vertical ? 1f : 0f, vertical ? 0f : 1f);
        }
        Color old = GUI.color;
        GUI.color = over ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.22f);
        Widgets.DrawAtlas(thumb, ModernWidgets.Fill);
        GUI.color = old;
    }
}

[HarmonyPatch]
public static class Fields_Scrollbars
{
    public static MethodBase TargetMethod() => AccessTools.Method(typeof(GUI), "HandleScrollbars");

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo track = AccessTools.Method(typeof(Fields), nameof(Fields.Track));
        MethodInfo thumb = AccessTools.Method(typeof(Fields), nameof(Fields.Thumb));
        int found = 0;
        foreach (CodeInstruction ins in instructions)
        {
            if (ins.operand is MethodInfo m && m.DeclaringType?.Name == "IGuiBackend" && (m.Name == "FillRect" || m.Name == "FillRectRounded"))
            {
                if (m.Name == "FillRect")
                {
                    Fields.SetBackendCalls(m, AccessTools.Method(m.DeclaringType, "FillRectRounded"));
                }
                found++;
                yield return new CodeInstruction(OpCodes.Call, m.Name == "FillRect" ? track : thumb).MoveLabelsFrom(ins).MoveBlocksFrom(ins);
                continue;
            }
            yield return ins;
        }
        if (found == 0)
        {
            Log.Warning("[JustDesktop] the game's scroll bars changed, they keep their look");
        }
    }
}

// the text field's own background and border are left out while ours is drawn under the text
[HarmonyPatch]
public static class Fields_TextField
{
    private static readonly AccessTools.FieldRef<GUISkin> skin = AccessTools.StaticFieldRefAccess<GUISkin>(AccessTools.Field(typeof(GUI), "_skin"));

    public static MethodBase TargetMethod() => AccessTools.Method(typeof(GUI), "DrawTextFieldChrome");

    public static void Prefix(Rect rect, bool focused, GUIStyle style, out Texture2D __state)
    {
        __state = null;
        if (!Fields.On)
        {
            return;
        }
        GUIStyle s = style ?? skin().textField;
        if (s?.normal.background == null)
        {
            return;
        }
        __state = s.normal.background;
        s.normal.background = null;
        Fields.FieldBack(rect, focused);
    }

    public static void Postfix(GUIStyle style, Texture2D __state)
    {
        if (__state != null)
        {
            (style ?? skin().textField).normal.background = __state;
        }
    }
}
