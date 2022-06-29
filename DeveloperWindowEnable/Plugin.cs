using BepInEx;
using BepInEx.Logging;
using Core.Harness;
using Game.Components.DeveloperUI;
using HarmonyLib;
using System;
using System.Reflection;

namespace DeveloperWindowEnable
{
    [BepInPlugin("Dino0040.TheLastClockwinder.DeveloperWindowEnable", "Developer Window Enable", CommonPluginConstants.Version)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource logger;

        private void Awake()
        {
            logger = Logger;
            Harmony.CreateAndPatchAll(typeof(Plugin));
        }

        [HarmonyPatch(typeof(DeveloperWindowController), "FixedUpdate")]
        [HarmonyPrefix]
        static bool EnsureEnabledDeveloperWindow()
        {
            if (ClockworkCoreSettings.Get().AllowDeveloperWindow == false)
            {
                ClockworkCoreSettings.Get().AllowDeveloperWindow = true;
                logger.LogInfo("Developer window is now enabled");
            }
            return true;
        }
    }
}
