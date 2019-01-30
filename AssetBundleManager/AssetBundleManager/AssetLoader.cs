using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace AssetBundles.Manager
{
    public class LoadAssetInfo
    {
        float m_AssetLoadStartTime;
        string m_AssetBundleName;
        string m_AssetName;
        Object m_LoadedObject = null;
        System.Action<Object> m_LoadedAction = null;
        AssetBundleLoadAssetOperation m_LoadRequest = null;

        public LoadAssetInfo(float assetLoadStartTime, string assetBundleName, string assetName, System.Action<Object> loadedAction, AssetBundleLoadAssetOperation loadRequest)
        {
            this.m_AssetLoadStartTime = assetLoadStartTime;
            this.m_AssetBundleName = assetBundleName;
            this.m_AssetName = assetName;
            this.m_LoadedAction = loadedAction;
            this.m_LoadRequest = loadRequest;
        }

        public float AssetLoadStartTime { get { return this.m_AssetLoadStartTime; } }

        public string AssetBundleName { get { return this.m_AssetBundleName; } }

        public string AssetName { get { return this.m_AssetName; } }

        public Object LoadedObject { get { return this.m_LoadedObject; } }

        public System.Action<Object> LoadedAction { get { return this.m_LoadedAction; } }

        public bool IsDone()
        {
            bool ret = false;
            if (m_LoadRequest.IsDone())
            {
                ret = true;
                m_LoadedObject = m_LoadRequest.GetAsset<Object>();
            }
            return ret;
        }
    }

    public class AssetLoader : MonoBehaviour
    {
        private List<LoadAssetInfo> m_LoadAssetInfos = new List<LoadAssetInfo>();
        private List<LoadAssetInfo> m_RemoveAssetInfo = new List<LoadAssetInfo>();
        private static AssetLoader s_Instance = null;

        void Update()
        {
            if (m_LoadAssetInfos.Count == 0) return;

            for (int i = 0; i < m_LoadAssetInfos.Count; i++)
            {
                LoadAssetInfo loadAssetInfo = m_LoadAssetInfos[i];
                if (loadAssetInfo.IsDone())
                {
                    // Will remove item which operation is done.
                    m_RemoveAssetInfo.Add(loadAssetInfo);
                    // Calculate and display the elapsed time.
                    float elapsedTime = Time.realtimeSinceStartup - loadAssetInfo.AssetLoadStartTime;
                    AssetBundleManager.Log(AssetBundleManager.LogType.Info, loadAssetInfo.AssetName + (loadAssetInfo.LoadedObject == null ? " was not" : " was") + " loaded successfully in " + elapsedTime + " seconds");
#if UNITY_EDITOR
                    if (Settings.Mode != AssetBundleManagerMode.SimulationMode)
                        RemapShader(loadAssetInfo.LoadedObject);
#endif
                    try
                    {
                        loadAssetInfo.LoadedAction(loadAssetInfo.LoadedObject);
                    }
                    catch (System.Exception e)
                    {
                        AssetBundleManager.Log(AssetBundleManager.LogType.Error, e.Message + "\n" + e.StackTrace);
                    }
                }
            }

            for (int i = 0; i < m_RemoveAssetInfo.Count; i++)
            {
                m_LoadAssetInfos.Remove(m_RemoveAssetInfo[i]);
            }
            //
            m_RemoveAssetInfo.Clear();
        }

#if UNITY_EDITOR
        // Remap lost shader.
        void RemapShader(Object loadedObject)
        {
            if (loadedObject is GameObject)
            {
                GameObject go = loadedObject as GameObject;
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
            else if (loadedObject is Material)  // remap the shader that material used.
            {
                Material mat = (Material)loadedObject;
                string shaderName = mat.shader.name;
                Shader newShader = Shader.Find(shaderName);
                if (newShader != null)
                    mat.shader = newShader;
                else
                    AssetBundleManager.Log(AssetBundleManager.LogType.Error, "Unable to refresh shader: " + shaderName + " in material " + mat.name);
            }
        }
#endif

        #region interface
        public static void LoadAsync<T>(string assetBundleName, string assetName, System.Action<Object> loadedAction, AssetType assetType, string assetFolderPath) where T : Object
        {
            if (s_Instance == null)
            {
                GameObject go = new GameObject("AssetLoader", typeof(AssetLoader));
                DontDestroyOnLoad(go);

                s_Instance = go.GetComponent<AssetLoader>();
                UnityEngine.Assertions.Assert.IsNotNull(s_Instance, "AssetLoader instance is null.");
            }
            // Load asset from assetBundle.
            AssetBundleLoadAssetOperation request = AssetBundleManager.LoadAssetAsync(assetBundleName, assetName, typeof(T), assetType, assetFolderPath);
            if (request == null)
                return;
            // 
            LoadAssetInfo loadAssetInfo = new LoadAssetInfo(Time.realtimeSinceStartup, assetBundleName, assetName, loadedAction, request);
            s_Instance.m_LoadAssetInfos.Add(loadAssetInfo);
        }
        #endregion
    }
}
