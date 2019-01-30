using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AssetBundles.Manager
{
    public enum AssetBundleManagerMode : int
    {
        SimulationMode,                  // Load asset through AssetDatabase.LoadMainAssetAtPath() API.
        StreamingAssets,                 // Load assetbundles from Application.StreamingAssets.
        Server                           // Load one part of assetbundles from Application.StreamingAssets, other part download and load from remote file server.
    }

    public enum ServerDeployMode : int
    {
        Local,                          // Downlaod assetbundle from local deploy server.
        Remote                          // Download assetbundle form remote deploy server.
    }

    public enum UploadMode : int
    {
        UnityInternalWWW,               // Use WWW Unity internal class to upload files.
        ExternalUploadApplication       // Use external upload application to upload files.
    }

    public class Settings : ScriptableObject
    {

        public class Path
        {

            public static string SettingsFileName { get { return "AssetBundleManagerSettings"; } }

#if UNITY_EDITOR
            public static string BasePath
            {
                get
                {
                    string baseDirPath = BaseFullPath;

                    int index = baseDirPath.LastIndexOf(ASSETS_PATH);
                    Assert.IsTrue(index >= 0);

                    baseDirPath = baseDirPath.Substring(index);

                    return baseDirPath;
                }
            }

            public static string BaseFullPath
            {
                get
                {
                    var obj = ScriptableObject.CreateInstance<Settings>();
                    MonoScript s = MonoScript.FromScriptableObject(obj);
                    var configGuiPath = AssetDatabase.GetAssetPath(s);
                    UnityEngine.Object.DestroyImmediate(obj);

                    var fileInfo = new FileInfo(configGuiPath);
                    var baseDir = fileInfo.Directory;

                    Assert.AreEqual("AssetBundleManager", baseDir.Name);

                    return baseDir.ToString();
                }
            }

#if UNITY_EDITOR_WIN  
            public const string ASSETS_PATH = "Assets\\";
#elif UNITY_EDITOR_OSX
            public const string ASSETS_PATH = "Assets/";
#endif

            public static string GuiSkinPath { get { return BasePath + "/Editor/AssetBundle Manager Style.guiskin"; } }
            public static string ResourcesPath { get { return BasePath + "/Resources/"; } }
            public static string SettingsFilePath { get { return ResourcesPath + SettingsFileName + ".asset"; } }
#endif
        }

        [SerializeField] List<string> m_includeAssetBundlesInPlayer;
        [SerializeField] AssetBundleManagerMode m_mode;
        [SerializeField] private ServerDeployMode m_serverDeployMode;
        [SerializeField] AssetMap m_assetMap;
        [SerializeField] private bool m_clearCacheOnPlay;
        [SerializeField] private string m_localAssetBundleDirectory;
        [SerializeField] private string m_remoteServerURL;
        [SerializeField] private string m_localServerURL;
        [SerializeField] private bool m_isCheckVersionAndDownloadExcludeAssetBundles;
        [SerializeField] private string m_graphToolExportAssetBundlePath;
        [SerializeField] private UploadMode m_currentUploadMode;
        [SerializeField] private string m_externalUploadApplicationPath;
        [SerializeField] private int m_numberOfDownloadAndLoadWWW;                     // How many www instances to download and load assetbundles from remote server.
#if UNITY_EDITOR
        [SerializeField] private int m_version;
        private const int VERSION = 1;
#endif

        private static Settings s_settings;

        private static Settings GetSettings()
        {
            if (s_settings == null)
            {
                if (!Load())
                {
                    // Create vanilla db
                    s_settings = ScriptableObject.CreateInstance<Settings>();
#if UNITY_EDITOR
                    s_settings.m_version = VERSION;

                    var DBDir = Path.ResourcesPath;

                    if (!Directory.Exists(DBDir))
                    {
                        Directory.CreateDirectory(DBDir);
                    }

                    AssetDatabase.CreateAsset(s_settings, Path.SettingsFilePath);
                    AssetDatabase.SaveAssets();
#endif
                }
            }

            return s_settings;
        }

        private static bool Load()
        {
            bool loaded = false;

#if UNITY_EDITOR
            try
            {
                var dbPath = Path.SettingsFilePath;

                if (File.Exists(dbPath))
                {
                    Settings m = AssetDatabase.LoadAssetAtPath<Settings>(dbPath);

                    if (m != null && m.m_version == VERSION)
                    {
                        s_settings = m;
                        loaded = true;
                    }
                }
            }
            catch (Exception e)
            {
				Debug.LogException(e);
            }
#else
            Settings s = Resources.Load(Path.SettingsFileName) as Settings;
            s_settings = s;
            loaded = true;
#endif

            return loaded;
        }

#if UNITY_EDITOR
        public static string GraphToolExportAssetBundlePath
        {
            get
            {
                return GetSettings().m_graphToolExportAssetBundlePath;
            }
            set
            {
                var s = GetSettings();
                s.m_graphToolExportAssetBundlePath = value;
                EditorUtility.SetDirty(s);
            }
        }
#endif

#if UNITY_EDITOR
        public static UploadMode CurrentUploadMode
        {
            get
            {
                return GetSettings().m_currentUploadMode;
            }
            set
            {
                var s = GetSettings();
                s.m_currentUploadMode = value;
                EditorUtility.SetDirty(s);
            }
        }
#endif

#if UNITY_EDITOR
        public static string ExternalUploadApplicationPath
        {
            get
            {
                return GetSettings().m_externalUploadApplicationPath;
            }
            set
            {
                var s = GetSettings();
                s.m_externalUploadApplicationPath = value;
                EditorUtility.SetDirty(s);
            }
        }
#endif

        public static ServerDeployMode CurrentServerDeployMode
        {
            get
            {
                return GetSettings().m_serverDeployMode;
            }
#if UNITY_EDITOR
            set
            {
                var s = GetSettings();
                s.m_serverDeployMode = value;
                EditorUtility.SetDirty(s);
            }
#endif
        }

        public static string AssetBundleDirectory
        {
            get
            {
                return GetSettings().m_localAssetBundleDirectory;
            }
#if UNITY_EDITOR
            set
            {
                var s = GetSettings();
                s.m_localAssetBundleDirectory = value;
                EditorUtility.SetDirty(s);
            }
#endif
        }

        public static List<string> IncludeAssetBundlesInPlayer
        {
            get
            {
                if (GetSettings().m_includeAssetBundlesInPlayer == null)
                    GetSettings().m_includeAssetBundlesInPlayer = new List<string>();
                return GetSettings().m_includeAssetBundlesInPlayer;
            }
#if UNITY_EDITOR
            set
            {
                var s = GetSettings();
                s.m_includeAssetBundlesInPlayer = value;
                EditorUtility.SetDirty(s);
            }
#endif
        }

#if UNITY_EDITOR
        public static bool ClearCacheOnPlay
        {
            get
            {
                return GetSettings().m_clearCacheOnPlay;
            }
            set
            {
                var s = GetSettings();
                s.m_clearCacheOnPlay = value;
                EditorUtility.SetDirty(s);
            }
        }
#endif

        public static AssetBundleManagerMode Mode
        {
            get
            {
                return GetSettings().m_mode;
            }
#if UNITY_EDITOR
            set
            {
                var s = GetSettings();
                s.m_mode = value;
                EditorUtility.SetDirty(s);
            }
#endif
        }

        public static AssetMap Map
        {
            get
            {
                return GetSettings().m_assetMap;
            }
#if UNITY_EDITOR
            set
            {
                var s = GetSettings();
                s.m_assetMap = value;
                EditorUtility.SetDirty(s);
            }
#endif
        }

        public static string ManifestFileName
        {
            get
            {
                return Utility.GetPlatformName();
            }
        }

        public static string ServerURL
        {
            get
            {
                string url = string.Empty;
                if (CurrentServerDeployMode == ServerDeployMode.Local)
                {
                    url = GetSettings().m_localServerURL;
                }
                else if (CurrentServerDeployMode == ServerDeployMode.Remote)
                {
                    url = GetSettings().m_remoteServerURL;
                }
                return url;
            }
        }

#if UNITY_EDITOR
        public static string LocalServerURL
        {
            get
            {
                string url = GetLocalServerURL() + Utility.GetPlatformName() + "/";
                var s = GetSettings();
                if (s.m_localServerURL == null || !s.m_localServerURL.Equals(url))
                {
                    s.m_localServerURL = url;
                    EditorUtility.SetDirty(s);
                }
                return url;
            }
        }
#endif

#if UNITY_EDITOR
        public static string RemoteServerURL
        {
            get
            {
                return GetSettings().m_remoteServerURL;
            }

            set
            {
                var s = GetSettings();
                s.m_remoteServerURL = value;
                EditorUtility.SetDirty(s);
            }
        }
#endif

        public static int NumberofDownloadAndLoadWWW
        {
            get
            {
                return GetSettings().m_numberOfDownloadAndLoadWWW;
            }
#if UNITY_EDITOR
            set
            {
                var s = GetSettings();
                s.m_numberOfDownloadAndLoadWWW = value;
                EditorUtility.SetDirty(s);
            }
#endif
        }

        private static string GetLocalServerURL()
        {
            string localServerURL = string.Empty;
            string localIP = "";
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces(); ;
            foreach (NetworkInterface adapter in adapters)
            {
                if (adapter.Supports(NetworkInterfaceComponent.IPv4))
                {
                    UnicastIPAddressInformationCollection uniCast = adapter.GetIPProperties().UnicastAddresses;
                    if (uniCast.Count > 0)
                    {
                        foreach (UnicastIPAddressInformation uni in uniCast)
                        {
                            // Get IPv4 ip address.
                            if (uni.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                localIP = uni.Address.ToString();
                            }
                        }
                    }
                }
            }
            localServerURL = "http://" + localIP + ":7888/";
            return localServerURL;
        }

#if UNITY_EDITOR
        public static void SetSettingsDirty()
        {
            EditorUtility.SetDirty(s_settings);
        }
#endif
    }
}
