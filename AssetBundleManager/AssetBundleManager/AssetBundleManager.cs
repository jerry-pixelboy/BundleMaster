using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.AssetBundles.GraphTool;
#endif
using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text;

/*
 	In this demo, we demonstrate:
	1.	Automatic asset bundle dependency resolving & loading.
		It shows how to use the manifest assetbundle like how to get the dependencies etc.
	2.	Automatic unloading of asset bundles (When an asset bundle or a dependency thereof is no longer needed, the asset bundle is unloaded)
	3.	Editor simulation. A bool defines if we load asset bundles from the project or are actually using asset bundles(doesn't work with assetbundle variants for now.)
		With this, you can player in editor mode without actually building the assetBundles.
	4.	Optional setup where to download all asset bundles
	5.	Build pipeline build postprocessor, integration so that building a player builds the asset bundles and puts them into the player data (Default implmenetation for loading assetbundles from disk on any platform)
	6.	Use WWW.LoadFromCacheOrDownload and feed 128 bit hash to it when downloading via web
		You can get the hash from the manifest assetbundle.
	7.	AssetBundle variants. A prioritized list of variants that should be used if the asset bundle with that variant exists, first variant in the list is the most preferred etc.
*/

namespace AssetBundles.Manager
{
    // Loaded assetBundle contains the references count which can be used to unload dependent assetBundles automatically.
    public class LoadedAssetBundle
    {
        public AssetBundle m_AssetBundle;
        public int m_ReferencedCount;

        public LoadedAssetBundle(AssetBundle assetBundle)
        {
            m_AssetBundle = assetBundle;
            m_ReferencedCount = 1;
        }
    }

    public class DownloadAssetBundle
    {
        private string m_DownloadAssetBundleName;
        public string DownloadAssetBundleName { get { return this.m_DownloadAssetBundleName; } }

        private Action<string> m_DownloadedCallback;
        public Action<string> DownloadedCallback { get { return this.m_DownloadedCallback; } }

        public DownloadAssetBundle(string downloadAssetBundleName, Action<string> downloadedCallback)
        {
            this.m_DownloadAssetBundleName = downloadAssetBundleName;
            this.m_DownloadedCallback = downloadedCallback;
        }
    }

    // The wrapped WWW. 
    public class WrappedWWW : IDisposable
    {
        WWW m_www = null;
        public WWW www
        {
            get { return this.m_www; }
        }

        // Invoke the callback on assetbundle has been downloaded from remote server successfully.
        Action<string> m_DownloadedCallback = null;
        public Action<string> DownloadedCallback { get { return this.m_DownloadedCallback; } }

        // Track whether Dispose has been called.
        bool m_disposed;
        public bool disposed
        {
            get { return this.m_disposed; }
        }

        // if true, assetbundle is downloading from remote server, else is loading from local file disk.
        bool m_isDownloadingFromUrl;
        public bool IsDownloadingFromUrl { get { return this.m_isDownloadingFromUrl; } }

        // The start time on load or download assetbundle.
        float m_loadOrDownloadStartTime;
        public float LoadOrDownloadStartTime { get { return this.m_loadOrDownloadStartTime; } }

        public WrappedWWW(WWW www, Action<string> downloadedCallback, bool downloadingFromUrl, float loadOrDownloadStartTime)
        {
            m_www = www;
            m_DownloadedCallback = downloadedCallback;
            m_isDownloadingFromUrl = downloadingFromUrl;
            m_loadOrDownloadStartTime = loadOrDownloadStartTime;
            m_disposed = false;
        }

        // Wrap www.Dispose().
        public void Dispose()
        {
            if (m_www != null)
            {
                m_www.Dispose();
                m_disposed = true;
            }
        }
    }

    public enum AssetType
    {
        AnimationClip,
        AudioClip,
        AudioMixer,
        Font,
        GUISkin,
        Material,
        Mesh,
        Model,
        PhysicMaterial,
        Prefab,
        Scene,
        Script,
        TextAsset,
        Shader,
        Sprite,
        Texture
    }

    // Class takes care of loading assetBundle and its dependencies automatically, loading variants automatically.
    public class AssetBundleManager : MonoBehaviour
    {
        public const string VERSION_FILE_NAME = "assetbundles_version_{0}.txt";
        public const string EXCLUDE_ASSETBUNDLES_FILE_NAME = "exclude_assetbundles.txt";
        public static readonly string[] FILTER_ASSET_TYPE = new string[]
        {
            "t:AnimationClip",
            "t:AudioClip",
            "t:AudioMixer",
            "t:Font",
            "t:GUISkin",
            "t:Material",
            "t:Mesh",
            "t:Model",
            "t:PhysicMaterial",
            "t:Prefab",
            "t:Scene",
            "t:Script",
            "t:TextAsset",
            "t:Shader",
            "t:Sprite",
            "t:Texture"
        };

        public enum LogMode { All, JustErrors };
        public enum LogType { Info, Warning, Error };

        static LogMode m_LogMode = LogMode.All;
        static string[] m_ActiveVariants = { };
        static bool m_IsLoadingAssetBundleManifest = false;
        static AssetBundleManifest m_AssetBundleManifest = null;
        // Is the local version data updated?
        static bool m_isLocalVerDataDirty = false;
        // If set true, download assetbundles from url.
        static bool m_isDownloadingAssetBundles = false;
        static bool m_CheckVersionAndDownloadExcludeAssetbundles = false;
        // The data of download assetbundles progress,
        // the first element in array is current downloading progress, the second element is count of all assetbundles to download.
        static float[] m_downloadABsProgress = new float[2] { 0, 0 };
        public static float[] DownloadAssetBundlesProgress { get { return m_downloadABsProgress; } }
        // The assetbundle download queue.
        static Queue<DownloadAssetBundle> m_downloadABsQueue = new Queue<DownloadAssetBundle>();
        // The assetbundle list have downloaded successfully from remote server.
        static List<string> m_downloadedAssetBundles = new List<string>();

        static Dictionary<string, LoadedAssetBundle> m_LoadedAssetBundles = new Dictionary<string, LoadedAssetBundle>();
        static Dictionary<string, WrappedWWW> m_DownloadingWWWs = new Dictionary<string, WrappedWWW>();
        static Dictionary<string, string> m_DownloadingErrors = new Dictionary<string, string>();
        static List<AssetBundleLoadOperation> m_InProgressOperations = new List<AssetBundleLoadOperation>();
        static Dictionary<string, string[]> m_Dependencies = new Dictionary<string, string[]>();

        // Remote assetbundle version data dictionary.
        static Dictionary<string, int> m_remoteVerDic = new Dictionary<string, int>();
        // Local assetbundle version data dictionary.
        static Dictionary<string, int> m_localVerDic = new Dictionary<string, int>();
        static Dictionary<string, int> m_localVerDicInStreamingAssets = new Dictionary<string, int>();
        // Exclude assetbundles in build player.
        static List<string> m_excludeAssetBundlesInPlayer = new List<string>();

        #region callback
        // 
        public static Action OnConnectServer { set; get; }
        // 
        public static Action OnConnectServerSuccessfully { set; get; }
        // Return with error message on connect server error.
        public static Action<string> OnConnectServerError { set; get; }
        //
        public static Action OnPrepareUnityCaching { set; get; }
        // Invoke the callback on checking version files on app launch.
        public static Action OnCheckingVersion { set; get; }
        //
        public static Action OnStartDownloadAssetBundles { set; get; }
        // Invoke the callback on version data updated.
        public static Action OnVersionUpdated { set; get; }
        // Invoke the callback on all assetbundles have been downloaded or nothing to download.
        public static Action OnDownloadAssetBundlesFinished { set; get; }
        // Invoke the callback on AssetBundleManager initialized successfully.
        public static Action OnAssetBundleManagerInitialized { set; get; }
        #endregion

        static AssetBundleManager s_Instance = null;
        public static AssetBundleManager Instance
        {
            get { return s_Instance; }
        }

        public static LogMode logMode
        {
            get { return m_LogMode; }
            set { m_LogMode = value; }
        }

        // Variants which is used to define the active variants.
        public static string[] ActiveVariants
        {
            get { return m_ActiveVariants; }
            set { m_ActiveVariants = value; }
        }

        // AssetBundleManifest object which can be used to load the dependecies and check suitable assetBundle variants.
        public static AssetBundleManifest AssetBundleManifestObject
        {
            set { m_AssetBundleManifest = value; }
        }
			
		// Enable or disable AssetBundleManager log.
		private static bool m_logEnable = true;

		// Enable log.
		public static void EnableLog()
		{
			m_logEnable = true;
		}

		// Disable log.
		public static void DisableLog()
		{
			m_logEnable = false;
		}

        public static void Log(LogType logType, string text)
        {
			if (!m_logEnable)
				return;
            if (logType == LogType.Error)
				Debug.LogError("[AssetBundleManager] " + text);
            else if (m_LogMode == LogMode.All)
				Debug.Log("[AssetBundleManager] " + text);
        }

#if UNITY_EDITOR
        // Flag to indicate if we want to simulate assetBundles in Editor without building them actually.
        public static bool SimulateAssetBundleInEditor
        {
            get
            {
                return Settings.Mode != AssetBundleManagerMode.StreamingAssets
                    && Settings.Mode != AssetBundleManagerMode.Server;
            }
        }

        public static bool CleanCacheOnPlay
        {
            get
            {
                return Settings.ClearCacheOnPlay;
            }
        }
#endif

        public static string GetStreamingAssetsPath ()
		{
            if (Application.isEditor)
                return "file://" + Application.streamingAssetsPath;
            //return "file://" + System.Environment.CurrentDirectory.Replace("\\", "/"); // Use the build output folder directly.
            else if (Application.isWebPlayer)
                return System.IO.Path.GetDirectoryName(Application.absoluteURL).Replace("\\", "/") + "/StreamingAssets";
            else if (Application.isConsolePlatform)
                return "file://" + Application.streamingAssetsPath;
            else if (Application.platform == RuntimePlatform.Android)
                return Application.streamingAssetsPath; 
            else if (Application.platform == RuntimePlatform.IPhonePlayer)
                return "file://" + Application.streamingAssetsPath;
            else // For standalone player.
                return "file://" + Application.streamingAssetsPath;
		}

        // Get loaded AssetBundle, only return vaild object when all the dependencies are downloaded successfully.
        static public LoadedAssetBundle GetLoadedAssetBundle(string assetBundleName, out string error)
        {
            if (m_DownloadingErrors.TryGetValue(assetBundleName, out error))
                return null;

            LoadedAssetBundle bundle = null;
            m_LoadedAssetBundles.TryGetValue(assetBundleName, out bundle);
            if (bundle == null)
                return null;

            // No dependencies are recorded, only the bundle itself is required.
            string[] dependencies = null;
            if (!m_Dependencies.TryGetValue(assetBundleName, out dependencies))
                return bundle;

            // Make sure all dependencies are loaded
            foreach (var dependency in dependencies)
            {
                if (m_DownloadingErrors.TryGetValue(assetBundleName, out error))
                    return bundle;

                // Wait all the dependent assetBundles being loaded.
                LoadedAssetBundle dependentBundle;
                m_LoadedAssetBundles.TryGetValue(dependency, out dependentBundle);
                if (dependentBundle == null)
                    return null;
            }

            return bundle;
        }

        //static public InitManager()
        static public void InitManager(Action OnAssetBundleManagerInitializedCallback)
        {
            if (s_Instance == null)
            {
                var go = new GameObject("AssetBundleManager", typeof(AssetBundleManager));
                s_Instance = go.GetComponent<AssetBundleManager>();
                DontDestroyOnLoad(go);
            }

            Log(LogType.Info, "Mode: " + Settings.Mode.ToString());
            // 
            OnAssetBundleManagerInitialized += OnAssetBundleManagerInitializedCallback;

#if UNITY_EDITOR
            if (SimulateAssetBundleInEditor)
            {
                OnAssetBundleManagerInitialized();
                OnAssetBundleManagerInitialized = null;
            }
            else
#endif
            {
                if (Settings.Mode == AssetBundleManagerMode.StreamingAssets)
                {
                    LoadAssetBundle(Settings.ManifestFileName, true);
                    var operation = new AssetBundleLoadManifestOperation(Settings.ManifestFileName, "AssetBundleManifest", typeof(AssetBundleManifest));
                    m_InProgressOperations.Add(operation);
                }
                else if (Settings.Mode == AssetBundleManagerMode.Server)
                    s_Instance.StartCoroutine(s_Instance.IsServerReachable());
            }
        }

        public static void CheckVersion(bool checkVersionAndDownloadExcludeAssetbundles = false, Action onDownloadAssetBundlesFinishedOnAppLaunch = null)
        {
            OnDownloadAssetBundlesFinished = onDownloadAssetBundlesFinishedOnAppLaunch;
            m_CheckVersionAndDownloadExcludeAssetbundles = checkVersionAndDownloadExcludeAssetbundles;
            VersionComparison();
        }

        // Is server reachable?
        IEnumerator IsServerReachable()
        {
            if (OnConnectServer != null)
            {
                OnConnectServer();
                OnConnectServer = null;
            }

            WWW www = null;
            string reachURL = Settings.ServerURL.Replace(Utility.GetPlatformName(), "") + string.Format(VERSION_FILE_NAME, Utility.GetPlatformName().ToLower());
            www = new WWW(reachURL);
            yield return www;
            if (!string.IsNullOrEmpty(www.error))
            {
                if (OnConnectServerError != null)
                {
					Log(LogType.Error,www.error);
                    OnConnectServerError(www.error);
                }
            }
            else
            {
                if (OnConnectServerSuccessfully != null)
                {
                    OnConnectServerSuccessfully();
                    OnConnectServerSuccessfully = null;
                }

                if (OnPrepareUnityCaching != null)
                {
                    OnPrepareUnityCaching();
                    OnPrepareUnityCaching = null;
                }
                s_Instance.StartCoroutine(s_Instance.CachingReady(s_Instance.ExecuteOnCachingReady));
            }
        }

        // Unity's Caching is ready.
        IEnumerator CachingReady(Action readyCallFunc)
        {
            while (!Caching.ready)
                yield return null;
            if (Caching.ready)
            {
                Log(LogType.Info, "Caching is ready");
                Log(LogType.Info, "Caching maximum available disk space: " + Caching.maximumAvailableDiskSpace + " bytes");
                Log(LogType.Info, "Caching free space: " + Caching.spaceFree + " bytes");
                Log(LogType.Info, "Caching occupied space: " + Caching.spaceOccupied + " bytes");
                readyCallFunc();
                yield break;
            }
        }

        // Execute the mothed when caching is ready.
        void ExecuteOnCachingReady()
        {
            // Init.
            Initialize();
        }

        // Initialize.
        // Execute on Setting.Mode = Settings.AssetBundleManagerMode.Server.
        // Firstly, execute PrepareLocalVersionInfo() coroutine.
        // Secondly, execute PrepareRemoteVersionInfo() coroutine.
        void Initialize()
        {
#if UNITY_EDITOR
            Log(LogType.Info, CleanCacheOnPlay == true ? "Enable CleanCacheOnPlay" : "Disable CleanCacheOnPlay");

            if (CleanCacheOnPlay)
            {
                Log(LogType.Info, "Cleaned local cache.");
                Caching.CleanCache();
            }
#endif
            StartCoroutine(PrepareLocalVersionInfo());
        }

        // When app launch at first time, load local version file named assetbundles_version_{platform}.txt in StreamingAssets directory;
        // then remove exclude assetbundle version data recorded in the file named exclude_assetbundles.txt;
        // finally, store results into Application.temporaryCachePath path.
        IEnumerator PrepareLocalVersionInfo()
        {
            // Invoke callback method on checking version files.
            if (OnCheckingVersion != null)
                OnCheckingVersion();
            //
            m_localVerDic.Clear();
            m_excludeAssetBundlesInPlayer.Clear();
            m_localVerDicInStreamingAssets.Clear();
            // Local version file path.
            string localVersionFilePath = Application.temporaryCachePath + Path.AltDirectorySeparatorChar + string.Format(VERSION_FILE_NAME, Utility.GetPlatformName().ToLower());
            // exclude_assetbundles.txt file path.
            string excludeABsFilePath = Application.temporaryCachePath + Path.AltDirectorySeparatorChar + EXCLUDE_ASSETBUNDLES_FILE_NAME;

#if UNITY_EDITOR
            if (CleanCacheOnPlay)
            {
                Log(LogType.Info, "Cleaned version files.");
                // On CleanCacheOnPlay = true, it need to delete local version file and file named exclude_assetbundles.txt stored in Application.temporaryCachePath.
                if (File.Exists(localVersionFilePath))
                    File.Delete(localVersionFilePath);
                if (File.Exists(excludeABsFilePath))
                    File.Delete(excludeABsFilePath);
            }
#endif
            //Check local version file exist in Application.temporaryCachePath.
            if (!File.Exists(localVersionFilePath))
            {
                // Load local version data.
                WWW loadLocalVersionFileWWW = new WWW(GetStreamingAssetsPath() + Path.AltDirectorySeparatorChar + string.Format(VERSION_FILE_NAME, Utility.GetPlatformName().ToLower()));
				yield return loadLocalVersionFileWWW;
                if (loadLocalVersionFileWWW.error != null)
                {
                    Log(LogType.Error, loadLocalVersionFileWWW.error);
                    loadLocalVersionFileWWW.Dispose();
                    yield break;
                }
                // Load data exclude assetbundles in build player.
                WWW excludeAssetBundlesFileWWW = new WWW(GetStreamingAssetsPath() + Path.AltDirectorySeparatorChar + EXCLUDE_ASSETBUNDLES_FILE_NAME);
                yield return excludeAssetBundlesFileWWW;
                if (excludeAssetBundlesFileWWW.error != null)
                {
                    Log(LogType.Error, excludeAssetBundlesFileWWW.error);
                    excludeAssetBundlesFileWWW.Dispose();
                    yield break;
                }
                // Remove unwanted version data recorded in exclude_assetbundles.txt.
                StringReader excludeABsSR = new StringReader(excludeAssetBundlesFileWWW.text);
                string line;
                while ((line = excludeABsSR.ReadLine()) != null && line.Length > 0)
                {
                    m_excludeAssetBundlesInPlayer.Add(line);
                }
                excludeABsSR.Dispose();
                excludeABsSR.Close();
                // 
                StringReader versionDataSR = new StringReader(loadLocalVersionFileWWW.text);
                Dictionary<string, int> versionDataDic = new Dictionary<string, int>();
                string line01;
                while ((line01 = versionDataSR.ReadLine()) != null && line01.Length > 0)
                {
                    string[] data = line01.Split(';');
                    m_localVerDicInStreamingAssets.Add(data[0], int.Parse(data[1]));
                    versionDataDic.Add(data[0], int.Parse(data[1]));
                }
                versionDataSR.Dispose();
                versionDataSR.Close();
                // Remove unwanted assetbundle version data.
                for (int i = 0; i < m_excludeAssetBundlesInPlayer.Count; i++)
                {
                    versionDataDic.Remove(m_excludeAssetBundlesInPlayer[i]);
                    m_localVerDicInStreamingAssets.Remove(m_excludeAssetBundlesInPlayer[i]);
                }
                // Store the filtered version data.
                string filteredVersionData = string.Empty;
                List<string> keys = new List<string>(versionDataDic.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    // Keep the filtered version data dictionary object.
                    m_localVerDic.Add(keys[i], versionDataDic[keys[i]]);
                    //
                    filteredVersionData += (keys[i] + ";" + versionDataDic[keys[i]]);
                    if (i <= keys.Count - 1 - 1)
                        filteredVersionData += "\n";
                }
                //
                File.WriteAllText(localVersionFilePath, filteredVersionData, Encoding.UTF8);
                File.WriteAllText(excludeABsFilePath, excludeAssetBundlesFileWWW.text, Encoding.UTF8);
                loadLocalVersionFileWWW.Dispose();
                excludeAssetBundlesFileWWW.Dispose();
            }
            else // Exist.
            {
                // Read local version data.
                string versionDataContent = File.ReadAllText(localVersionFilePath, Encoding.UTF8);
                // 
                StringReader versionDataSR = new StringReader(versionDataContent);
                string line01;
                while ((line01 = versionDataSR.ReadLine()) != null && line01.Length > 0)
                {
                    string[] data = line01.Split(';');
                    m_localVerDic.Add(data[0], int.Parse(data[1]));
                }
                versionDataSR.Dispose();
                versionDataSR.Close();
                // Read data exclude assetbundles in build player.
                string excludeABsData = File.ReadAllText(excludeABsFilePath, Encoding.UTF8);
                StringReader excludeABsDataSR = new StringReader(excludeABsData);
                string line02;
                while ((line02 = excludeABsDataSR.ReadLine()) != null && line02.Length > 0)
                {
                    m_excludeAssetBundlesInPlayer.Add(line02);
                }
                // Load version data in SteamingAssets path.
                WWW loadVersionFileInSteamingAssetsWWW = new WWW(GetStreamingAssetsPath() + Path.AltDirectorySeparatorChar + string.Format(VERSION_FILE_NAME, Utility.GetPlatformName().ToLower()));
                yield return loadVersionFileInSteamingAssetsWWW;
                if (loadVersionFileInSteamingAssetsWWW.error != null)
                {
                    Log(LogType.Error, loadVersionFileInSteamingAssetsWWW.error);
                    loadVersionFileInSteamingAssetsWWW.Dispose();
                    yield break;
                }
                // 
                StringReader localVersionDataInSteamingAssetsSR = new StringReader(loadVersionFileInSteamingAssetsWWW.text);
                string line001;
                while ((line001 = localVersionDataInSteamingAssetsSR.ReadLine()) != null && line001.Length > 0)
                {
                    string[] data = line001.Split(';');
                    if (m_excludeAssetBundlesInPlayer.Contains(data[0]))
                        continue;
                    m_localVerDicInStreamingAssets.Add(data[0], int.Parse(data[1]));
                }
                localVersionDataInSteamingAssetsSR.Dispose();
                localVersionDataInSteamingAssetsSR.Close();
            }
            // 
            StartCoroutine(PrepareRemoteVersionInfo());
        }

        // Download remote version data.
        IEnumerator PrepareRemoteVersionInfo()
        {
            // Download remote assetbundles version data.
            m_remoteVerDic.Clear();
            WWW remoteVerDownload = null;
            string verUrl = Settings.ServerURL.Replace(Utility.GetPlatformName(), "") + string.Format(VERSION_FILE_NAME, Utility.GetPlatformName().ToLower());
            remoteVerDownload = new WWW(verUrl);
            yield return remoteVerDownload;
            if (remoteVerDownload.error != null)
            {
				Log(LogType.Error,remoteVerDownload.error);
                remoteVerDownload.Dispose();
                yield break;
            }
            StringReader sr = new StringReader(remoteVerDownload.text);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] param = line.Split(';');
                m_remoteVerDic.Add(param[0], int.Parse(param[1]));
            }
            sr.Dispose();
            sr.Close();
            remoteVerDownload.Dispose();
            // Version compare.
            VersionComparison();
        }

        // Compare assetbunlde version number.
        static void VersionComparison()
        {
            List<string> downloadABs = new List<string>();
            List<string> remoteVerKeys = new List<string>(m_remoteVerDic.Keys);
            for (int i = 0; i < remoteVerKeys.Count; i++)
            {
                if (remoteVerKeys[i].Equals(Utility.GetPlatformName()) && m_AssetBundleManifest != null)
                    continue;

                // Check version and download assetbundles exclude in player.
                if (m_CheckVersionAndDownloadExcludeAssetbundles)
                {
                    if (m_excludeAssetBundlesInPlayer.Contains(remoteVerKeys[i]) && !IsAssetBundleDownloaded(remoteVerKeys[i]))
                    {
                        downloadABs.Add(remoteVerKeys[i]);
                        continue;
                    }
                    else if (!IsAssetBundleDownloaded(remoteVerKeys[i]))
                        downloadABs.Add(remoteVerKeys[i]);
                }
                // Skip assetbundle version compare stored in exclude_assetbundles.txt file.
                else
                {
                    if (m_excludeAssetBundlesInPlayer.Contains(remoteVerKeys[i]))
                        continue;
                    if (!IsAssetBundleDownloaded(remoteVerKeys[i]))
                        downloadABs.Add(remoteVerKeys[i]);
                }
            }
            // Nothing download, load AssetBundle Manifest bundle.
            if (downloadABs.Count == 0)
            {
                LoadAssetBundle(Settings.ManifestFileName, true);
                var operation = new AssetBundleLoadManifestOperation(Settings.ManifestFileName, "AssetBundleManifest", typeof(AssetBundleManifest));
                m_InProgressOperations.Add(operation);
                //
                if (OnDownloadAssetBundlesFinished != null)
                {
                    OnDownloadAssetBundlesFinished();
                    OnDownloadAssetBundlesFinished = null;
                }
            }
            else
            {
                if (OnStartDownloadAssetBundles != null)
                {
                    OnStartDownloadAssetBundles();
                    OnStartDownloadAssetBundles = null;
                }

                m_isDownloadingAssetBundles = true;
                for (int i = 0; i < downloadABs.Count; i++)
                {
                    DownloadAssetBundle(downloadABs[i], null);
                }
            }
        }

        static void AssetBundleManagerInitSuccessfully()
        {
            Log(LogType.Info, "AssetBundleManager Initialized Successfully.");
            //
            OnVersionUpdated = null;
            OnCheckingVersion = null;
        }

        // Load AssetBundle and its dependencies.
        static public void LoadAssetBundle(string assetBundleName, bool isLoadingAssetBundleManifest = false, Action<string> finishedCallback = null)
        {
            Log(LogType.Info, "Loading Asset Bundle " + (isLoadingAssetBundleManifest ? "Manifest: " : ": ") + assetBundleName);

#if UNITY_EDITOR
            // If we're in Editor simulation mode, we don't have to really load the assetBundle and its dependencies.
            if (SimulateAssetBundleInEditor)
                return;
#endif

            if (!isLoadingAssetBundleManifest)
            {
                if (m_AssetBundleManifest == null)
                {
                    Log(LogType.Error, "Please initialize AssetBundleManifest by calling AssetBundleManager.Initialize()");
                    return;
                }
            }

            // Check if the assetBundle has already been processed.
            bool isAlreadyProcessed = LoadAssetBundleInternal(assetBundleName, isLoadingAssetBundleManifest, finishedCallback);

            // Load dependencies.
            if (!isAlreadyProcessed && !isLoadingAssetBundleManifest)
                LoadDependencies(assetBundleName, finishedCallback);
        }

        // Remaps the asset bundle name to the best fitting asset bundle variant.
        static protected string RemapVariantName(string assetBundleName)
        {
            string[] bundlesWithVariant = null;
#if UNITY_EDITOR
            if (SimulateAssetBundleInEditor)
            {
                bundlesWithVariant = AssetDatabase.GetAllAssetBundleNames();
            }
            else
#endif
            {
                bundlesWithVariant = m_AssetBundleManifest.GetAllAssetBundlesWithVariant();
            }

            string[] split = assetBundleName.Split('.');

            int bestFit = int.MaxValue;
            int bestFitIndex = -1;
            // Loop all the assetBundles with variant to find the best fit variant assetBundle.
            for (int i = 0; i < bundlesWithVariant.Length; i++)
            {
                string[] curSplit = bundlesWithVariant[i].Split('.');

                if (curSplit.Length < 2)
                {
                    continue;
                }

                if (curSplit[0] != split[0])
                    continue;

                int found = System.Array.IndexOf(m_ActiveVariants, curSplit[1]);

                // If there is no active variant found. We still want to use the first 
                if (found == -1)
                    found = int.MaxValue - 1;

                if (found < bestFit)
                {
                    bestFit = found;
                    bestFitIndex = i;
                }
            }

            if (bestFit == int.MaxValue - 1)
            {
                Log(LogType.Warning, "Ambigious asset bundle variant chosen because there was no matching active variant: " + bundlesWithVariant[bestFitIndex]);
            }

            if (bestFitIndex != -1)
            {
                return bundlesWithVariant[bestFitIndex];
            }
            else
            {
                return assetBundleName;
            }
        }

        // Where we actuall call WWW to download the assetBundle.
        static protected bool LoadAssetBundleInternal(string assetBundleName, bool isLoadingAssetBundleManifest, Action<string> finishedCallback = null)
        {
            // Already downloading.
            WrappedWWW wrappedWWW = null;
            m_DownloadingWWWs.TryGetValue(assetBundleName, out wrappedWWW);
            if (wrappedWWW != null)
                return true;

            // Already loaded.
            LoadedAssetBundle bundle = null;
            m_LoadedAssetBundles.TryGetValue(assetBundleName, out bundle);
            if (bundle != null)
            {
                bundle.m_ReferencedCount++;
                return true;
            }

            // @TODO: Do we need to consider the referenced count of WWWs?
            // In the demo, we never have duplicate WWWs as we wait LoadAssetAsync()/LoadLevelAsync() to be finished before calling another LoadAssetAsync()/LoadLevelAsync().
            // But in the real case, users can call LoadAssetAsync()/LoadLevelAsync() several times then wait them to be finished which might have duplicate WWWs.
            if (m_DownloadingWWWs.ContainsKey(assetBundleName))
                return true;

            WWW download = null;
            string url = string.Empty;
            if (Settings.Mode == AssetBundleManagerMode.StreamingAssets)
                url = GetStreamingAssetsPath() + "/Assetbundles/" + Utility.GetPlatformName() + "/" + assetBundleName;
            else if (Settings.Mode == AssetBundleManagerMode.Server)
                url = Settings.ServerURL + assetBundleName;

            // Directly load assetbundle from SteamingAssets directory.
            if (Settings.Mode == AssetBundleManagerMode.StreamingAssets)
            {
                download = new WWW(url);
                m_DownloadingWWWs.Add(assetBundleName, new WrappedWWW(download, finishedCallback, false, Time.realtimeSinceStartup));
            }
            else if (Settings.Mode == AssetBundleManagerMode.Server)
            {
                //
                int includedInPlayerVer = -1;
                int localVer = -1;
                int remoteVer = -1;
                m_localVerDic.TryGetValue(assetBundleName, out localVer);
                m_remoteVerDic.TryGetValue(assetBundleName, out remoteVer);
                if (m_localVerDicInStreamingAssets.TryGetValue(assetBundleName, out includedInPlayerVer))
                {
                    // If assetbundle remote version number as the same as version number stored in StreamingAssets directory and included in build player, 
                    // load assetbundle from StreamingAssets path, not download from remote server.
                    if (includedInPlayerVer == remoteVer)
                    {
                        url = GetStreamingAssetsPath() + "/" + Utility.GetPlatformName() + "/" + assetBundleName;
                        download = new WWW(url);
                    }
                    else
                        download = WWW.LoadFromCacheOrDownload(url, remoteVer, 0);
                }
                else
                    download = WWW.LoadFromCacheOrDownload(url, remoteVer, 0);
                //
                m_DownloadingWWWs.Add(assetBundleName, new WrappedWWW(download, finishedCallback, false, Time.realtimeSinceStartup));
            }
            return false;
        }

        // Where we get all the dependencies and load them all.
        static protected void LoadDependencies(string assetBundleName, Action<string> finishedCallback = null)
        {
            if (m_AssetBundleManifest == null)
            {
                Log(LogType.Error, "Please initialize AssetBundleManifest by calling AssetBundleManager.Initialize()");
                return;
            }

            // Get dependecies from the AssetBundleManifest object..
            string[] dependencies = m_AssetBundleManifest.GetAllDependencies(assetBundleName);
            if (dependencies.Length == 0)
                return;

            for (int i = 0; i < dependencies.Length; i++)
                dependencies[i] = RemapVariantName(dependencies[i]);

            // Record and load all dependencies.
            m_Dependencies.Add(assetBundleName, dependencies);
            Log(LogType.Info, "[Dependency]" + assetBundleName + " => " + string.Concat(dependencies));
            for (int i = 0; i < dependencies.Length; i++)
            {
                bool isLoaded = LoadAssetBundleInternal(dependencies[i], false, finishedCallback);
                if (!isLoaded)
                    Log(LogType.Info, "[Loading Dependency] loading " + dependencies[i]);
            }
        }

        // Unload assetbundle and its dependencies.
        static public void UnloadAssetBundle(string assetBundleName)
        {
#if UNITY_EDITOR
            // If we're in Editor simulation mode, we don't have to load the manifest assetBundle.
            if (SimulateAssetBundleInEditor)
                return;
#endif
            Log(LogType.Info, m_LoadedAssetBundles.Count + " assetbundle(s) in memory before unloading " + assetBundleName);

            UnloadAssetBundleInternal(assetBundleName);
            UnloadDependencies(assetBundleName);

            Log(LogType.Info, m_LoadedAssetBundles.Count + " assetbundle(s) in memory after unloading " + assetBundleName);
        }

        static protected void UnloadDependencies(string assetBundleName)
        {
            string[] dependencies = null;
            if (!m_Dependencies.TryGetValue(assetBundleName, out dependencies))
                return;

            // Loop dependencies.
            foreach (var dependency in dependencies)
            {
                UnloadAssetBundleInternal(dependency);
            }

            m_Dependencies.Remove(assetBundleName);
        }

        static protected void UnloadAssetBundleInternal(string assetBundleName)
        {
            string error;
            LoadedAssetBundle bundle = GetLoadedAssetBundle(assetBundleName, out error);
            if (bundle == null)
                return;

            if (--bundle.m_ReferencedCount == 0)
            {
                bundle.m_AssetBundle.Unload(false);
                m_LoadedAssetBundles.Remove(assetBundleName);

                Log(LogType.Info, assetBundleName + " has been unloaded successfully");
            }
        }

        void Update()
        {
            if (m_DownloadingWWWs.Count == 0 && m_InProgressOperations.Count == 0) return;

            // Include download and load assetbundle WWW instances.
            if (m_downloadABsQueue.Count > 0 && (m_DownloadingWWWs.Count < Settings.NumberofDownloadAndLoadWWW))
            {
                int willDownloadCount = Settings.NumberofDownloadAndLoadWWW - m_DownloadingWWWs.Count;
                for (int i = 0; i < willDownloadCount && m_downloadABsQueue.Count > 0; i++)
                {
                    DownloadAssetBundle da = m_downloadABsQueue.Dequeue();
                    WWW download = null;
                    string url = Settings.ServerURL + da.DownloadAssetBundleName;
                    int remoteVer = -1;
                    m_remoteVerDic.TryGetValue(da.DownloadAssetBundleName, out remoteVer);
                    download = WWW.LoadFromCacheOrDownload(url, remoteVer, 0);
                    m_DownloadingWWWs.Add(da.DownloadAssetBundleName, new WrappedWWW(download, da.DownloadedCallback, true, Time.realtimeSinceStartup));
                }
            }

            // Collect all the finished WWWs.
            var keysToRemove = new List<string>();
            foreach (var keyValue in m_DownloadingWWWs)
            {
                WrappedWWW download = keyValue.Value;

                // If downloading fails.
                if (!string.IsNullOrEmpty(download.www.error))
                {
                    string error = string.Empty;
                    if (download.IsDownloadingFromUrl)
                    {
                        if (!m_DownloadingErrors.TryGetValue(keyValue.Key, out error))
                            m_DownloadingErrors.Add(keyValue.Key, string.Format("Failed downloading asset bundle {0} from {1}: {2}", keyValue.Key, download.www.url, download.www.error));
                    }
                    else
                    {
                        if (!m_DownloadingErrors.TryGetValue(keyValue.Key, out error))
                            m_DownloadingErrors.Add(keyValue.Key, string.Format("Failed loading asset bundle {0} from {1}: {2}", keyValue.Key, download.www.url, download.www.error));
                    }

                    if (download.IsDownloadingFromUrl && download.DownloadedCallback != null)
                        download.DownloadedCallback(null);

                    keysToRemove.Add(keyValue.Key);
                    continue;
                }

                // If downloading succeeds.
                if (download.www.isDone)
                {
                    AssetBundle bundle = download.www.assetBundle;
                    if (bundle == null)
                    {
                        string error = string.Empty;
                        if (!m_DownloadingErrors.TryGetValue(keyValue.Key, out error))
                            m_DownloadingErrors.Add(keyValue.Key, string.Format("{0} is not a valid asset bundle.", keyValue.Key));

                        if (download.IsDownloadingFromUrl && download.DownloadedCallback != null)
                            download.DownloadedCallback(null);

                        keysToRemove.Add(keyValue.Key);
                        continue;
                    }

                    if (Settings.ManifestFileName.Equals(keyValue.Key))
                    {
                        if (download.IsDownloadingFromUrl)
                            Log(LogType.Info, "AssetBundle Manifest: " + keyValue.Key + " downloaded in " + (Time.realtimeSinceStartup - download.LoadOrDownloadStartTime) + " seconds");
                        else
                            Log(LogType.Info, "AssetBundle Manifest: " + keyValue.Key + " loaded in " + (Time.realtimeSinceStartup - download.LoadOrDownloadStartTime) + " seconds");
                    }
                    else
                    {
                        if (download.IsDownloadingFromUrl)
                            Log(LogType.Info, "AssetBundle: " + keyValue.Key + " downloaded in " + (Time.realtimeSinceStartup - download.LoadOrDownloadStartTime) + " seconds");
                        else
                            Log(LogType.Info, "AssetBundle: " + keyValue.Key + " loaded in " + (Time.realtimeSinceStartup - download.LoadOrDownloadStartTime) + " seconds");
                    }

                    if (!download.IsDownloadingFromUrl)
                        m_LoadedAssetBundles.Add(keyValue.Key, new LoadedAssetBundle(download.www.assetBundle));

                    keysToRemove.Add(keyValue.Key);
                }
            }

            // Calculate the progress of downloading assetbundles.
            CalculateDownloadAssetBundlesProgress();
            // Remove the finished WWWs.
            foreach (var key in keysToRemove)
            {
                WrappedWWW download = m_DownloadingWWWs[key];

                if (download.DownloadedCallback != null)
                    download.DownloadedCallback(key);

                if (download.IsDownloadingFromUrl)
                {
                    int remoteVer = 0;
                    m_remoteVerDic.TryGetValue(key, out remoteVer);
                    m_localVerDic.Remove(key);
                    m_localVerDic.Add(key, remoteVer);
                    m_isLocalVerDataDirty = true;
                }

                AssetBundle assetBundle = download.www.assetBundle;
                m_DownloadingWWWs.Remove(key);
                download.Dispose();

                if (download.IsDownloadingFromUrl)
                    assetBundle.Unload(true);
            }
            // Write local version data into local file.
            if (m_isLocalVerDataDirty)
                SaveLocalVerData();
            // Check any assetbundle is downloading from remote url.
            bool isDownloadingFromUrl = false;
            List<string> keys = new List<string>(m_DownloadingWWWs.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                WrappedWWW www = m_DownloadingWWWs[keys[i]];
                if (www.IsDownloadingFromUrl)
                {
                    isDownloadingFromUrl = true;
                    break;
                }
            }
            // Download completely, then load AssetBundle Manifest bundle.
            if (!isDownloadingFromUrl && m_downloadABsQueue.Count == 0 && m_isDownloadingAssetBundles)
            {
                m_isDownloadingAssetBundles = false;
                if (m_AssetBundleManifest == null && !m_IsLoadingAssetBundleManifest)
                {
                    LoadAssetBundle(Settings.ManifestFileName, true);
                    var operation = new AssetBundleLoadManifestOperation(Settings.ManifestFileName, "AssetBundleManifest", typeof(AssetBundleManifest));
                    m_InProgressOperations.Add(operation);
                    m_IsLoadingAssetBundleManifest = true;
                }
                //
                if (OnDownloadAssetBundlesFinished != null)
                {
                    OnDownloadAssetBundlesFinished();
                    OnDownloadAssetBundlesFinished = null;
                }
            }
            // Update all in progress operations
            for (int i = 0; i < m_InProgressOperations.Count;)
            {
                if (!m_InProgressOperations[i].Update())
                {
                    // 1. In StreamingAssets mode, load Assetbundle Mainfest bundle firstly.
                    // 2. In Server mode, download and load AssetBundle Mainfest bundle lastly after others assetbundle downloaded from remote server.
                    //
                    // As above situations, when Assetbundle Mainfest bundle has been downloaded and loaded completely; the AssetBundleManager init works done.
                    if (m_InProgressOperations[i] is AssetBundleLoadManifestOperation && OnAssetBundleManagerInitialized != null)
                    {
                        AssetBundleManagerInitSuccessfully();
                        OnAssetBundleManagerInitialized();
                        OnAssetBundleManagerInitialized = null;
                    }
                    m_InProgressOperations.RemoveAt(i);
                }
                else
                    i++;
            }
        }

        // Load asset from the given assetBundle.
        static public AssetBundleLoadAssetOperation LoadAssetAsync(string assetBundleName, string assetName, System.Type type, AssetType assetType, string assetFolderPath)
        {
            AssetBundleLoadAssetOperation operation = null;
            assetBundleName = RemapVariantName(assetBundleName);

#if UNITY_EDITOR
            if (SimulateAssetBundleInEditor)
            {
                Log(LogType.Info, "Loading " + assetName + " asset.");
                string[] assetPaths = null;
                if (Settings.Mode == AssetBundleManagerMode.SimulationMode)
                {
                    string[] guids = AssetDatabase.FindAssets(assetName + " " + FILTER_ASSET_TYPE[(int)assetType], new string[] { assetFolderPath });
                    foreach (string guid in guids)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (Path.GetFileNameWithoutExtension(assetPath).Equals(assetName))
                        {
                            assetPaths = new string[] { AssetDatabase.GUIDToAssetPath(guid) };
                            break;
                        }
                    }
                }

                if (assetPaths == null || assetPaths.Length == 0)
                {
                    if (Settings.Mode == AssetBundleManagerMode.SimulationMode)
                        Log(LogType.Error, "There is no asset with name [" + assetName + "] in the path [" + assetFolderPath + "]");
                    else
                        Log(LogType.Error, "There is no asset with name [" + assetName + "] in the assetbundle [" + assetBundleName + "]");
                    return null;
                }

                // @TODO: Now we only get the main object from the first asset. Should consider type also.
                UnityEngine.Object target = AssetDatabase.LoadMainAssetAtPath(assetPaths[0]);
                operation = new AssetBundleLoadAssetOperationSimulation(target);
            }
            else
#endif
            {
                if (Settings.Mode == AssetBundleManagerMode.StreamingAssets)
                {
                    Log(LogType.Info, "Loading " + assetName + " from " + assetBundleName + " bundle");
                    LoadAssetBundle(assetBundleName);
                    operation = new AssetBundleLoadAssetOperationFull(assetBundleName, assetName, type);
                    m_InProgressOperations.Add(operation);
                }
                else if (Settings.Mode == AssetBundleManagerMode.Server)
                {
                    if (IsAssetBundleDownloaded(assetBundleName))
                    {
                        Log(LogType.Info, "Loading " + assetName + " from " + assetBundleName + " bundle");
                        LoadAssetBundle(assetBundleName);
                        operation = new AssetBundleLoadAssetOperationFull(assetBundleName, assetName, type);
                        m_InProgressOperations.Add(operation);
                    }
                    else
                    {
                        Log(LogType.Error, "the asset bundle " + assetBundleName + " is not downloaded, please firstly download it then load!");
                    }
                }
            }
            return operation;
        }

        // Load level from the given assetBundle.
        static public AssetBundleLoadOperation LoadLevelAsync(string assetBundleName, string levelName, bool isAdditive, List<WrappedWWW> downloadingWWWs, List<AssetBundleLoadOperation> inProgressOperations,string sceneFolderPath)
        {
            AssetBundleLoadOperation operation = null;
            assetBundleName = RemapVariantName(assetBundleName);
#if UNITY_EDITOR
            if (SimulateAssetBundleInEditor)
            {
                Log(LogType.Info, "Loading " + levelName + " scene asset.");
                operation = new AssetBundleLoadLevelSimulationOperation(assetBundleName, levelName, isAdditive, sceneFolderPath);
                //
                m_InProgressOperations.Add(operation);
                inProgressOperations.Add(operation);
            }
            else
#endif
            if (Settings.Mode == AssetBundleManagerMode.StreamingAssets || (Settings.Mode == AssetBundleManagerMode.Server && IsAssetBundleDownloaded(assetBundleName)))
            {
                Log(LogType.Info, "Loading " + levelName + " scene from " + assetBundleName + " bundle");
                LoadAssetBundle(assetBundleName);
                string[] dependenciesAssetBundleName = null;
                m_Dependencies.TryGetValue(assetBundleName, out dependenciesAssetBundleName);
                List<string> willLoadedAssetBundles = new List<string>();
                willLoadedAssetBundles.Add(assetBundleName);
                if (dependenciesAssetBundleName != null)
                {
                    for (int i = 0; i < dependenciesAssetBundleName.Length; i++)
                    {
                        willLoadedAssetBundles.Add(dependenciesAssetBundleName[i]);
                    }
                }
                operation = new AssetBundleLoadLevelOperation(assetBundleName, levelName, isAdditive);
                for (int i = 0; i < willLoadedAssetBundles.Count; i++)
                {
                    WrappedWWW www = null;
                    m_DownloadingWWWs.TryGetValue(willLoadedAssetBundles[i], out www);
                    if (www != null)
                        downloadingWWWs.Add(www);
                }
                
                m_InProgressOperations.Add(operation);
                inProgressOperations.Add(operation);
            }
            else if (Settings.Mode == AssetBundleManagerMode.Server && !IsAssetBundleDownloaded(assetBundleName))
            {
                Log(LogType.Info, "the scene bundle " + assetBundleName + " is not downloaded, please firstly download it then load!");
            }
            return operation;
        }

        /// <summary>
        /// Version compare and download assetbundle from remote server.
        /// </summary>
        /// <param name="assetBundleName">download assetbundle name.</param>
        /// <param name="finishedCallback">finished callback; if successed return with assetbundle name, if failed return with null.</param>
        static public void DownloadAssetBundle(string assetBundleName, Action<string> finishedCallback)
        {
            if (!IsAssetBundleDownloaded(assetBundleName))
            {
                // Reset download progress value.
                if (m_downloadABsProgress[0] == m_downloadABsProgress[1])
                {
                    m_downloadedAssetBundles.Clear();
                    m_downloadABsProgress[0] = m_downloadABsProgress[1] = 0;
                }
                m_downloadABsProgress[1] += 1;
                m_downloadABsQueue.Enqueue(new DownloadAssetBundle(assetBundleName, finishedCallback));
            }
            else
            {
                //Already downloaded.
                if (finishedCallback != null)
                    finishedCallback(assetBundleName);
            }
        }

        // Calculate the progress of downloading assetbundles.
        static private void CalculateDownloadAssetBundlesProgress()
        {
            if (m_downloadedAssetBundles.Count == m_downloadABsProgress[1])
                return;

            float notFinishProgress = 0;
            foreach (KeyValuePair<string, WrappedWWW> kp in m_DownloadingWWWs)
            {
                WrappedWWW wrappedWWW = kp.Value;
                if (!wrappedWWW.IsDownloadingFromUrl)
                    continue;
                if (!string.IsNullOrEmpty(wrappedWWW.www.error))
                    continue;
                if (wrappedWWW.www.isDone)
                {
                    if (wrappedWWW.www.assetBundle == null)
                        continue;
                    else
                        m_downloadedAssetBundles.Add(kp.Key);
                }
                else
                    notFinishProgress += wrappedWWW.www.progress;
            }

            m_downloadABsProgress[0] = m_downloadedAssetBundles.Count + notFinishProgress;
            Log(LogType.Info, "Download assetbundles progress: " + m_downloadABsProgress[0] + "/" + m_downloadABsProgress[1]);
        }

        // Is assetbundle will download from remote server.
        // if true downloaded, else not downloaded.
        static private bool IsAssetBundleDownloaded(string assetBundleName)
        {
            bool isDownloaded = true;
            int remoteVer = -1;
            m_remoteVerDic.TryGetValue(assetBundleName, out remoteVer);
            int localVer = -1;
            if (m_localVerDic.TryGetValue(assetBundleName, out localVer))
            {
                // version changed.
                if (localVer != remoteVer)
                    isDownloaded = false;
                // Alreay downloaded.
                else
                    isDownloaded = true;
            }
            else
                isDownloaded = false;
            return isDownloaded;
        }

        // Save local version data into file.
        static private void SaveLocalVerData()
        {
            // Local version file path.
            string localVersionFilePath = Application.temporaryCachePath + Path.AltDirectorySeparatorChar + string.Format(VERSION_FILE_NAME, Utility.GetPlatformName().ToLower());
			FileStream fs = new FileStream(localVersionFilePath, FileMode.Create);
            StreamWriter sw = new StreamWriter(fs);
            string content = string.Empty;
            List<string> keys = new List<string>(m_localVerDic.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                content += (keys[i] + ";" + m_localVerDic[keys[i]]);
                if (i <= keys.Count - 1 - 1)
                    content += "\n";
            }
            sw.Write(content);
            sw.Flush();
            sw.Close();
            fs.Close();
            //
            m_isLocalVerDataDirty = false;
            // callback on version data updated.
            if (OnVersionUpdated != null)
                OnVersionUpdated();
            // Log info to console.
            Log(LogType.Info, "Save local version data.");
        }
    } // End of AssetBundleManager.
}