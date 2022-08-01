using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using BepInEx.Logging;
using System.IO;

namespace CustomPlatformLoader
{
    internal class AssetExporter
    {
        static AsyncOperation asyncOp = null;

        public static readonly Dictionary<string, Material> AllMaterials = new Dictionary<string, Material>();
        public static readonly Dictionary<string, Mesh> AllMeshes = new Dictionary<string, Mesh>();

        public static IEnumerator ExportAll(ManualLogSource log, Action finishCallback)
        {
            float oldFixedDeltaTime = Time.fixedDeltaTime;
            Time.fixedDeltaTime = Mathf.Infinity;
            log.LogInfo("There are " + SceneManager.sceneCountInBuildSettings + " scenes");
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                log.LogInfo("Got scene path " + scenePath);
                if (!scenePath.Contains("/Platforms/"))
                {
                    continue;
                }
                if(asyncOp != null)
                {
                    asyncOp.allowSceneActivation = true;
                }
                log.LogInfo("Loading scene " + scenePath);
                asyncOp = SceneManager.LoadSceneAsync(i, LoadSceneMode.Single);
                asyncOp.allowSceneActivation = false;
                while(asyncOp.progress < 0.9f)
                {
                    yield return null;
                }
                log.LogInfo("Waiting scene " + scenePath);

                Scene scene = SceneManager.GetSceneByBuildIndex(i);
                log.LogInfo("Got scene " + scene.name);

                Material[] allLoadedMaterials = Resources.FindObjectsOfTypeAll<Material>();
                foreach (Material material in allLoadedMaterials)
                {
                    if (!AllMaterials.TryGetValue(material.name, out Material _))
                    {
                        AllMaterials.Add(material.name, material);
                    }
                }

                Mesh[] allLoadedMeshes = Resources.FindObjectsOfTypeAll<Mesh>();
                foreach (Mesh mesh in allLoadedMeshes)
                {
                    if (!AllMeshes.TryGetValue(mesh.name, out Mesh _))
                    {
                        AllMeshes.Add(mesh.name, mesh);
                    }
                }
            }
            asyncOp.allowSceneActivation = true;
            log.LogInfo("Saved all!");
            Time.fixedDeltaTime = oldFixedDeltaTime;
            finishCallback?.Invoke();
        }
    }
}
