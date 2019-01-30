using UnityEngine;
using UnityEditor;

using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

#if UNITY_5_5_OR_NEWER
using UnityEngine.Profiling;
#endif

using UnityEngine.AssetBundles.GraphTool;
using Model = UnityEngine.AssetBundles.GraphTool.DataModel.Version2;

/**
    ImportSetting is the class for apply specific setting to already imported files.
*/
[CustomNode("Configure Bundle/Extract Shared Assets", 71)]
public class ExtractSharedAssets : Node
{
    enum GroupingType : int
    {
        ByFileSize,
        ByRuntimeMemorySize
    };

    enum GroupingSizeUnit : int
    {
        KB,
        MB
    };

    [SerializeField]
    private string m_bundleNameTemplate;
    [SerializeField]
    private SerializableMultiTargetInt m_groupExtractedAssets;
    [SerializeField]
    private SerializableMultiTargetInt m_groupSizeByte;
    [SerializeField]
    private SerializableMultiTargetInt m_groupingType;
    [SerializeField]
    private SerializableMultiTargetInt m_groupingSizeUnit;

    public override string ActiveStyle
    {
        get
        {
            return "node 3 on";
        }
    }

    public override string InactiveStyle
    {
        get
        {
            return "node 3";
        }
    }

    public override string Category
    {
        get
        {
            return "Configure";
        }
    }

    public override Model.NodeOutputSemantics NodeInputType
    {
        get
        {
            return Model.NodeOutputSemantics.AssetBundleConfigurations;
        }
    }

    public override Model.NodeOutputSemantics NodeOutputType
    {
        get
        {
            return Model.NodeOutputSemantics.AssetBundleConfigurations;
        }
    }

    public override void Initialize(Model.NodeData data)
    {
        m_bundleNameTemplate = "shared_*";
        m_groupExtractedAssets = new SerializableMultiTargetInt();
        m_groupSizeByte = new SerializableMultiTargetInt();
        m_groupingType = new SerializableMultiTargetInt();
        m_groupingSizeUnit = new SerializableMultiTargetInt();
        data.AddDefaultInputPoint();
        data.AddDefaultOutputPoint();
    }

    public override Node Clone(Model.NodeData newData)
    {
        var newNode = new ExtractSharedAssets();
        newNode.m_groupExtractedAssets = new SerializableMultiTargetInt(m_groupExtractedAssets);
        newNode.m_groupSizeByte = new SerializableMultiTargetInt(m_groupSizeByte);
        newNode.m_groupingType = new SerializableMultiTargetInt(m_groupingType);
        newNode.m_groupingSizeUnit = new SerializableMultiTargetInt(m_groupingSizeUnit);
        newNode.m_bundleNameTemplate = m_bundleNameTemplate;
        newData.AddDefaultInputPoint();
        newData.AddDefaultOutputPoint();
        return newNode;
    }

    public override void OnInspectorGUI(NodeGUI node, AssetReferenceStreamManager streamManager, NodeGUIEditor editor, Action onValueChanged)
    {

        EditorGUILayout.HelpBox("Extract Shared Assets: Extract shared assets between asset bundles and add bundle configurations.", MessageType.Info);
        editor.UpdateNodeName(node);

        GUILayout.Space(10f);

        var newValue = EditorGUILayout.TextField("Bundle Name Template", m_bundleNameTemplate);
        if (newValue != m_bundleNameTemplate)
        {
            using (new RecordUndoScope("Bundle Name Template Change", node, true))
            {
                m_bundleNameTemplate = newValue;
                onValueChanged();
            }
        }

        GUILayout.Space(10f);

        GUILayout.Space(10f);

        //Show target configuration tab
        editor.DrawPlatformSelector(node);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            var disabledScope = editor.DrawOverrideTargetToggle(node, m_groupSizeByte.ContainsValueOf(editor.CurrentEditingGroup), (bool enabled) =>
            {
                using (new RecordUndoScope("Remove Target Grouping Size Settings", node, true))
                {
                    if (enabled)
                    {
                        m_groupExtractedAssets[editor.CurrentEditingGroup] = m_groupExtractedAssets.DefaultValue;
                        m_groupSizeByte[editor.CurrentEditingGroup] = m_groupSizeByte.DefaultValue;
                        m_groupingType[editor.CurrentEditingGroup] = m_groupingType.DefaultValue;
                        m_groupingSizeUnit[editor.CurrentEditingGroup] = m_groupingSizeUnit.DefaultValue;
                    }
                    else
                    {
                        m_groupExtractedAssets.Remove(editor.CurrentEditingGroup);
                        m_groupSizeByte.Remove(editor.CurrentEditingGroup);
                        m_groupingType.Remove(editor.CurrentEditingGroup);
                        m_groupingSizeUnit.Remove(editor.CurrentEditingGroup);
                    }
                    onValueChanged();
                }
            });

            using (disabledScope)
            {
                var useGroup = EditorGUILayout.ToggleLeft("Group shared assets by size", m_groupExtractedAssets[editor.CurrentEditingGroup] != 0);
                if (useGroup != (m_groupExtractedAssets[editor.CurrentEditingGroup] != 0))
                {
                    using (new RecordUndoScope("Change Grouping Type", node, true))
                    {
                        m_groupExtractedAssets[editor.CurrentEditingGroup] = (useGroup) ? 1 : 0;
                        onValueChanged();
                    }
                }

                using (new EditorGUI.DisabledScope(!useGroup))
                {
                    var newType = (GroupingType)EditorGUILayout.EnumPopup("Grouping Type", (GroupingType)m_groupingType[editor.CurrentEditingGroup]);
                    if (newType != (GroupingType)m_groupingType[editor.CurrentEditingGroup])
                    {
                        using (new RecordUndoScope("Change Grouping Type", node, true))
                        {
                            m_groupingType[editor.CurrentEditingGroup] = (int)newType;
                            onValueChanged();
                        }
                    }

                    var newSizeUnit = (GroupingSizeUnit)EditorGUILayout.EnumPopup("Grouping Size Unit", (GroupingSizeUnit)m_groupingSizeUnit[editor.CurrentEditingGroup]);
                    if (newSizeUnit != (GroupingSizeUnit)m_groupingSizeUnit[editor.CurrentEditingGroup])
                    {
                        using (new RecordUndoScope("Change Grouping Size Unit", node, true))
                        {
                            m_groupingSizeUnit[editor.CurrentEditingGroup] = (int)newSizeUnit;
                            onValueChanged();
                        }
                    }

                    var newSizeText = EditorGUILayout.TextField("Size", m_groupSizeByte[editor.CurrentEditingGroup].ToString());
                    int newSize = 0;

                    if (!Int32.TryParse(newSizeText, out newSize))
                    {
                        throw new NodeException("Invalid size. Size property must be in decimal format.", node.Id);
                    }
                    if (newSize < 0)
                    {
                        throw new NodeException("Invalid size. Size property must be a positive number.", node.Id);
                    }

                    if (newSize != m_groupSizeByte[editor.CurrentEditingGroup])
                    {
                        using (new RecordUndoScope("Change Grouping Size", node, true))
                        {
                            m_groupSizeByte[editor.CurrentEditingGroup] = newSize;
                            onValueChanged();
                        }
                    }
                }
            }
        }

        EditorGUILayout.HelpBox("Bundle Name Template replaces \'*\' with number.", MessageType.Info);
    }

    /**
	 * Prepare is called whenever graph needs update. 
	 */
    public override void Prepare(BuildTarget target,
                                    Model.NodeData node,
                                    IEnumerable<PerformGraph.AssetGroups> incoming,
                                    IEnumerable<Model.ConnectionData> connectionsToOutput,
                                    PerformGraph.Output Output)
    {
        if (string.IsNullOrEmpty(m_bundleNameTemplate))
        {
            throw new NodeException(node.Name + ":Bundle Name Template is empty.", node.Id);
        }

        // Pass incoming assets straight to Output
        if (Output != null)
        {
            var destination = (connectionsToOutput == null || !connectionsToOutput.Any()) ?
                null : connectionsToOutput.First();

            if (incoming != null)
            {

                var buildMap = AssetBundleBuildMap.GetBuildMap();
                buildMap.ClearFromId(node.Id);

                var dependencyCollector = new Dictionary<string, List<string>>(); // [asset path:group name]
                var sharedDependency = new Dictionary<string, List<AssetReference>>();
                var groupNameMap = new Dictionary<string, string>();

                // build dependency map
                foreach (var ag in incoming)
                {
                    foreach (var key in ag.assetGroups.Keys)
                    {
                        var assets = ag.assetGroups[key];

                        foreach (var a in assets)
                        {
                            CollectDependencies(key, new string[] { a.importFrom }, ref dependencyCollector);
                        }
                    }
                }

                foreach (var entry in dependencyCollector)
                {
                    // �����õ�shaderȫ�������һ��assetbundle��
                    if (entry.Value != null && (entry.Value.Count > 1 || Path.GetExtension(entry.Key).Equals(".shader", StringComparison.CurrentCultureIgnoreCase)))
                    {
                        string[] coms = AssetDatabase.GetMainAssetTypeAtPath(entry.Key).ToString().Split('.');
                        var groupName = m_bundleNameTemplate.Replace("*", "dependency") + "_" + coms[coms.Length - 1].ToLower() /*+ "_" + AssetDatabase.AssetPathToGUID(entry.Key)*/;
                        if (!sharedDependency.ContainsKey(groupName))
                        {
                            sharedDependency[groupName] = new List<AssetReference>();
                        }
                        List<AssetReference> sharedAssetRefList = null;
                        sharedDependency.TryGetValue(groupName, out sharedAssetRefList);
                        sharedAssetRefList.Add(AssetReference.CreateReference(entry.Key));
                    }
                }

                if (sharedDependency.Keys.Count > 0)
                {
                    // group shared dependency bundles by size.
                    if (m_groupExtractedAssets[target] != 0)
                    {
                        List<string> devidingBundleNames = new List<string>(sharedDependency.Keys);
                        long szGroup = 0;
                        if ((GroupingSizeUnit)m_groupingSizeUnit[target] == GroupingSizeUnit.KB)
                            szGroup = m_groupSizeByte[target] * 1024;
                        else if ((GroupingSizeUnit)m_groupingSizeUnit[target] == GroupingSizeUnit.MB)
                            szGroup = m_groupSizeByte[target] * 1024 * 1024;

                        foreach (var bundleName in devidingBundleNames)
                        {
                            var assets = sharedDependency[bundleName];
                            int groupCount = 0;
                            long szGroupCount = 0;
                            foreach (var a in assets)
                            {
                                var subGroupName = string.Format("{0}_{1}", bundleName, groupCount);
                                if (!sharedDependency.ContainsKey(subGroupName))
                                {
                                    sharedDependency[subGroupName] = new List<AssetReference>();
                                }
                                sharedDependency[subGroupName].Add(a);


                                szGroupCount += GetSizeOfAsset(a, (GroupingType)m_groupingType[target]);
                                if (szGroupCount >= szGroup)
                                {
                                    szGroupCount = 0;
                                    ++groupCount;
                                }
                            }
                            sharedDependency.Remove(bundleName);
                        }
                    }

                    foreach (var bundleName in sharedDependency.Keys)
                    {
                        var bundleConfig = buildMap.GetAssetBundleWithNameAndVariant(node.Id, bundleName, string.Empty);
                        bundleConfig.AddAssets(node.Id, sharedDependency[bundleName].Select(a => a.importFrom));
                    }

                    foreach (var ag in incoming)
                        Output(destination, new Dictionary<string, List<AssetReference>>(ag.assetGroups));

                    // Remove asset from sharedDependency assetbundle which have been marked as assetbundle.
                    List<AssetReference> allAssetGroups = new List<AssetReference>();
                    foreach (var ag in incoming)
                    {
                        foreach (KeyValuePair<string, List<AssetReference>> kp in ag.assetGroups)
                        {
                            allAssetGroups.AddRange(kp.Value);
                        }
                    }
                    Dictionary<string, List<AssetReference>> removedSharedDependencyDic = new Dictionary<string, List<AssetReference>>();
                    foreach (KeyValuePair<string, List<AssetReference>> kp in sharedDependency)
                    {
                        foreach (AssetReference ar in kp.Value)
                        {
                            foreach (AssetReference ar1 in allAssetGroups)
                            {
                                if (ar.importFrom.Equals(ar1.importFrom))
                                {
                                    List<AssetReference> arList = null;
                                    removedSharedDependencyDic.TryGetValue(kp.Key, out arList);
                                    if (arList == null)
                                    {
                                        arList = new List<AssetReference>();
                                        arList.Add(ar);
                                        removedSharedDependencyDic.Add(kp.Key, arList);
                                    }
                                    else
                                    {
                                        arList.Add(ar);
                                    }
                                    continue;
                                }
                            }
                        }
                    }
                    // 
                    List<string> removedkeys = new List<string>();
                    foreach (KeyValuePair<string, List<AssetReference>> kp in removedSharedDependencyDic)
                    {
                        List<AssetReference> arList = null;
                        sharedDependency.TryGetValue(kp.Key, out arList);
                        if (arList != null)
                        {
                            foreach (AssetReference ar in kp.Value)
                            {
                                arList.Remove(ar);
                                if (arList.Count == 0)
                                    removedkeys.Add(kp.Key);
                            }
                        }
                    }
                    // Remove list count == 0 item.
                    foreach (string key in removedkeys)
                        sharedDependency.Remove(key);
                    removedkeys.Clear();
                    removedkeys = null;
                    // 
                    Output(destination, sharedDependency);
                }
                else
                {
                    foreach (var ag in incoming)
                        Output(destination, ag.assetGroups);
                }
            }
            else
            {
                // Overwrite output with empty Dictionary when no there is incoming asset
                Output(destination, new Dictionary<string, List<AssetReference>>());
            }
        }
    }

    private void CollectDependencies(string groupKey, string[] assetPaths, ref Dictionary<string, List<string>> collector)
    {
        var dependencies = AssetDatabase.GetDependencies(assetPaths);
        foreach (var d in dependencies)
        {
            // AssetBundle must not include script asset
            if (TypeUtility.GetTypeOfAsset(d) == typeof(MonoScript))
            {
                continue;
            }

            if (!collector.ContainsKey(d))
            {
                collector[d] = new List<string>();
            }
            if (!collector[d].Contains(groupKey))
            {
                collector[d].Add(groupKey);
                collector[d].Sort();
            }
        }
    }

    private long GetSizeOfAsset(AssetReference a, GroupingType t)
    {

        long size = 0;

        // You can not read scene and do estimate
        if (TypeUtility.GetTypeOfAsset(a.importFrom) == typeof(UnityEditor.SceneAsset))
        {
            t = GroupingType.ByFileSize;
        }

        if (t == GroupingType.ByRuntimeMemorySize)
        {
            var objects = a.allData;
            foreach (var o in objects)
            {
#if UNITY_5_6_OR_NEWER
                size += Profiler.GetRuntimeMemorySizeLong (o);
#else
                size += Profiler.GetRuntimeMemorySize(o);
#endif
            }

            a.ReleaseData();
        }
        else if (t == GroupingType.ByFileSize)
        {
            System.IO.FileInfo fileInfo = new System.IO.FileInfo(a.absolutePath);
            if (fileInfo.Exists)
            {
                size = fileInfo.Length;
            }
        }

        return size;
    }
}
