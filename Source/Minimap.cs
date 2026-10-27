using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace JustDesktop;

// a map of the current map to move around with: only the ground, water, rock and what's built, no pawns or items.
// A button at the top right opens it under itself, a click or drag on it moves the camera there, a right click on
// either closes it
public static class Minimap
{
    private static Settings S => JustDesktopMod.Settings;

    public static bool On => S.minimap && Loading.Ready;

    public const float ButtonSize = 40f;
    public static Rect Button => new(UI.screenWidth - ButtonSize - 6f, 6f, ButtonSize, ButtonSize);

    private static Texture2D _icon;
    private static Texture2D Icon => _icon ??= ContentFinder<Texture2D>.Get("UI/JustDesktop/Hud/Minimap");

    public static void DrawButton()
    {
        if (!On || Find.CurrentMap == null || WorldRendererUtility.WorldSelected)
        {
            return;
        }
        Rect r = Button;
        bool open = Find.WindowStack.IsOpen<Window_Minimap>();
        bool over = Mouse.IsOver(r);
        Hud.Card(r);
        if (open || over)
        {
            GUI.color = open ? Style.Faded(0.35f) : Style.Hover;
            Widgets.DrawAtlas(r.ContractedBy(2f), Style.Round);
        }
        GUI.color = open || over ? Color.white : Style.Text;
        GUI.DrawTexture(r.ContractedBy(8f), Icon);
        GUI.color = Color.white;
        TooltipHandler.TipRegion(r, "Minimap\n\n" + "Click to open, right click to close".Colorize(Style.Dim));
        Event e = Event.current;
        if (e.type == EventType.MouseDown && over)
        {
            if (e.button == 0 && !open)
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Find.WindowStack.Add(new Window_Minimap());
            }
            else if (open)
            {
                Find.WindowStack.TryRemove(typeof(Window_Minimap));
            }
            e.Use();
        }
        if (open)
        {
            SizeButtons(r);
        }
    }

    private static readonly (float scale, string label)[] sizes = { (0.5f, "1/2x"), (1f, "1x"), (2f, "2x") };

    // while it's open, its sizes at the left of the button
    private static void SizeButtons(Rect button)
    {
        const float w = 36f;
        Rect card = new Rect(button.x - 6f - sizes.Length * w - 4f, button.y, sizes.Length * w + 4f, button.height);
        Hud.Card(card);
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleCenter;
        for (int i = 0; i < sizes.Length; i++)
        {
            Rect r = new Rect(card.x + 2f + i * w, card.y + 2f, w, card.height - 4f);
            bool on = Mathf.Approximately(S.minimapScale, sizes[i].scale);
            bool over = Mouse.IsOver(r);
            if (on || over)
            {
                GUI.color = on ? Style.Faded(0.35f) : Style.Hover;
                Widgets.DrawAtlas(r, Style.Round);
            }
            GUI.color = on || over ? Color.white : Style.Text;
            Widgets.Label(r, sizes[i].label);
            if (Widgets.ButtonInvisible(r) && !on)
            {
                S.minimapScale = sizes[i].scale;
                JustDesktopMod.Settings.Write();
                Find.WindowStack.WindowOfType<Window_Minimap>()?.Resize();
            }
        }
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
    }

    // the color of each cell: hidden ones dark, then rock, buildings, water, floors and the ground by how fertile it is
    private static readonly Color32 Fog = new(14, 15, 17, 255);
    private static readonly Color32 Rock = new(104, 100, 96, 255);
    private static readonly Color32 Ours = new(214, 212, 205, 255);
    private static readonly Color32 Theirs = new(168, 140, 112, 255);
    private static readonly Color32 Deep = new(42, 74, 112, 255);
    private static readonly Color32 Shallow = new(66, 104, 138, 255);
    private static readonly Color32 Floor = new(128, 114, 94, 255);
    private static readonly Color32 Rich = new(70, 92, 52, 255);
    private static readonly Color32 Soil = new(84, 88, 60, 255);
    private static readonly Color32 Barren = new(110, 104, 84, 255);

    public static void Paint(Map map, Color32[] pixels)
    {
        CellIndices indices = map.cellIndices;
        int count = indices.NumGridCells;
        for (int i = 0; i < count; i++)
        {
            pixels[i] = ColorOf(map, i);
        }
    }

    private static readonly Color32 Black = new(0, 0, 0, 255);
    private static readonly Color32 White = new(235, 235, 235, 255);

    private static Color32 ColorOf(Map map, int i)
    {
        if (!S.minimapColor)
        {
            return !map.fogGrid.IsFogged(i) && map.edificeGrid[i] != null ? White : Black;
        }
        if (map.fogGrid.IsFogged(i))
        {
            return Fog;
        }
        Building edifice = map.edificeGrid[i];
        if (edifice != null)
        {
            if (edifice.def.building is { isNaturalRock: true })
            {
                return Rock;
            }
            return edifice.Faction == Faction.OfPlayer ? Ours : Theirs;
        }
        TerrainDef terrain = map.terrainGrid.TerrainAt(i);
        if (terrain == null)
        {
            return Fog;
        }
        if (terrain.IsWater)
        {
            return terrain.passability == Traversability.Impassable ? Deep : Shallow;
        }
        if (terrain.layerable)
        {
            return Floor;
        }
        return terrain.fertility >= 1f ? Rich : terrain.fertility > 0f ? Soil : Barren;
    }
}

public class Window_Minimap : Window
{
    private const float Pad = 6f;
    private const float Refresh = 2f;

    private Texture2D texture;
    private Color32[] pixels;
    private Map painted;
    private float paintedAt = -100f;
    private bool dragging, paintedColor;

    public Window_Minimap()
    {
        doWindowBackground = false;
        drawShadow = false;
        closeOnClickedOutside = false;
        absorbInputAroundWindow = false;
        preventCameraMotion = false;
        closeOnAccept = false;
        closeOnCancel = false;
        layer = WindowLayer.GameUI;
        soundAppear = null;
        soundClose = null;
    }

    protected override float Margin => 0f;

    private static Vector2 MapSize(Map map)
    {
        float longest = Mathf.Round(260f * JustDesktopMod.Settings.paneSize * JustDesktopMod.Settings.minimapScale);
        IntVec3 size = map.Size;
        float scale = longest / Mathf.Max(size.x, size.z);
        return new Vector2(Mathf.Round(size.x * scale), Mathf.Round(size.z * scale));
    }

    public override Vector2 InitialSize => Find.CurrentMap is { } map ? MapSize(map) + new Vector2(Pad * 2f, Pad * 2f) : new Vector2(10f, 10f);

    protected override void SetInitialSizeAndPosition()
    {
        Vector2 size = InitialSize;
        Rect button = Minimap.Button;
        windowRect = new Rect(UI.screenWidth - size.x - 6f, button.yMax + 6f, size.x, size.y);
    }

    public void Resize() => SetInitialSizeAndPosition();

    public override void WindowUpdate()
    {
        base.WindowUpdate();
        Map map = Find.CurrentMap;
        // out of the way over the world or without a map, back where it was after
        if (map == null || WorldRendererUtility.WorldSelected || !Minimap.On)
        {
            windowRect = new Rect(UI.screenWidth, 0f, 0f, 0f);
            return;
        }
        if (windowRect.width <= 0f || map != painted)
        {
            SetInitialSizeAndPosition();
        }
    }

    public override void PostClose()
    {
        base.PostClose();
        if (texture != null)
        {
            Object.Destroy(texture);
            texture = null;
            painted = null;
        }
    }

    private void Paint(Map map)
    {
        IntVec3 size = map.Size;
        if (texture == null || texture.width != size.x || texture.height != size.z)
        {
            if (texture != null)
            {
                Object.Destroy(texture);
            }
            texture = new Texture2D(size.x, size.z, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            pixels = new Color32[size.x * size.z];
        }
        Minimap.Paint(map, pixels);
        texture.SetPixels32(pixels, 0);
        texture.Apply();
        painted = map;
        paintedColor = JustDesktopMod.Settings.minimapColor;
        paintedAt = Time.realtimeSinceStartup;
    }

    public override void DoWindowContents(Rect inRect)
    {
        Map map = Find.CurrentMap;
        if (map == null || inRect.width <= 0f)
        {
            return;
        }
        // painted again when the picture was let go of on closing too, or it would draw nothing until the next refresh
        if (texture == null || map != painted || paintedColor != JustDesktopMod.Settings.minimapColor || Time.realtimeSinceStartup - paintedAt > Refresh)
        {
            Paint(map);
        }
        Pane.CardBack(inRect);
        Rect area = inRect.ContractedBy(Pad);
        GUI.DrawTexture(area, texture);

        // what the camera sees
        IntVec3 size = map.Size;
        CellRect view = Find.CameraDriver.CurrentViewRect.ClipInsideMap(map);
        float sx = area.width / size.x, sz = area.height / size.z;
        Rect seen = new Rect(area.x + view.minX * sx, area.yMax - (view.maxZ + 1) * sz, view.Width * sx, view.Height * sz);
        GUI.color = new Color(1f, 1f, 1f, 0.85f);
        Widgets.DrawBox(seen);
        GUI.color = Color.white;

        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 1 && Mouse.IsOver(inRect))
        {
            e.Use();
            Close();
            return;
        }
        if (!Input.GetMouseButton(0))
        {
            dragging = false;
        }
        if (e.type == EventType.MouseDown && e.button == 0 && Mouse.IsOver(area))
        {
            dragging = true;
        }
        if (dragging && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
        {
            float x = Mathf.Clamp((e.mousePosition.x - area.x) / sx, 0f, size.x - 1);
            float z = Mathf.Clamp((area.yMax - e.mousePosition.y) / sz, 0f, size.z - 1);
            Find.CameraDriver.JumpToCurrentMapLoc(new Vector3(x, 0f, z));
            e.Use();
        }
        if (e.rawType == EventType.MouseUp && e.button == 0)
        {
            dragging = false;
        }
    }
}

// the button with the rest of the map's interface, after the main tabs
[HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_AfterMainTabs))]
public static class Minimap_Button
{
    public static void Postfix()
    {
        Minimap.DrawButton();
    }
}
