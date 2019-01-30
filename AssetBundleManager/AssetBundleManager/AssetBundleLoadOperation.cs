using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

using System.IO;

namespace AssetBundles.Manager
{
    public abstract class AssetBundleLoadOperation : IEnumerator
    {
        public object Current
        {
            get
            {
                return null;
            }
        }
        public bool MoveNext()
        {
            return !IsDone();
        }

        public void Reset()
        {
        }

        abstract public bool Update();

        abstract public bool IsDone();
    }

#if UNITY_EDITOR
    public class AssetBundleLoadLevelSimulationOperation : AssetBundleLoadOperation
    {
        AsyncOperation m_Operation = null;
        private string m_LevelName;
        public string LevelName { get { return this.m_LevelName; } }

        private string m_AssetBundleName;

        public string AssetBundleName { get { return this.m_AssetBundleName; } }

        protected float m_TimeStamp;

        public float TimeStamp { get { return this.m_TimeStamp; } }

        public AsyncOperation LoadLevelRequest
        {
            get
            {
                return this.m_Operation;
            }
        }

        public AssetBundleLoadLevelSimulationOperation(string assetBundleName, string levelName, bool isAdditive, string sceneFolderPath)
        {
            this.m_LevelName = levelName;
            this.m_AssetBundleName = assetBundleName;
            this.m_TimeStamp = Time.realtimeSinceStartup;

            string[] levelPaths = null;
            string[] guids = AssetDatabase.FindAssets(levelName + " t:Scene", new string[] { sceneFolderPath });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(assetPath).Equals(levelName))
                {
                    levelPaths = new string[] { AssetDatabase.GUIDToAssetPath(guid) };
                    break;
                }
            }

            if (levelPaths == null || levelPaths.Length == 0)
            {
                ///@TODO: The error needs to differentiate that an asset bundle name doesn't exist
                //        from that there right scene does not exist in the asset bundle...
                if (Settings.Mode == AssetBundleManagerMode.SimulationMode)
                    AssetBundleManager.Log(AssetBundleManager.LogType.Error, "There is no scene with name [" + levelName + "] in the path [" + sceneFolderPath + "]");
                else
                    AssetBundleManager.Log(AssetBundleManager.LogType.Error, "There is no scene with name [" + levelName + "] in the assetbundle [" + assetBundleName + "]");
                return;
            }

            if (isAdditive)
                m_Operation = UnityEditor.EditorApplication.LoadLevelAdditiveAsyncInPlayMode(levelPaths[0]);
            else
                m_Operation = UnityEditor.EditorApplication.LoadLevelAsyncInPlayMode(levelPaths[0]);

            m_Operation.allowSceneActivation = true;
        }

        public override bool Update()
        {
            return false;
        }

        public override bool IsDone()
        {
            //return m_Operation == null || m_Operation.isDone;
            return m_Operation == null || m_Operation.progress >= 0.9f;
        }
    }

#endif
    public class AssetBundleLoadLevelOperation : AssetBundleLoadOperation
    {
        protected string m_AssetBundleName;
        protected string m_LevelName;
        protected bool m_IsAdditive;
        protected string m_DownloadingError;
        protected AsyncOperation m_Request;
        protected float m_TimeStamp;

        public string AssetBundleName { get { return this.m_AssetBundleName; } }

        public string LevelName { get { return this.m_LevelName; } }

        public float TimeStamp { get { return this.m_TimeStamp; } }

        public AsyncOperation LoadLevelRequest
        {
            get
            {
                return this.m_Request;
            }
        }

        public AssetBundleLoadLevelOperation(string assetbundleName, string levelName, bool isAdditive)
        {
            m_AssetBundleName = assetbundleName;
            m_LevelName = levelName;
            m_IsAdditive = isAdditive;
            this.m_TimeStamp = Time.realtimeSinceStartup;
        }

        public override bool Update()
        {
            if (m_Request != null)
                return false;

            LoadedAssetBundle bundle = AssetBundleManager.GetLoadedAssetBundle(m_AssetBundleName, out m_DownloadingError);
            if (bundle != null)
            {
                if (m_IsAdditive)
                {
                    m_Request = SceneManager.LoadSceneAsync(m_LevelName, LoadSceneMode.Additive);
                    m_Request.allowSceneActivation = true;
                }
                else
                {
                    m_Request = SceneManager.LoadSceneAsync(m_LevelName, LoadSceneMode.Single);
                    m_Request.allowSceneActivation = true;
                }
                return false;
            }
            else
                return true;
        }

        public override bool IsDone()
        {
            // Return if meeting downloading error.
            // m_DownloadingError might come from the dependency downloading.
            if (m_Request == null && m_DownloadingError != null)
            {
                AssetBundleManager.Log(AssetBundleManager.LogType.Error, m_DownloadingError);
                return true;
            }

            // Set allowSceneActivation = true manually.
            return m_Request != null && m_Request.progress >= 0.9f;
            //return m_Request != null && m_Request.isDone;
        }
    }

    public abstract class AssetBundleLoadAssetOperation : AssetBundleLoadOperation
    {
        public abstract T GetAsset<T>() where T : UnityEngine.Object;
    }

    public class AssetBundleLoadAssetOperationSimulation : AssetBundleLoadAssetOperation
    {
        Object m_SimulatedObject;

        public AssetBundleLoadAssetOperationSimulation(Object simulatedObject)
        {
            m_SimulatedObject = simulatedObject;
        }

        public override T GetAsset<T>()
        {
            return m_SimulatedObject as T;
        }

        public override bool Update()
        {
            return false;
        }

        public override bool IsDone()
        {
            return true;
        }
    }

    public class AssetBundleLoadAssetOperationFull : AssetBundleLoadAssetOperation
    {
        protected string m_AssetBundleName;
        protected string m_AssetName;
        protected string m_DownloadingError;
        protected System.Type m_Type;
        protected AssetBundleRequest m_Request = null;

        public AssetBundleLoadAssetOperationFull(string bundleName, string assetName, System.Type type)
        {
            m_AssetBundleName = bundleName;
            m_AssetName = assetName;
            m_Type = type;
        }

        public override T GetAsset<T>()
        {
            if (m_Request != null && m_Request.isDone)
                return m_Request.asset as T;
            else
                return null;
        }

        // Returns true if more Update calls are required.
        public override bool Update()
        {
            if (m_Request != null)
                return false;

            LoadedAssetBundle bundle = AssetBundleManager.GetLoadedAssetBundle(m_AssetBundleName, out m_DownloadingError);
            if (bundle != null)
            {
                ///@TODO: When asset bundle download fails this throws an exception...
                m_Request = bundle.m_AssetBundle.LoadAssetAsync(m_AssetName, m_Type);
                return false;
            }
            else
            {
                return true;
            }
        }

        public override bool IsDone()
        {
            // Return if meeting downloading error.
            // m_DownloadingError might come from the dependency downloading.
            if (m_Request == null && m_DownloadingError != null)
            {
                AssetBundleManager.Log(AssetBundleManager.LogType.Error, m_DownloadingError);
                return true;
            }

            return m_Request != null && m_Request.isDone;
        }
    }

    public class AssetBundleLoadManifestOperation : AssetBundleLoadAssetOperationFull
    {
        public AssetBundleLoadManifestOperation(string bundleName, string assetName, System.Type type)
            : base(bundleName, assetName, type)
        {
        }

        public override bool Update()
        {
            base.Update();

            if (m_Request != null && m_Request.isDone)
            {
                AssetBundleManager.AssetBundleManifestObject = GetAsset<AssetBundleManifest>();
                return false;
            }
            else
                return true;
        }
    }


}
