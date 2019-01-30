using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AssetBundles.Manager
{
    public class SceneLoader : MonoBehaviour
    {
        public delegate void OnSceneLoadedCallbackDelegate(string sceneName);

        private OnSceneLoadedCallbackDelegate sceneLoadedCallback = null;
        private List<WrappedWWW> sceneDownloadingWWWs = new List<WrappedWWW>();
        private List<AssetBundleLoadOperation> inProgressOperations = new List<AssetBundleLoadOperation>();
        private float m_Progress;
        private List<AssetBundleLoadOperation> sceneLoadOperations = new List<AssetBundleLoadOperation>();

        private static SceneLoader s_Instance = null;
        private static SceneLoader instance
        {
            get
            {
                if (s_Instance == null)
                {
                    GameObject go = new GameObject("SceneLoader", typeof(SceneLoader));
                    DontDestroyOnLoad(go);
                    s_Instance = go.GetComponent<SceneLoader>();

                    SceneManager.sceneUnloaded += s_Instance.SceneUnloaded;
                    SceneManager.sceneLoaded += s_Instance.SceneLoaded;

                    UnityEngine.Assertions.Assert.IsNotNull(s_Instance, "SceneLoader instance is null.");
                }
                return s_Instance;
            }
        }

#if UNITY_EDITOR
        // Remap lost shader when playing in unity editor.
        void RemapShader(Scene scene)
        {
            GameObject[] gos = SceneManager.GetSceneByName(scene.name).GetRootGameObjects();
            foreach (GameObject go in gos)
            {
                Renderer[] renders = go.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer render in renders)
                {
                    Material[] materials = render.sharedMaterials;
                    foreach (Material material in materials)
                    {
                        if (material == null) continue;
                        string shaderName = material.shader.name;
                        Shader newShader = Shader.Find(shaderName);
                        if (newShader != null)
                            material.shader = newShader;
                        else
                            AssetBundleManager.Log(AssetBundleManager.LogType.Error, "Unable to refresh shader: " + shaderName + " in material " + material.name);
                    }
                }
            }
            // Remapping scene skybox's material shader.
            string skyboxMaterialShaderName = RenderSettings.skybox.shader.name;
            Shader newSkyboxMaterialShader = Shader.Find(skyboxMaterialShaderName);
            if (newSkyboxMaterialShader != null)
                RenderSettings.skybox.shader = newSkyboxMaterialShader;
            else
                AssetBundleManager.Log(AssetBundleManager.LogType.Error, "Unable to remapping skybox material shader: " + skyboxMaterialShaderName);
        }
#endif

        // Get loading Level's AsyncOperation.
        AsyncOperation GetLoadLevelRequest(AssetBundleLoadOperation request)
        {
            AsyncOperation asyncRequest = null;
#if UNITY_EDITOR
            if (request is AssetBundleLoadLevelSimulationOperation)
            {
                asyncRequest = ((AssetBundleLoadLevelSimulationOperation)request).LoadLevelRequest;
            }
            else if (request is AssetBundleLoadLevelOperation)
#endif
            {
                asyncRequest = ((AssetBundleLoadLevelOperation)request).LoadLevelRequest;
            }
            return asyncRequest;
        }

        void LateUpdate()
        {
            CalculateProgess();
        }

        // 计算加载场景进度
        void CalculateProgess()
        {
            if (m_Progress == 1) return;
            float tempProgress = 0;
            if (instance.sceneDownloadingWWWs.Count == 0 && instance.inProgressOperations.Count == 0)
                m_Progress = 0;

            for (int i = 0; i < instance.sceneDownloadingWWWs.Count; i++)
            {
                // if trackWWW.disposed = true, scene or dependency assetbundle have be downloaded. 
                if (!instance.sceneDownloadingWWWs[i].disposed)
                    tempProgress += instance.sceneDownloadingWWWs[i].www.progress;
                else
                    tempProgress += 1;
            }

            for (int i = 0; i < instance.inProgressOperations.Count; i++)
            {
                AsyncOperation async = instance.GetLoadLevelRequest(instance.inProgressOperations[i]);
                if (async != null)
                {
                    if (async.progress < 0.9)
                        tempProgress += async.progress;
                    else
                        tempProgress += 1;
                }
            }

            float curProgress = tempProgress / (instance.sceneDownloadingWWWs.Count + instance.inProgressOperations.Count);
            m_Progress = curProgress;
        }

        #region interface
        public static void LoadAsync(string sceneBundleName, string sceneName, LoadSceneMode loadSceneMode, OnSceneLoadedCallbackDelegate sceneLoadedCallback, string sceneFolderPath)
        {
            instance.m_Progress = 0;
            instance.sceneLoadedCallback = sceneLoadedCallback;
            instance.sceneDownloadingWWWs.Clear();
            instance.inProgressOperations.Clear();

            AssetBundleLoadOperation loadOp = AssetBundleManager.LoadLevelAsync(sceneBundleName,
                sceneName,
                loadSceneMode == LoadSceneMode.Additive ? true : false,
                instance.sceneDownloadingWWWs,
                instance.inProgressOperations,
                sceneFolderPath);

            instance.sceneLoadOperations.Add(loadOp);
        }

        static public float progess { get { return instance.m_Progress; } }
        #endregion

        private void SceneUnloaded(Scene scene)
        {
            string sceneBundleName = GetSceneBundleNameByName(scene.name);
            if (sceneBundleName.Length > 0)
            {
                string error = string.Empty;
                LoadedAssetBundle loadedAssetBundle = AssetBundleManager.GetLoadedAssetBundle(sceneBundleName, out error);
                if (loadedAssetBundle != null && loadedAssetBundle.m_ReferencedCount == 0)
                    AssetBundleManager.UnloadAssetBundle(sceneBundleName);
                //
                Resources.UnloadUnusedAssets();
            }
        }

        private void SceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
        {
            string sceneBundleName = GetSceneBundleNameByName(scene.name);
            if (sceneBundleName.Length > 0)
            {
                string error = string.Empty;
                LoadedAssetBundle loadedAssetBundle = AssetBundleManager.GetLoadedAssetBundle(sceneBundleName, out error);
                if (loadedAssetBundle != null && loadedAssetBundle.m_ReferencedCount == 0)
                    AssetBundleManager.UnloadAssetBundle(sceneBundleName);
            }

            StartCoroutine(SceneLoadedCallback(scene, instance.sceneLoadedCallback));
        }

        IEnumerator SceneLoadedCallback(Scene scene, OnSceneLoadedCallbackDelegate callback)
        {
            if (!scene.isLoaded)
                yield return null;

#if UNITY_EDITOR
            if (Settings.Mode != AssetBundleManagerMode.SimulationMode)
                RemapShader(scene);
#endif
            if (instance.sceneLoadedCallback != null)
            {
                yield return null;
                instance.sceneLoadedCallback(scene.name);
                float elapsedTime = Time.realtimeSinceStartup - GetTimeStampStartLoadSceneByName(scene.name);
                AssetBundleManager.Log(AssetBundleManager.LogType.Info, "Scene [" + scene.name + "] was loaded in " + elapsedTime + " seconds");
            }
        }

        string GetSceneBundleNameByName(string sceneName)
        {
            string sceneBundleName = string.Empty;
            for (int i = 0; i < sceneLoadOperations.Count; i++)
            {
#if UNITY_EDITOR
                if (sceneLoadOperations[i] is AssetBundleLoadLevelSimulationOperation)
                {
                    AssetBundleLoadLevelSimulationOperation tempOp = (AssetBundleLoadLevelSimulationOperation)sceneLoadOperations[i];
                    if (tempOp.LevelName.Equals(sceneName))
                    {
                        sceneBundleName = tempOp.AssetBundleName;
                        break;
                    }
                }
#endif

#if UNITY_EDITOR
                if (sceneLoadOperations[i] is AssetBundleLoadLevelOperation)
                {
#endif
                    AssetBundleLoadLevelOperation tempOp = (AssetBundleLoadLevelOperation)sceneLoadOperations[i];
                    if (tempOp.LevelName.Equals(sceneName))
                    {
                        sceneBundleName = tempOp.AssetBundleName;
                        break;
                    }
#if UNITY_EDITOR
                }
#endif
            }
            return sceneBundleName;
        }

        float GetTimeStampStartLoadSceneByName(string sceneName)
        {
            float timeStamp = 0f;
            for (int i = 0; i < sceneLoadOperations.Count; i++)
            {
#if UNITY_EDITOR
                if (sceneLoadOperations[i] is AssetBundleLoadLevelSimulationOperation)
                {
                    AssetBundleLoadLevelSimulationOperation tempOp = (AssetBundleLoadLevelSimulationOperation)sceneLoadOperations[i];
                    if (tempOp.LevelName.Equals(sceneName))
                    {
                        timeStamp = tempOp.TimeStamp;
                        break;
                    }
                }
#endif

#if UNITY_EDITOR
                if (sceneLoadOperations[i] is AssetBundleLoadLevelOperation)
                {
#endif
                    AssetBundleLoadLevelOperation tempOp = (AssetBundleLoadLevelOperation)sceneLoadOperations[i];
                    if (tempOp.LevelName.Equals(sceneName))
                    {
                        timeStamp = tempOp.TimeStamp;
                        break;
                    }
#if UNITY_EDITOR
                }
#endif
            }
            return timeStamp;
        }
    }
}