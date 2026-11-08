using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// a colored outline around a pawn you mark, mostly enemies: the one worth capturing, the one to shoot first.
// The color is picked from a button on the card and kept with the save
[StaticConstructorOnStartup]
public class PawnMarks : GameComponent
{
    private Dictionary<Pawn, Color> marks = new();
    private List<Pawn> keys;
    private List<Color> values;

    public PawnMarks(Game game)
    {
    }

    public static PawnMarks Get => Current.Game?.GetComponent<PawnMarks>();

    public static readonly Color[] Colors =
    {
        new(0.565f, 0.745f, 0.427f),   // 90be6d green
        new(0.153f, 0.49f, 0.631f),    // 277da1 blue
        new(0.976f, 0.78f, 0.31f),     // f9c74f yellow
        new(0.976f, 0.255f, 0.267f),   // f94144 red
        new(0.953f, 0.447f, 0.173f),   // f3722c orange
        new(0.443f, 0.153f, 0.475f),   // 712779 purple
    };

    public static bool TryGet(Pawn p, out Color color)
    {
        color = default;
        PawnMarks m = Get;
        return m != null && p != null && m.marks.TryGetValue(p, out color);
    }

    public static void Set(Pawn p, Color? color)
    {
        PawnMarks m = Get;
        if (m == null || p == null)
        {
            return;
        }
        if (color is { } c)
        {
            c.a = 1f;
            m.marks[p] = c;
        }
        else
        {
            m.marks.Remove(p);
        }
    }

    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            marks.RemoveAll(kv => kv.Key == null || kv.Key.Discarded);
        }
        Scribe_Collections.Look(ref marks, "marks", LookMode.Reference, LookMode.Value, ref keys, ref values);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            marks ??= new Dictionary<Pawn, Color>();
            marks.RemoveAll(kv => kv.Key == null);
        }
    }

    // ---- the outline ----

    private static Shader shader;
    private static Shader Outline => shader ??= ShaderDatabase.LoadShader("Map/JD_Outline");
    private static readonly Dictionary<Color, MaterialPropertyBlock> blocks = new();
    private static readonly Vector2[] Around =
    {
        new(1f, 0f), new(-1f, 0f), new(0f, 1f), new(0f, -1f),
        new(0.7071f, 0.7071f), new(-0.7071f, 0.7071f), new(0.7071f, -0.7071f), new(-0.7071f, -0.7071f),
    };

    private static MaterialPropertyBlock Block(Color c)
    {
        if (!blocks.TryGetValue(c, out MaterialPropertyBlock b))
        {
            b = new MaterialPropertyBlock();
            b.SetColor(ShaderPropertyIDs.Color, c);
            blocks[c] = b;
        }
        return b;
    }

    // each part of the pawn again in flat color, nudged a little all around, just under the pawn's lowest part.
    // Thicker in the world when zoomed out, so it stays about as thick on screen
    public static void DrawOutline(Pawn p, PawnDrawParms parms, List<PawnGraphicDrawRequest> requests)
    {
        if (requests == null || requests.Count == 0 || parms.Portrait || parms.Cache || parms.Statue || !TryGet(p, out Color color))
        {
            return;
        }
        float minY = float.MaxValue;
        foreach (PawnGraphicDrawRequest r in requests)
        {
            if (Outlines(r))
            {
                minY = Mathf.Min(minY, r.preDrawnComputedMatrix.m13);
            }
        }
        if (minY == float.MaxValue)
        {
            return;
        }
        float zoom = Find.CameraDriver?.ZoomRootSize ?? 12f;
        float thick = Mathf.Lerp(0.035f, 0.11f, Mathf.InverseLerp(10f, 50f, zoom));
        MaterialPropertyBlock block = Block(color);
        foreach (PawnGraphicDrawRequest r in requests)
        {
            if (!Outlines(r))
            {
                continue;
            }
            Material mat = MaterialPool.MatFrom(new MaterialRequest(r.material.mainTexture, Outline));
            foreach (Vector2 d in Around)
            {
                Matrix4x4 m = Matrix4x4.Translate(new Vector3(d.x * thick, 0f, d.y * thick)) * r.preDrawnComputedMatrix;
                m.m13 = minY - 0.0004f;
                GenDraw.DrawMeshNowOrLater(r.mesh, m, mat, parms.DrawNow, block);
            }
        }
    }

    // eyes and other things on the face never reach past the head, and they're placed by a texture offset the
    // flat copy wouldn't follow (it showed as stray lines above the head)
    private static bool Outlines(PawnGraphicDrawRequest r) =>
        r.material != null && r.mesh != null && r.material.mainTexture != null && r.node is not PawnRenderNode_AttachmentHead
        && r.material.mainTextureOffset == Vector2.zero && r.material.mainTextureScale == Vector2.one;

    // ---- the button on the card and its picker ----

    private static Texture2D dot, ring;

    private static Texture2D Circle(bool filled)
    {
        const int n = 48;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float r = Mathf.Sqrt((x + 0.5f - n / 2f) * (x + 0.5f - n / 2f) + (y + 0.5f - n / 2f) * (y + 0.5f - n / 2f));
                float outer = Mathf.Clamp01(n / 2f - 1f - r + 0.5f);
                float a = filled ? outer : outer * Mathf.Clamp01(r - (n / 2f - 6f) + 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    public static Texture2D Dot => dot ??= Circle(true);
    public static Texture2D Ring => ring ??= Circle(false);

    // a ring when not marked, the mark's color filled in when it is; click to pick
    public static void Button(Rect r, Pawn p)
    {
        bool marked = TryGet(p, out Color c);
        bool over = Mouse.IsOver(r);
        Rect inner = r.ContractedBy(Mathf.Round(r.width * 0.18f));
        GUI.color = marked ? c : over ? Color.white : Style.Dim;
        GUI.DrawTexture(inner, marked ? Dot : Ring);
        GUI.color = Color.white;
        TooltipHandler.TipRegion(r, marked ? "Marked: an outline in this color around them. Click to change or clear" : "Mark with a colored outline, to find them on the map at a glance");
        if (Widgets.ButtonInvisible(r))
        {
            Find.WindowStack.Add(new Window_Mark(p, GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.yMax))));
        }
    }
}

// the colors in a row, a custom one with a color wheel, and clear
public class Window_Mark : Window
{
    private readonly Pawn pawn;
    private readonly Vector2 at;
    private bool custom;
    private bool dragging;
    private Color customColor = new(0.5f, 0.8f, 0.9f);

    private const float Swatch = 26f, Gap = 6f, Pad = 10f;
    private static float RowWidth => (PawnMarks.Colors.Length + 2) * (Swatch + Gap) - Gap;

    public Window_Mark(Pawn p, Vector2 at)
    {
        pawn = p;
        this.at = at;
        if (PawnMarks.TryGet(p, out Color c) && Array.IndexOf(PawnMarks.Colors, c) < 0)
        {
            customColor = c;
            custom = true;
        }
        closeOnClickedOutside = true;
        doCloseX = false;
        doWindowBackground = false;
        drawShadow = false;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
        layer = WindowLayer.Super;
        soundAppear = SoundDefOf.TabOpen;
    }

    protected override float Margin => 0f;

    public override Vector2 InitialSize => new(RowWidth + Pad * 2f, custom ? 214f : 70f);

    protected override void SetInitialSizeAndPosition()
    {
        Vector2 size = InitialSize;
        windowRect = new Rect(Mathf.Clamp(at.x, 0f, UI.screenWidth - size.x), Mathf.Clamp(at.y + 4f, 0f, UI.screenHeight - size.y), size.x, size.y);
    }

    public override void DoWindowContents(Rect inRect)
    {
        if (pawn == null || pawn.Discarded)
        {
            Close();
            return;
        }
        if (windowRect.height != InitialSize.y)
        {
            windowRect.height = InitialSize.y;
        }
        Pane.CardBack(inRect);
        Rect inner = inRect.ContractedBy(Pad);
        GUI.color = Style.Dim;
        Text.Font = GameFont.Tiny;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 16f), "Mark " + pawn.LabelShortCap);
        Text.Font = GameFont.Small;
        GUI.color = Color.white;
        float y = inner.y + 18f;
        PawnMarks.TryGet(pawn, out Color now);
        bool marked = PawnMarks.TryGet(pawn, out _);
        float x = inner.x;
        foreach (Color c in PawnMarks.Colors)
        {
            Rect r = new Rect(x, y, Swatch, Swatch);
            if (Pick(r, c, marked && now == c && !custom))
            {
                PawnMarks.Set(pawn, c);
                custom = false;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            x += Swatch + Gap;
        }
        // custom: a ring of the last custom color, opens the wheel
        Rect customRect = new Rect(x, y, Swatch, Swatch);
        bool overCustom = Mouse.IsOver(customRect);
        GUI.color = customColor;
        GUI.DrawTexture(customRect.ContractedBy(overCustom ? 1f : 3f), custom ? PawnMarks.Dot : PawnMarks.Ring);
        GUI.color = Color.white;
        TooltipHandler.TipRegion(customRect, "A color of your own");
        if (Widgets.ButtonInvisible(customRect))
        {
            custom = !custom || !marked;
            if (custom)
            {
                PawnMarks.Set(pawn, customColor);
            }
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }
        x += Swatch + Gap;
        // clear
        Rect clear = new Rect(x, y, Swatch, Swatch).ContractedBy(5f);
        GUI.color = Mouse.IsOver(clear) ? Style.CloseHover : marked ? Style.Text : Style.Dim;
        GUI.DrawTexture(clear, Style.Close);
        GUI.color = Color.white;
        TooltipHandler.TipRegion(clear, "No mark");
        if (Widgets.ButtonInvisible(clear))
        {
            PawnMarks.Set(pawn, null);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            Close();
            return;
        }
        if (custom)
        {
            Rect wheel = new Rect(inner.x, y + Swatch + 10f, 130f, 130f);
            Color.RGBToHSV(customColor, out float h, out float s, out float v);
            Color hue = Color.HSVToRGB(h, s, 1f);
            Widgets.HSVColorWheel(wheel, ref hue, ref dragging, 1f);
            Color.RGBToHSV(hue, out h, out s, out _);
            Rect slider = new Rect(wheel.xMax + 14f, wheel.y + 50f, inner.xMax - wheel.xMax - 14f, 24f);
            GUI.color = Style.Dim;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(slider.x, slider.y - 18f, slider.width, 16f), "Brightness");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            v = Widgets.HorizontalSlider(slider, v, 0.2f, 1f);
            Color picked = Color.HSVToRGB(h, s, v);
            Widgets.DrawBoxSolid(new Rect(slider.x, wheel.y, 40f, 26f), picked);
            if (picked != customColor)
            {
                customColor = picked;
                PawnMarks.Set(pawn, customColor);
            }
        }
    }

    private static bool Pick(Rect r, Color c, bool chosen)
    {
        bool over = Mouse.IsOver(r);
        GUI.color = c;
        GUI.DrawTexture(r.ContractedBy(over || chosen ? 1f : 3f), PawnMarks.Dot);
        if (chosen)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(r.ContractedBy(-2f), PawnMarks.Ring);
        }
        GUI.color = Color.white;
        return Widgets.ButtonInvisible(r);
    }
}

// marked pawns are drawn part by part even when zoomed out (the game would use one cached picture), so the outline
// has parts to follow
[HarmonyPatch(typeof(PawnRenderer), "ParallelGetPreRenderResults")]
public static class PawnMarks_NoCache
{
    private static readonly AccessTools.FieldRef<PawnRenderer, Pawn> pawn = AccessTools.FieldRefAccess<PawnRenderer, Pawn>("pawn");

    public static void Prefix(PawnRenderer __instance, ref bool disableCache)
    {
        if (!disableCache && PawnMarks.TryGet(pawn(__instance), out _))
        {
            disableCache = true;
        }
    }
}

[HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.Draw))]
public static class PawnMarks_Draw
{
    private static readonly AccessTools.FieldRef<PawnRenderTree, List<PawnGraphicDrawRequest>> requests = AccessTools.FieldRefAccess<PawnRenderTree, List<PawnGraphicDrawRequest>>("drawRequests");

    public static void Prefix(PawnRenderTree __instance, PawnDrawParms parms)
    {
        if (__instance.pawn != null && __instance.Resolved)
        {
            PawnMarks.DrawOutline(__instance.pawn, parms, requests(__instance));
        }
    }
}
