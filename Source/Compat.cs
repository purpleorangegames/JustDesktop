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
