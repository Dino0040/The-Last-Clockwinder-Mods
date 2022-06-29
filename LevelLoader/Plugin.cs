using BepInEx;
using BepInEx.Logging;
using Game.Components.LevelSelect;
using Game.Platforms;
using Global.Scripts;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CustomPlatformLoader
{
    [BepInPlugin("Dino0040.TheLastClockwinder.CustomPlatformLoader", "Custom Platform Loader", CommonPluginConstants.Version)]
    class Plugin : BaseUnityPlugin
    {
        static ManualLogSource logger;

        static string CustomPlatformAssetBundlePath => Path.Combine(Application.streamingAssetsPath, "CustomPlatforms");

        private void Awake()
        {
            logger = Logger;
            Harmony.CreateAndPatchAll(typeof(Plugin));
        }

        [HarmonyPatch(typeof(PlatformManager), nameof(PlatformManager.PlatformList))]
        [HarmonyPatch(MethodType.Getter)]
        [HarmonyPrefix]
        static bool LoadAndPrepareCustomPlatformsBeforeFirstArrayAccess(PlatformManager __instance)
        {
            if (__instance.platformList != null)
            {
                return true;
            }

            logger.LogInfo("Loading custom platforms");

            List<AssetBundle> assetBundles = LoadAllAssetBundles();

            ImportedGlobeSpot[] geos = FindObjectsOfType<ImportedGlobeSpot>();

            /* Geos are named "geo_x" where x is an integer.
             * Remove the first 4 characters and sort by the parsed integer. */
            geos = geos.OrderBy(n => int.Parse(n.gameObject.name.Remove(0, 4))).ToArray();

            // Choose geo_96, containing a plain level selection spot, as the prefab.
            GameObject spotPrefab = geos[96].transform.GetChild(0).gameObject;

            /* geo_76 up until geo_85 do not contain any level selection spots,
               making them usable as hosts to our own. */
            int geoIndex = 76;

            foreach(AssetBundle assetBundle in assetBundles)
            {
                foreach (string scenePath in assetBundle.GetAllScenePaths())
                {
                    logger.LogInfo($"Placing scene '{scenePath}' at {geos[geoIndex].gameObject.name}");

                    // Level select spots are the hexagon on the globe
                    #region "Create Level Select Spot"
                    GameObject spotInstance = Instantiate(spotPrefab);
                    spotInstance.transform.SetParent(geos[geoIndex].transform, false);
                    spotInstance.transform.localPosition = Vector3.zero;
                    spotInstance.transform.localRotation = Quaternion.identity;
                    spotInstance.transform.localScale = Vector3.one;
                    LevelSelectSpot levelSelectSpot = spotInstance.GetComponentInChildren<LevelSelectSpot>();
                    levelSelectSpot.Level.PlatformScene.ScenePath = scenePath;
                    levelSelectSpot.Activated = true;
                    #endregion

                    // Platform (definitions) hold the necessary information for the platform manager
                    #region "Create Platform Definition"
                    GameObject platformDefinitionObject = new GameObject($"Platform_Mod_{geoIndex}");
                    platformDefinitionObject.transform.SetParent(__instance.transform);
                    Platform platformDefinition = platformDefinitionObject.AddComponent<Platform>();
                    /* WARNING: The identity system is yet to be fully understood.
                     * All built-in platform definitions store a global object id in this field. */
                    platformDefinition.Identity = scenePath;
                    // MachineMusic of Platform_Act1_Study
                    platformDefinition.MachineMusic = new Core.Audio.UnityGuid(Guid.Parse("48ec6acb-6e28-45eb-b456-72a16d5b180c"));
                    platformDefinition.NoEstimation = true;
                    platformDefinition.ContentScene.ScenePath = scenePath;
                    #endregion

                    geoIndex++;
                }
            }
            
            return true;
        }

        static List<AssetBundle> LoadAllAssetBundles()
        {
            List<AssetBundle> loadedAssetBundles = new List<AssetBundle>();

            Directory.CreateDirectory(CustomPlatformAssetBundlePath);

            string[] assetBundleFiles = Directory.GetFiles(CustomPlatformAssetBundlePath);
            foreach (string assetBundleFile in assetBundleFiles)
            {
                AssetBundle assetBundle = AssetBundle.LoadFromFile(assetBundleFile);
                if(assetBundle != null)
                {
                    loadedAssetBundles.Add(assetBundle);
                    logger.LogInfo($"Success loading AssetBundle {assetBundleFile}");
                }
                else
                {
                    logger.LogInfo($"Failed to load AssetBundle {assetBundleFile}");
                }
            }
            return loadedAssetBundles;
        }

        [HarmonyPatch(typeof(PlatformContent), nameof(PlatformContent.Transition))]
        [HarmonyPostfix]
        static void FireTransitionEventsWhenPlayableIsMissing(PlatformContent.TransitionDirection direction, PlatformContent __instance)
        {
            if (direction == PlatformContent.TransitionDirection.In && __instance.PlatformTransitionIn == null)
            {
                logger.LogInfo("Firing missing platform-in transition events");
                __instance.StartCoroutine(FirePlatformInTransitionEventsRoutine());
            }
            if (direction == PlatformContent.TransitionDirection.Out && __instance.PlatformTransitionOut == null)
            {
                logger.LogInfo("Firing missing platform-out transition events");
                __instance.StartCoroutine(FirePlatformOutTransitionEventsRoutine());
            }
        }

        static IEnumerator FirePlatformInTransitionEventsRoutine()
        {
            yield return null;
            StringMarker stringMarker = ScriptableObject.CreateInstance<StringMarker>();
            stringMarker.Data = "play_locking_sound";
            PlatformTransition.Instance.OnNotify(default, stringMarker, default);

            yield return null;
            stringMarker = ScriptableObject.CreateInstance<StringMarker>();
            stringMarker.Data = "platform_finish_transition_in";
            PlatformTransition.Instance.OnNotify(default, stringMarker, default);
        }

        static IEnumerator FirePlatformOutTransitionEventsRoutine()
        {
            yield return null;
            StringMarker stringMarker = ScriptableObject.CreateInstance<StringMarker>();
            stringMarker.Data = "platform_finish_transition_out";
            PlatformTransition.Instance.OnNotify(default, stringMarker, default);
        }
    }
}
