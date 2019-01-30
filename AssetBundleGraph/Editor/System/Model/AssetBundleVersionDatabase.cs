using System.Collections.Generic;
using UnityEngine.AssetBundles.GraphTool.DataModel.Version2;
using System.IO;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.AssetBundles.GraphTool
{
    public class AssetBundleVersionDatabase : ScriptableObject
    {
        private static AssetBundleVersionDatabase s_DB = null;
        [SerializeField]
        private List<AssetBundlePlatformVersion> m_AssetBundleVersionDatabase = null;

        public static AssetBundleVersionDatabase GetVersionDB()
        {
            if (s_DB == null)
            {
                if (!Load())
                {
                    s_DB = ScriptableObject.CreateInstance<AssetBundleVersionDatabase>();
                    s_DB.m_AssetBundleVersionDatabase = new List<AssetBundlePlatformVersion>();
                    var DBDir = Settings.Path.SettingFilePath;

                    if (!Directory.Exists(DBDir))
                    {
                        Directory.CreateDirectory(DBDir);
                    }

                    AssetDatabase.CreateAsset(s_DB, Settings.Path.VersionFilePath);
                }
            }
            return s_DB;
        }

        private static bool Load()
        {
            bool loaded = false;

#if UNITY_EDITOR
            try
            {
                var dbPath = Settings.Path.VersionFilePath;

                if (File.Exists(dbPath))
                {
                    AssetBundleVersionDatabase m = AssetDatabase.LoadAssetAtPath<AssetBundleVersionDatabase>(dbPath);

                    if (m != null)
                    {
                        s_DB = m;
                        loaded = true;
                    }
                }
            }
            catch (Exception e)
            {
				Debug.LogException(e);
            }
#endif

            return loaded;
        }

        public void Add(string target, string assetBundleName, string assetBundleHashCode, int crc = 0)
        {
            AssetBundlePlatformVersion platformVer = GetAssetBundlePlatformVersion(target);
            if (platformVer == null)
            {
                platformVer = new AssetBundlePlatformVersion(target);
                s_DB.m_AssetBundleVersionDatabase.Add(platformVer);
            }

            AssetBundleVersion abver = platformVer.GetAssetBundleVersion(assetBundleName);

            if (abver == null)
            {
                abver = new AssetBundleVersion(assetBundleName);
                platformVer.AssetBundleVersions.Add(abver);
            }

            platformVer.VersionDirty |= abver.Validate(assetBundleHashCode);
            abver.AssetBundleHashCode = assetBundleHashCode;
            abver.CRC = crc;
        }

        public void Remove(string target, string assetBundleName)
        {
            AssetBundlePlatformVersion platformVer = GetAssetBundlePlatformVersion(target);
            if (platformVer == null)
                return;
        }

        public AssetBundlePlatformVersion GetAssetBundlePlatformVersion(string target)
        {
            AssetBundlePlatformVersion platformVer = null;
            foreach (AssetBundlePlatformVersion i in s_DB.m_AssetBundleVersionDatabase)
            {
                if (i.Target == target)
                {
                    platformVer = i;
                    break;
                }
            }

            return platformVer;
        }

        public void ValidateVersion(string target)
        {
            AssetBundlePlatformVersion platformVer = GetAssetBundlePlatformVersion(target);
            if (platformVer.VersionDirty)
            {
                platformVer.AssetBundleManifestVersion++;
                platformVer.VersionDirty = false;
            }
        }
    }

    [Serializable]
    public class AssetBundlePlatformVersion
    {
        [SerializeField]
        private string m_Target;
        public string Target { get { return this.m_Target; } }

        [SerializeField]
        private int m_AssetBundleManifestVersion = -1;
        public int AssetBundleManifestVersion
        {
            set
            {
                this.m_AssetBundleManifestVersion = value;
            }
            get
            {
                return this.m_AssetBundleManifestVersion;
            }
        }

        [SerializeField]
        private List<AssetBundleVersion> m_AssetBundleVersions;
		public List<AssetBundleVersion> AssetBundleVersions {
			get {
				if (this.m_AssetBundleVersions == null)
					this.m_AssetBundleVersions = new List<AssetBundleVersion> ();
				return this.m_AssetBundleVersions;
			}
		}

        [SerializeField]
        [HideInInspector]
        private bool m_VersionDrity = false;

        public bool VersionDirty
        {
            set
            {
                this.m_VersionDrity = value;
            }
            get
            {
                return this.m_VersionDrity;
            }
        }

        public AssetBundlePlatformVersion(string target)
        {
            this.m_Target = target;
            m_AssetBundleVersions = new List<AssetBundleVersion>();
        }

        public AssetBundleVersion GetAssetBundleVersion(string assetBundleName)
        {
            AssetBundleVersion abver = null;
            for (int i = 0; i < m_AssetBundleVersions.Count; i++)
            {
                AssetBundleVersion ver = m_AssetBundleVersions[i];
                if (ver.AssetBundleName.Equals(assetBundleName))
                {
                    abver = ver;
                    break;
                }
            }

            return abver;
        }
    }

    [Serializable]
    public class AssetBundleVersion
    {
        [SerializeField]
        private string m_AssetBundleName;
        [SerializeField]
        private string m_AssetBundleHashCode = string.Empty;
        [SerializeField]
        private int m_AssetBundleVersion = -1;

        [SerializeField]
        [HideInInspector]
        private bool m_Dirty = false;
        public bool Dirty { set; get; }

        public AssetBundleVersion(string assetBundleName)
        {
            this.m_AssetBundleName = assetBundleName;
        }

        public bool Validate(string newAssetBundleHashCode)
        {
            bool ret = false;
            if (!m_AssetBundleHashCode.Equals(newAssetBundleHashCode))
            {
                m_AssetBundleVersion++;
                ret = true;
            }
            else
                ret = false;

            return ret;
        }

        public string AssetBundleName
        {
            get
            {
                return this.m_AssetBundleName;
            }
        }

        public string AssetBundleHashCode
        {
            set
            {
                this.m_AssetBundleHashCode = value;
            }
            get
            {
                return this.m_AssetBundleHashCode;
            }
        }

        public int CRC { set; get; }

        public int Version
        {
            get
            {
                return this.m_AssetBundleVersion;
            }
        }
    }
}