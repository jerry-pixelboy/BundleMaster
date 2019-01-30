using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Extension;
using UnityEngine;
using UnityEngine.AssetBundles.GraphTool;

namespace AssetBundles.Manager
{
    public class AssetBundleManagerControlPanel : EditorWindow
    {
        private static AssetBundleManagerControlPanel s_window;
        private Vector2 m_scrollPos;
        protected bool m_Focus;
        private bool isLocalServerFoldout = true;
        private bool isRemoteServerFoldout = true;

        [MenuItem("[Bundle Producer]/Bundle Manager/Open Control Panel...", false, 4)]
        public static void Open()
        {
            s_window = GetWindow<AssetBundleManagerControlPanel>();
        }

        public static Texture2D LoadTextureFromFile(string path)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        private void Init()
        {
            this.titleContent = new GUIContent("Bundle Manager");
        }

        public void OnEnable()
        {
            Init();
        }

        public void OnFocus()
        {
            m_Focus = true;
        }

        private void OnLostFocus()
        {
            m_Focus = false;
        }

        public void OnDisable()
        {
        }

        public string DrawFolderSelector(string label,
            string dialogTitle,
            string currentDirPath,
            string directoryOpenPath,
            Func<string, string> onValidFolderSelected = null)
        {
            string newDirPath;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Space(5f);
                    if (string.IsNullOrEmpty(label))
                    {
                        newDirPath = EditorGUILayout.TextField(currentDirPath);
                    }
                    else
                    {
                        newDirPath = EditorGUILayout.TextField(label, currentDirPath);
                    }
                }
                if (GUILayout.Button("Select", GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false)))
                {
                    var folderSelected =
                        EditorUtility.OpenFolderPanel(dialogTitle, directoryOpenPath, "");
                    if (!string.IsNullOrEmpty(folderSelected))
                    {
                        if (onValidFolderSelected != null)
                        {
                            newDirPath = onValidFolderSelected(folderSelected);
                        }
                        else
                        {
                            newDirPath = folderSelected;
                        }
                    }
                }
            }
            return newDirPath;
        }

        public void OnGUI()
        {
            if (Settings.GraphToolExportAssetBundlePath == null || Settings.GraphToolExportAssetBundlePath.Length == 0)
            {
                ShowNotification(new GUIContent("Please export assetbundles by AssetBundleGraphTool"));
                GUI.enabled = false;
            }
            GUILayout.Space(4f);
            m_scrollPos = EditorGUILayout.BeginScrollView(m_scrollPos);
            DrawNormalSettingGUI();
            DrawServerSettingGUI();
            EditorGUILayout.EndScrollView();
            GUI.enabled = true;
        }

        void DrawNormalSettingGUI()
        {
            GUILayout.Space(2f);
            bool newClearCache = EditorGUILayout.ToggleLeft("Clear Cache On Play (Editor)", AssetBundleManager.CleanCacheOnPlay);
            if (newClearCache != AssetBundleManager.CleanCacheOnPlay)
            {
                Settings.ClearCacheOnPlay = newClearCache;
            }

            GUILayout.Space(4f);

            if (Settings.NumberofDownloadAndLoadWWW == 0) Settings.NumberofDownloadAndLoadWWW = 20;
            int newNumberOfDownloadWWW = EditorGUILayout.IntSlider("The WWW Count", Settings.NumberofDownloadAndLoadWWW, 1, 100);
            EditorGUILayout.HelpBox("The Count of WWWs that download assetbundle from remote server.", MessageType.Info);
            if (newNumberOfDownloadWWW != Settings.NumberofDownloadAndLoadWWW)
            {
                Settings.NumberofDownloadAndLoadWWW = newNumberOfDownloadWWW;
            }

            GUILayout.Space(2f);

            Settings.Mode = (AssetBundleManagerMode)EditorGUILayout.EnumPopup("AssetBundles Load Mode", Settings.Mode);
            if (Settings.Mode == AssetBundleManagerMode.SimulationMode)
                EditorGUILayout.HelpBox("Directly, load asset from asset path in project directory.(Only work for Editor)", MessageType.Info);
            else if (Settings.Mode == AssetBundleManagerMode.StreamingAssets)
                EditorGUILayout.HelpBox("Load asset from assetbundles exported to SteamingAssets directory, and don't need to execute version comparison.(Both work for Editor and Player.)", MessageType.Info);
            else if (Settings.Mode == AssetBundleManagerMode.Server)
                EditorGUILayout.HelpBox("Load asset from assetbundle which one part in StreamingAssets directory, other part downloaded from server; and execute version comparison(Both work for Editor and Player.)", MessageType.Info);

            GUILayout.Space(2f);
        }

        void DrawServerSettingGUI()
        {
            Settings.CurrentServerDeployMode = (ServerDeployMode)EditorGUILayout.EnumPopup("Server Deploy Mode", Settings.CurrentServerDeployMode);

            isLocalServerFoldout = EditorGUILayout.Foldout(isLocalServerFoldout, "Local Server");
            if (isLocalServerFoldout)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16f);
                EditorGUILayout.BeginVertical("HelpBox");
                // AssetBundles Directory.
                var newFolder1 = DrawFolderSelector("AssetBundle Directory",
                           "Select AssetBundle Directory",
                           Settings.AssetBundleDirectory,
                           Application.dataPath + "/../");
                if (newFolder1 != Settings.AssetBundleDirectory)
                {
                    Settings.AssetBundleDirectory = newFolder1;
                }

                EditorGUILayout.LabelField("Manifest File Name", Settings.ManifestFileName, "WordWrappedLabel");

                bool isRunning = false;
#if UNITY_EDITOR_WIN
                isRunning = LaunchAssetBundleServer.IsRunning();
#endif
                EditorGUILayout.LabelField("Local Server Running", isRunning.ToString(), "WordWrappedLabel");
                EditorGUILayout.LabelField("Local Server URL", Settings.LocalServerURL, "WordWrappedLabel");

#if UNITY_EDITOR_WIN
                EditorGUILayout.HelpBox(LaunchAssetBundleServer.GetServerArgs(), MessageType.Info);
#elif UNITY_EDITOR_OSX
                						    EditorGUILayout.HelpBox("Server Args", MessageType.Info);
#endif

                using (new EditorGUI.DisabledScope(Settings.Mode != AssetBundleManagerMode.Server))
                {
                    if (GUILayout.Button(isRunning ? "Stop Local Server" : "Start Local Server", GUILayout.MinHeight(30)))
                    {
                        if (!isRunning)
                            PrepareOnLocalServerRun();
                        LaunchAssetBundleServer.ToggleLocalAssetBundleServer();
                    }
                    GUILayout.Space(2f);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }

            isRemoteServerFoldout = EditorGUILayout.Foldout(isRemoteServerFoldout, "Remote Server");
            if (isRemoteServerFoldout)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16f);
                EditorGUILayout.BeginVertical("HelpBox");
                EditorGUILayout.LabelField("Manifest File Name", Settings.ManifestFileName, "WordWrappedLabel");

                Settings.RemoteServerURL = EditorGUILayout.TextField("Remote Server URL", Settings.RemoteServerURL);
                if (Utility.IsURL(Settings.RemoteServerURL))
                    EditorGUILayout.HelpBox("URL format is correct.", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("URL format is incorrect.", MessageType.Error);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
        }

        // Prepare something on start run local server.
        void PrepareOnLocalServerRun()
        {
            if (!Directory.Exists(Settings.AssetBundleDirectory))
                return;
            CopyVersionAndAssetBundlesFilesToFileServerWorkDirectory();
        }

        // Copy version data and assetbundles files into file server work directory where download from.
        void CopyVersionAndAssetBundlesFilesToFileServerWorkDirectory()
        {
            // If work directory not exist, do nothing.
            if (!Directory.Exists(Settings.AssetBundleDirectory))
                return;
            // Copy version data file.
            string versionFilePath = string.Empty;
            if (Settings.GraphToolExportAssetBundlePath.StartsWith("Assets"))
                versionFilePath = Application.dataPath + Path.AltDirectorySeparatorChar + Settings.GraphToolExportAssetBundlePath.Replace("Assets/", "") + Path.AltDirectorySeparatorChar + "Assetbundles" + Path.AltDirectorySeparatorChar + string.Format(AssetBundleManager.VERSION_FILE_NAME, Utility.GetPlatformName().ToLower());
            else
                versionFilePath = Settings.GraphToolExportAssetBundlePath + Path.AltDirectorySeparatorChar + string.Format(AssetBundleManager.VERSION_FILE_NAME, Utility.GetPlatformName().ToLower());
            if (!File.Exists(versionFilePath))
            {
                Debug.LogError(versionFilePath + " not found!");
                return;
            }
            // Copy
            string destFileName = Settings.AssetBundleDirectory + Path.AltDirectorySeparatorChar + string.Format(AssetBundleManager.VERSION_FILE_NAME, Utility.GetPlatformName().ToLower());
            File.Copy(versionFilePath, destFileName, true);
            // Copy AssetBundles
            string paltformAssetBundlesDir = string.Empty;
            string exportPlatformAssetBundlesDir = string.Empty;
            paltformAssetBundlesDir = Settings.AssetBundleDirectory + Path.AltDirectorySeparatorChar + "Assetbundles" + Path.AltDirectorySeparatorChar + Utility.GetPlatformName();
            exportPlatformAssetBundlesDir = Settings.GraphToolExportAssetBundlePath + Path.AltDirectorySeparatorChar + Utility.GetPlatformName();
            if (!Directory.Exists(paltformAssetBundlesDir))
                Directory.CreateDirectory(paltformAssetBundlesDir);
            else
            {
                Directory.Delete(paltformAssetBundlesDir, true);
                Directory.CreateDirectory(paltformAssetBundlesDir);
            }
            if (!Directory.Exists(exportPlatformAssetBundlesDir))
            {
                Debug.LogError("No exist assetbundles in path: " + exportPlatformAssetBundlesDir + ", Please export(exported by AssetBundleGraphTool) assetbundles firstly.");
                return;
            }
            // Copy assetbundle files.
            string[] assetBundlefiles = Directory.GetFiles(exportPlatformAssetBundlesDir);
            foreach (string filePath in assetBundlefiles)
            {
                string fileExt = Path.GetExtension(filePath);
                if (fileExt.Equals(".meta") || fileExt.Equals(".manifest"))
                {
                    continue;
                }
                string fileName = Path.GetFileName(filePath);
                string toFileName = Path.Combine(paltformAssetBundlesDir, fileName);
                File.Copy(filePath, toFileName, true);
            }
        }

        // Export AssetBundle Manager Config.
        void ExportAssetBundleManagerConfig()
        {
            var filePathSelected = EditorUtility.SaveFilePanel("Export AssetBundle Manager Config", Application.dataPath, string.Format(AssetBundleManagerConfig.CONFIG_FILE_NAME, Utility.GetPlatformName().ToLower()), "txt");
            if (!string.IsNullOrEmpty(filePathSelected))
            {
                AssetBundleManagerConfig config = new AssetBundleManagerConfig();
                config.platform = Utility.GetPlatformName();
                AssetBundleVersionDatabase versionDb = AssetBundleVersionDatabase.GetVersionDB();
                AssetBundlePlatformVersion versionPlatform = versionDb.GetAssetBundlePlatformVersion(Utility.GetPlatformName());
                config.includedInPlayerAssetBundles = new List<AssetBundleManagerConfig.AssetBundleVersion>();
                foreach (string assetBundleName in Settings.IncludeAssetBundlesInPlayer)
                {
                    AssetBundleVersion ver = versionPlatform.GetAssetBundleVersion(assetBundleName);
                    if (ver == null)
                        continue;
                    AssetBundleManagerConfig.AssetBundleVersion item = new AssetBundleManagerConfig.AssetBundleVersion();
                    item.assetBundleName = ver.AssetBundleName;
                    item.version = ver.Version;
                    config.includedInPlayerAssetBundles.Add(item);
                }
                config.currentAssetBundleManagerMode = Settings.Mode;
                config.currentServerDeployMode = Settings.CurrentServerDeployMode;
                config.remoteServerURL = Settings.RemoteServerURL;
                config.localServerURL = Settings.LocalServerURL;
                string content = AssetBundleManagerConfig.ToJson(config);
                // Create file and write content into it.
                File.WriteAllText(filePathSelected, content, Encoding.UTF8);
            }
        }
    }
}
