using System;
using Argon.UI;
using UnityModManagerNet;

namespace Argon;

public static class ModEntryPoint
{
    private static ArgonHost? _host;

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        modEntry.OnToggle = OnToggle;
        modEntry.OnShowGUI = OnShowGui;
        modEntry.OnHideGUI = OnHideGui;
        modEntry.OnUnload = OnUnload;
        modEntry.Logger.Log("Argon UMM entry point loaded.");
        return true;
    }

    private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
    {
        if (enabled)
        {
            try
            {
                if (_host == null)
                {
                    _host = ArgonHost.Create();
                }

                modEntry.Logger.Log("Argon enabled.");
                return true;
            }
            catch (Exception exception)
            {
                modEntry.Logger.LogException("Failed to start Argon.", exception);
                Shutdown();
                return false;
            }
        }

        Shutdown();
        modEntry.Logger.Log("Argon disabled.");
        return true;
    }

    private static void OnShowGui(UnityModManager.ModEntry _)
    {
        _host?.ShowWindow();
    }

    private static void OnHideGui(UnityModManager.ModEntry _)
    {
        _host?.HideWindow();
    }

    private static bool OnUnload(UnityModManager.ModEntry modEntry)
    {
        Shutdown();
        modEntry.Logger.Log("Argon unloaded.");
        return true;
    }

    private static void Shutdown()
    {
        if (_host == null)
        {
            return;
        }

        ArgonHost.Destroy(_host);
        _host = null;
    }
}
