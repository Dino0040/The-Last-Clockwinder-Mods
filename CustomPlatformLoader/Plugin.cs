using BepInEx;
using BepInEx.Logging;
using Core.Serialization;
using Game.Components.AssistantRecord;
using Game.Components.AudioLogs;
using Game.Components.Collector;
using Game.Components.Flows;
using Game.Components.LevelSelect;
using Game.Components.LevelToken;
using Game.Components.Metrics;
using Game.Components.Plants.CutPlant;
using Game.Platforms;
using Game.Platforms.Act3.Platform_FuelBeast;
using Global.Scripts;
using HarmonyLib;
using Prototype.Components.Lilypad;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomPlatformLoader
{
    [BepInPlugin("Dino0040.TheLastClockwinder.CustomPlatformLoader", "Custom Platform Loader", CommonPluginConstants.Version)]
    class Plugin : BaseUnityPlugin
    {
        static ManualLogSource logger;

        static string CustomPlatformAssetBundlePath => Path.Combine(Application.streamingAssetsPath, "CustomPlatforms");

        static bool exporting = false;

        static readonly HashSet<string> customPlatformPaths = new HashSet<string>();

        private void Awake()
        {
            logger = Logger;
            Harmony.CreateAndPatchAll(typeof(Plugin));

            SceneManager.sceneLoaded += ReplaceCustomMaterialsAndMeshesWithNative;
        }

        private void ReplaceCustomMaterialsAndMeshesWithNative(Scene scene, LoadSceneMode mode)
        {
            if (customPlatformPaths.Contains(scene.path))
            {
                var allRenderersInScene = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<MeshRenderer>());
                foreach (Renderer renderer in allRenderersInScene)
                {
                    Material[] rendererMaterials = renderer.sharedMaterials;
                    for (int i = 0; i < rendererMaterials.Length; i++)
                    {
                        if (AssetExporter.AllMaterials.TryGetValue(rendererMaterials[i].name, out Material cachedMaterial))
                        {
                            rendererMaterials[i] = cachedMaterial;
                        }
                    }
                    renderer.sharedMaterials = rendererMaterials;
                }

                var allMeshFiltersInScene = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<MeshFilter>());
                foreach (MeshFilter meshFilter in allMeshFiltersInScene)
                {
                    if (AssetExporter.AllMeshes.TryGetValue(meshFilter.gameObject.name, out Mesh mesh))
                    {
                        meshFilter.sharedMesh = mesh;
                    }
                }

                var allMeshCollidersInScene = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<MeshCollider>());
                foreach (MeshCollider meshCollider in allMeshCollidersInScene)
                {
                    if (AssetExporter.AllMeshes.TryGetValue(meshCollider.gameObject.name, out Mesh mesh))
                    {
                        meshCollider.sharedMesh = mesh;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Bootstrapper), "Awake")]
        [HarmonyPrefix]
        static bool StopBootstrapperAndExportNativeAssets(MonoBehaviour __instance)
        {
            if(AssetExporter.AllMaterials.Count == 0)
            {
                exporting = true;
                __instance.enabled = false;
                foreach (var comp in __instance.gameObject.GetComponents<Component>())
                {
                    if (!(comp is Transform || comp is RectTransform || comp is Bootstrapper))
                    {
                        Destroy(comp);
                    }
                }
                foreach(Transform child in __instance.transform)
                {
                    Destroy(child);
                }
                DontDestroyOnLoad(__instance);
                __instance.StartCoroutine(AssetExporter.ExportAll(logger, () =>
                {
                    exporting = false;
                    SceneManager.LoadScene(0, LoadSceneMode.Single);
                    Destroy(__instance.gameObject);
                    foreach (KeyValuePair<string, Material> keyValuePair in AssetExporter.AllMaterials)
                    {
                        logger.LogInfo("Material: " + keyValuePair.Key);
                    }
                    foreach (KeyValuePair<string, Mesh> keyValuePair in AssetExporter.AllMeshes)
                    {
                        logger.LogInfo("Mesh: " + keyValuePair.Key);
                    }
                }));
                return false;
            }
            return true;
        }


        [HarmonyPatch(typeof(SavableBehaviour), "Awake")]
        [HarmonyPatch(typeof(IdentityBehaviour), "Awake")]
        [HarmonyPatch(typeof(PlatformLockDrivers), "Update")]
        [HarmonyPatch(typeof(FruitCollector), "Awake")]
        [HarmonyPatch(typeof(PlatformContent), "Awake")]
        [HarmonyPatch(typeof(CutPlant), "Awake")]
        [HarmonyPatch(typeof(FuelBeast), "Awake")]
        [HarmonyPatch(typeof(LilypadCloneCancel), "Awake")]
        [HarmonyPatch(typeof(Flow), "Awake")]
        [HarmonyPatch(typeof(KinematicDuringPlatformTransition), "OnDestroy")]
        [HarmonyPatch(typeof(KinematicDuringPlatformTransition), "Awake")]
        [HarmonyPatch(typeof(PlatformAssistantLimit), "OnDestroy")]
        [HarmonyPatch(typeof(PlatformAssistantLimit), "Awake")]
        [HarmonyPatch(typeof(Game.Platforms.Act3.Platform_Launch.LaunchFlow), "Awake")]
        [HarmonyPatch(typeof(Game.Platforms.Act3.Platform_Launch.LaunchFlow), "OnDestroy")]
        [HarmonyPatch(typeof(AudioLog), "Awake")]
        [HarmonyPatch(typeof(AssistantRecord), "Awake")]
        [HarmonyPatch(typeof(AssistantRecord), "FixedUpdate")]
        [HarmonyPatch(typeof(ThroughputMetric), "Awake")]
        [HarmonyPatch(typeof(LevelToken), "Awake")]
        [HarmonyPatch(typeof(NewtonVR.NVRSnappable), "Awake")]
        [HarmonyPatch(typeof(NewtonVR.NVRInteractableItem), "Start")]
        [HarmonyPatch(typeof(ClockArm), "FixedUpdate")]
        [HarmonyPrefix]
        static bool StopAllBehavioursDuringExport(MonoBehaviour __instance)
        {
            if (exporting)
            {
                return false;
            }
            return true;
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

                    customPlatformPaths.Add(scenePath);

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
