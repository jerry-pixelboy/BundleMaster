
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

using V1 = AssetBundleGraph;
using Model = UnityEngine.AssetBundles.GraphTool.DataModel.Version2;

namespace UnityEngine.AssetBundles.GraphTool
{
    [CustomNode("Group Assets/Group By File", 42)]
    public class GroupingByFile : Node
    {
        // Use asset file name as group key.
        [SerializeField] private bool m_useAssetFileNameAsGroupKey;

        public override string ActiveStyle
        {
            get
            {
                return "node 2 on";
            }
        }

        public override string InactiveStyle
        {
            get
            {
                return "node 2";
            }
        }

        public override string Category
        {
            get
            {
                return "Group";
            }
        }

        public override void Initialize(Model.NodeData data)
        {
            data.AddDefaultInputPoint();
            data.AddDefaultOutputPoint();
        }

        public override Node Clone(Model.NodeData newData)
        {
            var newNode = new GroupingByFile();

            newData.AddDefaultInputPoint();
            newData.AddDefaultOutputPoint();
            return newNode;
        }

        public override void OnInspectorGUI(NodeGUI node, AssetReferenceStreamManager streamManager, NodeGUIEditor editor, Action onValueChanged)
        {
            EditorGUILayout.HelpBox("Group By File: Create group per individual asset.", MessageType.Info);
            editor.UpdateNodeName(node);

            GUILayout.Space(10f);

            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                GUILayout.Label("Settings:");
                var newUseAssetFileNameAsGroupKey = GUILayout.Toggle(m_useAssetFileNameAsGroupKey, " Use asset file name as group key");
                if (m_useAssetFileNameAsGroupKey != newUseAssetFileNameAsGroupKey)
                {
                    using (new RecordUndoScope("Change Use Asset File Name As Group Key", node, true))
                    {
                        m_useAssetFileNameAsGroupKey = newUseAssetFileNameAsGroupKey;
                        onValueChanged();
                    }
                }
            }
        }

        public override void Prepare(BuildTarget target,
            Model.NodeData node,
            IEnumerable<PerformGraph.AssetGroups> incoming,
            IEnumerable<Model.ConnectionData> connectionsToOutput,
            PerformGraph.Output Output)
        {
            if (connectionsToOutput == null || Output == null)
            {
                return;
            }

            var outputDict = new Dictionary<string, List<AssetReference>>();

            string execptionInfoStr = string.Empty;

            if (incoming != null)
            {
                int i = 0;
                foreach (var ag in incoming)
                {
                    foreach (var assets in ag.assetGroups.Values)
                    {
                        foreach (var a in assets)
                        {
                            var targetPath = a.path;

                            var key = "";

                            if (m_useAssetFileNameAsGroupKey)
                                key = a.fileName;
                            else
                                key = i.ToString();

                            if (outputDict.ContainsKey(key))
                                execptionInfoStr += (a.fileNameAndExtension + "\n");

                            outputDict[key] = new List<AssetReference>();
                            outputDict[key].Add(a);
                            ++i;
                        }
                    }
                }
            }

            AssetBundleGraphEditorWindow window = AssetBundleGraphEditorWindow.Window;
            NodeGUI nodeGui = null;
            if (execptionInfoStr.Length > 0)
            {
                if (window != null)
                    throw new NodeException(string.Format("Multiple files have the same file name.\n{0}", execptionInfoStr), node.Id);
                else
                    Debug.LogError(string.Format("Multiple files have the same file name.\n{0}", execptionInfoStr.TrimEnd("\n".ToCharArray())));
            }
            else
            {
                nodeGui = null;
                if (window != null)
                {
                    nodeGui = window.GetNodeGUI(node.Id);
                }
                if (nodeGui != null)
                    nodeGui.ResetErrorStatus();
            }

            var dst = (connectionsToOutput == null || !connectionsToOutput.Any()) ?
                null : connectionsToOutput.First();
            Output(dst, outputDict);
        }
    }
}