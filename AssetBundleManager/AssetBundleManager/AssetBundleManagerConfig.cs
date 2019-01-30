using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace AssetBundles.Manager
{
    [Serializable]
    public class AssetBundleManagerConfig
    {
        public const string CONFIG_FILE_NAME = "assetbundle_manager_config_{0}";

        [Serializable]
        public class AssetBundleVersion
        {
            public string assetBundleName;
            public int version;
        }

        public string platform;
        public List<AssetBundleVersion> includedInPlayerAssetBundles = null;
        public AssetBundleManagerMode currentAssetBundleManagerMode;
        public ServerDeployMode currentServerDeployMode;
        public string remoteServerURL;
        public string localServerURL;
        public bool checkVersionAndDownloadExcludedAssetBundles;

        public static AssetBundleManagerConfig FromJson(string jsonContent)
        {
            return JsonUtility.FromJson<AssetBundleManagerConfig>(jsonContent);
        }

        public static string ToJson(AssetBundleManagerConfig toJsonObj)
        {
            return JsonUtility.ToJson(toJsonObj,true);
        }
    }
}
