using UnityEngine;
using UnityEditor;

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

using UnityEngine.AssetBundles.GraphTool;

namespace AssetBundles.Manager
{
    public class PostprocessCreateAssetMap : IPostprocess {
        public void DoPostprocess (IEnumerable<AssetBundleBuildReport> buildReports, IEnumerable<ExportReport> exportReports) {

            AssetMap newMap = null;

            foreach (var report in buildReports) {
                if (newMap == null) {
                    newMap = new AssetMap ();
                }

                newMap.ManifestFileName = report.ManifestFileName;

                foreach(var b in report.BundleBuild) {
                    var bundleFullName = b.assetBundleVariant == null ? 
                        b.assetBundleName : string.Format ("{0}.{1}", b.assetBundleName, b.assetBundleVariant);

                    foreach (var n in b.assetNames) {
                        var t = AssetDatabase.GetMainAssetTypeAtPath (n);
                        var typeName = (t == null) ? "" : t.FullName;

                        newMap.AddAsset (n, typeName, bundleFullName);
                    }
                }
    		}

            if (newMap != null) {
                Settings.Map = newMap;

                string exportPath = string.Empty;
                foreach (var export in exportReports)
                {
                    if (export.Node.Operation.Object is Exporter)
                    {
                        exportPath = ((Exporter)export.Node.Operation.Object).CurrentPlatformExportPath;
                    }
                    break;
                }
                Settings.GraphToolExportAssetBundlePath = exportPath;
            }
    	}
    }
}
