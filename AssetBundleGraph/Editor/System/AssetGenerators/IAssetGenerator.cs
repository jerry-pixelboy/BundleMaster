using UnityEngine;
using UnityEditor;

using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Reflection;

using Model=UnityEngine.AssetBundles.GraphTool.DataModel.Version2;

namespace UnityEngine.AssetBundles.GraphTool {

	/**
	 * IAssetGenerator is an interface to generate new asset from incoming asset.
	 * Subclass of IAssetGenerator must have CustomAssetGenerator attribute.
	 */
	public interface IAssetGenerator {
        /**
         * File extension of generated asset.
         */ 
        string Extension {
            get;
        }

        /**
         * Asset Type of generated asset.
         */ 
        Type AssetType {
            get;
        }

        /**
		 * Test if asset can be generated from given asset.
         * @param [out] message Additional message when generator can not generate asset.
		 */
        bool CanGenerateAsset (AssetReference asset, out string message);

		/**
		 * Generate asset.
         * @param [in] asset Source asset to generate derivertive asset.
         * @param [in] generateAssetPath Path to save generated derivertive asset.
		 */ 
        bool GenerateAsset (AssetReference asset, string generateAssetPath);

		/**
		 * Draw Inspector GUI for this AssetGenerator.
		 */ 
		void OnInspectorGUI (Action onValueChanged);
	}

	[AttributeUsage(AttributeTargets.Class)] 
	public class CustomAssetGenerator : Attribute {
		private string m_name;
		private string m_version;

		private const int kDEFAULT_ASSET_THRES = 10;

		public string Name {
			get {
				return m_name;
			}
		}

		public string Version {
			get {
				return m_version;
			}
		}

        public CustomAssetGenerator (string name) {
			m_name = name;
			m_version = string.Empty;
		}

        public CustomAssetGenerator (string name, string version) {
			m_name = name;
			m_version = version;
		}

        public CustomAssetGenerator (string name, string version, int itemThreashold) {
			m_name = name;
			m_version = version;
		}
	}

	public class AssetGeneratorUtility {

        private static  Dictionary<string, string> s_attributeAssemblyQualifiedNameMap;

		public static Dictionary<string, string> GetAttributeAssemblyQualifiedNameMap () {

			if(s_attributeAssemblyQualifiedNameMap == null) {
				// attribute name or class name : class name
				s_attributeAssemblyQualifiedNameMap = new Dictionary<string, string>(); 

                var allBuilders = new List<Type>();

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    var builders = assembly.GetTypes()
                        .Where(t => !t.IsInterface)
                        .Where(t => typeof(IAssetGenerator).IsAssignableFrom(t));
                    allBuilders.AddRange (builders);
                }

                foreach (var type in allBuilders) {
					// set attribute-name as key of dict if atribute is exist.
                    CustomAssetGenerator attr = 
                        type.GetCustomAttributes(typeof(CustomAssetGenerator), true).FirstOrDefault() as CustomAssetGenerator;

                    var typename = type.AssemblyQualifiedName;


					if (attr != null) {
						if (!s_attributeAssemblyQualifiedNameMap.ContainsKey(attr.Name)) {
							s_attributeAssemblyQualifiedNameMap[attr.Name] = typename;
						}
					} else {
						s_attributeAssemblyQualifiedNameMap[typename] = typename;
					}
				}
			}
			return s_attributeAssemblyQualifiedNameMap;
		}

        public static string GetGUIName(IAssetGenerator generator) {
            CustomAssetGenerator attr = 
                generator.GetType().GetCustomAttributes(typeof(CustomAssetGenerator), false).FirstOrDefault() as CustomAssetGenerator;
			return attr.Name;
		}

		public static bool HasValidAttribute(Type t) {
            CustomAssetGenerator attr = 
                t.GetCustomAttributes(typeof(CustomAssetGenerator), false).FirstOrDefault() as CustomAssetGenerator;
			return attr != null && !string.IsNullOrEmpty(attr.Name);
		}

		public static string GetGUIName(string className) {
			if(className != null) {
				var type = Type.GetType(className);
				if(type != null) {
                    CustomAssetGenerator attr = 
                        type.GetCustomAttributes(typeof(CustomAssetGenerator), false).FirstOrDefault() as CustomAssetGenerator;
					if(attr != null) {
						return attr.Name;
					}
				}
			}
			return string.Empty;
		}

		public static string GetVersion(string className) {
			var type = Type.GetType(className);
			if(type != null) {
                CustomAssetGenerator attr = 
                    type.GetCustomAttributes(typeof(CustomAssetGenerator), false).FirstOrDefault() as CustomAssetGenerator;
				if(attr != null) {
					return attr.Version;
				}
			}
			return string.Empty;
		}

		public static string GUINameToAssemblyQualifiedName(string guiName) {
			var map = GetAttributeAssemblyQualifiedNameMap();

			if(map.ContainsKey(guiName)) {
				return map[guiName];
			}

			return null;
		}

        public static IAssetGenerator CreateGenerator(string guiName) {
			var className = GUINameToAssemblyQualifiedName(guiName);
			if(className != null) {
                var type = Type.GetType(className);
                if (type == null) {
                    return null;
                }
                return (IAssetGenerator) type.Assembly.CreateInstance(type.FullName);
			}
			return null;
		}

        public static IAssetGenerator CreateByAssemblyQualifiedName(string assemblyQualifiedName) {

			if(assemblyQualifiedName == null) {
				return null;
			}

			Type t = Type.GetType(assemblyQualifiedName);
			if(t == null) {
				return null;
			}

			if(!HasValidAttribute(t)) {
				return null;
			}

            return (IAssetGenerator) t.Assembly.CreateInstance(t.FullName);
		}
	}
}