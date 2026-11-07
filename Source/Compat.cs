using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace JustDesktop;

// RimHUD draws the selection pane's tab buttons itself, with its own width
public static class RimHud
{
    private static bool looked;
    private static PropertyInfo modifyPane, tabWidth, value;

    public static bool Loaded => ModsConfig.IsActive("Jaxe.RimHUD");

    // RimHUD is drawing its own selection pane right now
    public static bool ModifiesPane
    {
        get
        {
            TabWidth(0f);
            try
            {
                return modifyPane != null && (bool)modifyPane.GetValue(null);
            }
            catch
            {
                return false;
            }
        }
    }

    public static float TabWidth(float vanilla)
    {
        if (!looked)
        {
            looked = true;
            modifyPane = AccessTools.TypeByName("RimHUD.Engine.State")?.GetProperty("ModifyPane", BindingFlags.Public | BindingFlags.Static);
            tabWidth = AccessTools.TypeByName("RimHUD.Configuration.Theme")?.GetProperty("InspectPaneTabWidth", BindingFlags.Public | BindingFlags.Static);
            value = tabWidth?.PropertyType.GetProperty("Value");
        }
        try
        {
            if (modifyPane != null && value != null && (bool)modifyPane.GetValue(null))
            {
                return Convert.ToSingle(value.GetValue(tabWidth.GetValue(null)));
            }
        }
        catch
        {
            modifyPane = null;
        }
        return vanilla;
    }
}

// Atmosphere Overhaul (acutus.AtmosphereOverhaul.release) shows feels-like temperature and wind under the mouse in the game's
// corner readout, which our cards replace; here they're read from it for the weather card, in its own words
public static class Atmosphere
{
    private static bool looked;
    private static MethodInfo feelsLike, getComp, trySample;
    private static FieldInfo velX, velZ;

    private static int cachedFrame = -1;

    public static bool Loaded => ModsConfig.IsActive("acutus.AtmosphereOverhaul.release");

    private static void Look()
    {
        looked = true;
        Type feels = AccessTools.TypeByName("AtmosphereOverhaul.RimWorld.FeelsLikeTemperature");
        Type comp = AccessTools.TypeByName("AtmosphereOverhaul.RimWorld.AtmosphereMapComponent");
        Type sample = AccessTools.TypeByName("AtmosphereOverhaul.Simulation.LocalAtmosphereSample");
        if (feels == null || comp == null || sample == null)
        {
            return;
        }
        feelsLike = AccessTools.Method(feels, "TryGet", new[] { typeof(Map), typeof(IntVec3), typeof(float).MakeByRefType() });
        getComp = AccessTools.Method(typeof(Map), nameof(Map.GetComponent), Type.EmptyTypes)?.MakeGenericMethod(comp);
        trySample = AccessTools.Method(comp, "TryGetSample", new[] { typeof(IntVec3), sample.MakeByRefType() });
        velX = AccessTools.Field(sample, "VelocityX");
        velZ = AccessTools.Field(sample, "VelocityZ");
    }

    // "Feels 33C · SW 3m/s" for the cell under the mouse, null when there's nothing to say
    public static string Line(Map map)
    {
        Read(map, out string feels, out string wind);
        return feels == null ? wind : wind == null ? feels : feels + "  ·  " + wind;
    }

    private static string cachedFeels, cachedWind;

    // the feels-like temperature and the wind under the mouse, each null when Atmosphere Overhaul has none
    public static void Read(Map map, out string feels, out string wind)
    {
        Fill(map);
        feels = cachedFeels;
        wind = cachedWind;
    }

    private static void Fill(Map map)
    {
        if (!Loaded || map == null)
        {
            cachedFeels = cachedWind = null;
            return;
        }
        if (cachedFrame == UnityEngine.Time.frameCount)
        {
            return;
        }
        cachedFrame = UnityEngine.Time.frameCount;
        cachedFeels = cachedWind = null;
        if (!looked)
        {
            Look();
        }
        IntVec3 cell = UI.MouseCell();
        if (!cell.InBounds(map) || cell.Fogged(map))
        {
            return;
        }
        try
        {
            string feels = null, wind = null;
            if (feelsLike != null)
            {
                object[] args = { map, cell, 0f };
                if ((bool)feelsLike.Invoke(null, args))
                {
                    // short "Feels 33C" in English, the mod's own words in other languages
                    string t = ((float)args[2]).ToStringTemperature("F0");
                    feels = LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage ? "Feels " + t : "AO_FeelsLike".Translate(t).Resolve();
                }
            }
            object comp = getComp?.Invoke(map, null);
            if (comp != null && trySample != null && velX != null && velZ != null)
            {
                object[] args = { cell, null };
                if ((bool)trySample.Invoke(comp, args))
                {
                    float x = (float)velX.GetValue(args[1]), z = (float)velZ.GetValue(args[1]);
                    float speed = UnityEngine.Mathf.Sqrt(x * x + z * z);
                    int rounded = UnityEngine.Mathf.RoundToInt(speed);
                    if (speed < 0.05f)
                    {
                        wind = "AO_Readout_WindCalm".Translate(rounded);
                    }
                    else
                    {
                        float angle = UnityEngine.Mathf.Atan2(x, z) * UnityEngine.Mathf.Rad2Deg;
                        if (angle < 0f)
                        {
                            angle += 360f;
                        }
                        string[] keys = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
                        wind = "AO_Readout_Wind".Translate(("AO_Compass_" + keys[UnityEngine.Mathf.RoundToInt(angle / 45f) % 8]).Translate(), rounded);
                    }
                }
            }
            cachedFeels = feels;
            cachedWind = wind;
        }
        catch (Exception e)
        {
            Log.ErrorOnce("[JustDesktop] couldn't read Atmosphere Overhaul's feels-like and wind: " + e, 61290377);
        }
    }
}
