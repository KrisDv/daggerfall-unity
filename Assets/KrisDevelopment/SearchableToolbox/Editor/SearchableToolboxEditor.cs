// Software by Kris Development

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using SETUtil;
using SETUtil.Types;
using UnityEditor;
using UnityEngine;

namespace SearchableToolbox
{
	public class SearchableToolboxEditor : EditorWindow
	{
		[Serializable]
		private class Tool
		{
			public string displayName;

			[SerializeField] private SerializableSystemType MethodDeclaringSystemType;
			[SerializeField] private string methodInfoName;
			[SerializeField] internal int methodParamsHash;
			private MethodInfo methodInfo;

			public MethodInfo MethodInfo
			{
				get
				{
					if (methodInfo == null) {
						if (MethodDeclaringSystemType == null || MethodDeclaringSystemType.value == null) {
							return null;
						}

						methodInfo = MethodDeclaringSystemType.value.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).FirstOrDefault(a => GetParamsListHash(a.GetParameters()) == methodParamsHash && a.Name == methodInfoName);
					}

					return methodInfo;
				}
				set
				{
					methodInfo = value;
					methodInfoName = value.Name;
					methodParamsHash = GetParamsListHash(value.GetParameters());
					MethodDeclaringSystemType = new SerializableSystemType(value.DeclaringType);
				}
			}

			private SerializableSystemType _validationDeclaringSystemType;
			[SerializeField] private string validationName;
			[SerializeField] private int validateParamsHash;
			private MethodInfo validation;

			public MethodInfo Validation
			{
				get
				{
					if (validation == null && _validationDeclaringSystemType != null && _validationDeclaringSystemType.value != null) {
						validation = _validationDeclaringSystemType.value.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).FirstOrDefault(a => GetParamsListHash(a.GetParameters()) == validateParamsHash && a.Name == validationName);
					}

					return validation;
				}
				set
				{
					validation = value;
					validationName = value.Name;
					validateParamsHash = GetParamsListHash(value.GetParameters());
					_validationDeclaringSystemType = new SerializableSystemType(value.DeclaringType);
				}
			}

			public string menuItem;
			public string fingerprint;


			public bool Contains(string keyWord)
			{
				var searchString = String.Format("{0}*{1}*{2}", displayName, menuItem, (MethodDeclaringSystemType != null) ? MethodDeclaringSystemType.Name : string.Empty);

				foreach (var word in keyWord.Split()) {
					if (searchString.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) {
						return false;
					}
				}

				return true;
			}

			public void GenerateFingerprint()
			{
				fingerprint = String.Format("{0}{1}.{2}", menuItem, MethodInfo.DeclaringType, MethodInfo.Name);
			}

			private int GetParamsListHash(ParameterInfo[] prs)
			{
				int hash = 0;
				foreach (var p in prs) {
					hash += p.ParameterType.GetHashCode();
				}

				return hash;
			}
		}

		[Serializable]
		private class ToolNode
		{
			public readonly ToolNode parent;
			public readonly List<int> content;

			public List<ToolNode> peers { get { return parent != null ? parent.children : null; } }
			public List<ToolNode> children = new List<ToolNode>();
			public int depth;

			public ToolNode(ToolNode parent, List<int> content)
			{
				this.parent = parent;
				if (this.parent != null) {
					this.parent.children.Add(this);
				}

				depth = this.parent != null ? this.parent.depth + 1 : 0;
				this.content = content;
			}

			public string Name;
			public bool Expand;
		}

		private class ValidationMethodPiece
		{
			public string menuItem;
			public MethodInfo method;
		}

		[Serializable]
		private class FavoriteTools
		{
			public List<Tool> Content = new List<Tool>();
			public bool ExpandView = false;
		}

		private class CollectToolsTaskParamsHandle
		{
			public bool requestCancelTask = false;
			public float progress = 0f;
			public bool requestRepaint = false;
			public bool working = false;
			public ToolboxPrefs toolboxPrefs;
			public List<Tool> tools;
			public ToolNode toolNodeRoot;
			public Action onTaskDone;
		}

		[Serializable]
		private class ToolboxPrefs
		{
			public bool collectMethodsWithParams = false;
			public bool dockable = true;
			public FavoriteTools favoriteTools = new FavoriteTools();
		}

		private class SearchableToolboxConfigWizard : EditorWindow
		{
			private ToolboxPrefs pfs;
			private Action onClose;

			public static void Open(ToolboxPrefs pfs, Action onClose)
			{
				var window = SETUtil.EditorUtil.ShowUtilityWindow<SearchableToolboxConfigWizard>("Toolbox Prefs");
				window.pfs = pfs;
				window.onClose = onClose;
			}

			private void OnGUI()
			{
				pfs.collectMethodsWithParams = EditorGUILayout.ToggleLeft("Collect Methods With Params", pfs.collectMethodsWithParams);
				EditorGUILayout.HelpBox(
					"When enabled 'Collect Methods With Params' will collect tools that require parameters to be run. Such tools will have '[P]' next to their name.",
					MessageType.Info);

				pfs.dockable = EditorGUILayout.ToggleLeft("Dockable Window", pfs.dockable);
				EditorGUILayout.HelpBox(
					"When not dockable, the window will close when a tool is selected.",
					MessageType.Info);

				if (GUILayout.Button("Save & Close")) {
					onClose.Invoke();
					Close();
				}
			}
		}


		private Vector2 scrollView = Vector2.zero;
		private string search = string.Empty;
		[NonSerialized] private bool showAllResults = false;

		[NonSerialized] private List<Tool> tools = new List<Tool>();
		[NonSerialized] private ToolNode m_toolNodeRoot;

		private ToolNode toolNodeRoot
		{
			get
			{
				return m_toolNodeRoot ?? (m_toolNodeRoot = new ToolNode(null, new List<int>()) {
					Name = "All Tools",
					Expand = true,
				});
			}
		}

		private static string prefsKey { get { return String.Format("{0}_TbxPfs", (Application.companyName + Application.productName).GetHashCode()); } }

		private static ToolboxPrefs m_toolboxPrefs;
		private static ToolboxPrefs toolboxPrefs { get { return m_toolboxPrefs ?? (m_toolboxPrefs = loadPrefs.Invoke()); } }

		private static Func<ToolboxPrefs> loadPrefs = () => {
			var key = prefsKey;
			if (EditorPrefs.HasKey(key)) {
				return JsonUtility.FromJson<ToolboxPrefs>(EditorPrefs.GetString(key));
			}

			return new ToolboxPrefs();
		};

		private FavoriteTools favoriteTools { get { return toolboxPrefs.favoriteTools; } }

		[NonSerialized] private CollectToolsTaskParamsHandle collectJobHandle = new CollectToolsTaskParamsHandle();
		private Rect backupAreaRect = new Rect();

		[NonSerialized] private GUIStyle detailsTextStyle;

		[NonSerialized] // Will trigger a refresh when assembly reloads
		private static bool hasReloadedTools = false;

		private static bool requestFocusSearchField;

		private static GUIStyle m_toolStyle;

		private static GUIStyle toolStyle
		{
			get
			{
				if (m_toolStyle == null) {
					m_toolStyle = new GUIStyle("Window");
					m_toolStyle.alignment = TextAnchor.UpperLeft;
					m_toolStyle.fontStyle = FontStyle.Bold;
					m_toolStyle.richText = true;
					m_toolStyle.stretchHeight = false;
				}

				return m_toolStyle;
			}
		}


		[MenuItem("Window/Kris Development/Searchable Toolbox &#m")]
		public static void ShowWindow()
		{
			var window = toolboxPrefs.dockable ? GetWindow<SearchableToolboxEditor>("Toolbox") : SETUtil.EditorUtil.ShowUtilityWindow<SearchableToolboxEditor>("Toolbox");
			window.RunCollectToolsTask();
			requestFocusSearchField = true;
		}


		private void RunCollectToolsTask()
		{
			// cancel if any old one is still running
			CancelCollectToolsTask();

			collectJobHandle.progress = 0;
			collectJobHandle.working = true;
			collectJobHandle.requestCancelTask = false;
			collectJobHandle.toolNodeRoot = new ToolNode(null, new List<int>()) {
				Name = "All Tools",
				Expand = true,
			};
			collectJobHandle.tools = new List<Tool>();
			collectJobHandle.toolboxPrefs = toolboxPrefs;

			collectJobHandle.onTaskDone = () => {
				tools = collectJobHandle.tools;
				m_toolNodeRoot = collectJobHandle.toolNodeRoot;
				AssemblyReloadEvents.beforeAssemblyReload -= CancelCollectToolsTask;
			};

			EditorApplication.update += RepaintHandler;
			AssemblyReloadEvents.beforeAssemblyReload += CancelCollectToolsTask;
			ThreadPool.QueueUserWorkItem(CollectToolsTask, collectJobHandle);
		}

		private void CancelCollectToolsTask()
		{
			if (!collectJobHandle.working) {
				return;
			}

			collectJobHandle.requestCancelTask = true;
			AssemblyReloadEvents.beforeAssemblyReload -= CancelCollectToolsTask;
			while (collectJobHandle.working) {/* main thread wait */}

			hasReloadedTools = false;
		}


		private void RepaintHandler()
		{
			if (collectJobHandle.requestRepaint) {
				Repaint();
				collectJobHandle.requestRepaint = false;
			}

			if (collectJobHandle.working == false) {
				EditorApplication.update -= RepaintHandler;
			}
		}

		private void OnGUIInit()
		{
			if (!hasReloadedTools || (tools.Count == 0 && !collectJobHandle.working)) {
				RunCollectToolsTask();
				hasReloadedTools = true;
			}

			if (detailsTextStyle == null) {
				detailsTextStyle = new GUIStyle(EditorStyles.miniLabel) {wordWrap = true};
			}
		}

		private void OnGUI()
		{
			OnGUIInit();

			GUILayout.BeginHorizontal(EditorStyles.toolbar);
			{
				GUILayout.Label(String.Format("Search {0} Tools:", tools.Count), EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
				GUI.SetNextControlName("STESearchField");
				var _searchVal = EditorGUILayout.TextField(search, EditorStyles.toolbarTextField, GUILayout.ExpandWidth(true));

				if (_searchVal != search) {
					search = _searchVal;
					showAllResults = false;
				}

				if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) {
					RunCollectToolsTask();
				}

				if (GUILayout.Button("Config", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) {
					SearchableToolboxConfigWizard.Open(toolboxPrefs, () => {
						SaveToolsPrefs();
						RunCollectToolsTask();
					});
				}

				if (GUILayout.Button(EditorGUIUtility.IconContent("BuildSettings.Web.Small"), EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
				{
					Application.OpenURL("https://krisdevelopment.wordpress.com");
				}
			}
			GUILayout.EndHorizontal();

			if (collectJobHandle.working) {
				GUILayout.BeginVertical(GUILayout.Height(35));
				var rect = EditorUtil.FindLayoutAreaRect(ref backupAreaRect, 2);
				EditorGUI.ProgressBar(rect, collectJobHandle.progress, "Loading Tools..");
				GUILayout.EndVertical();

				return;
			}

			scrollView = GUILayout.BeginScrollView(scrollView);
			{
				if (!string.IsNullOrEmpty(search)) {
					GUILayout.BeginHorizontal();
					{
						GUILayout.Space(8);
						GUILayout.BeginVertical();
						{
							int _count = 0;
							foreach (var tool in tools) {
								if (!tool.Contains(search)) {
									continue;
								}

								if (!showAllResults && _count++ > 24) {
									if (GUILayout.Button("... Show All ...")) {
										showAllResults = true;
									}

									break;
								}

								DrawTool(tool);
							}
						}
						GUILayout.EndVertical();
					}
					GUILayout.EndHorizontal();
				} else {
					if (favoriteTools.Content.Count > 0) {
						var _exVal = EditorUtil.ExpandButton(favoriteTools.ExpandView, "Pinned Items");

						if (_exVal != favoriteTools.ExpandView) {
							favoriteTools.ExpandView = _exVal;
							SaveToolsPrefs();
						}

						if (favoriteTools.ExpandView) {
							GUILayout.BeginHorizontal();
							{
								EditorUtil.VerticalRule();
								GUILayout.Space(6);

								GUILayout.BeginVertical();
								{
									foreach (var tool in favoriteTools.Content) {
										DrawTool(tool);
									}
								}
								GUILayout.EndVertical();
							}
							GUILayout.EndHorizontal();

							SETUtil.EditorUtil.HorizontalRule();
						}
					}

					DrawRecursiveTree(toolNodeRoot, true);
				}
				
				GUILayout.Space(16);
			}
			GUILayout.EndScrollView();
			
			// focus control
			if (requestFocusSearchField) {
				EditorGUI.FocusTextInControl("STESearchField");
				Focus();
				requestFocusSearchField = false;
			}
		}

		private void DrawTool(Tool tool)
		{
			var nameHash = tool.displayName.GetHashCode();
			var hue = Mathf.Abs((nameHash + 0.5f) % 64f) / 64f;
			EditorUtil.BeginColorPocket(Color.HSVToRGB(hue, 0.2f, 1));

			GUILayout.BeginVertical(string.Format("{0}{1}", tool.displayName, tool.methodParamsHash != 0 ? " [P]" : string.Empty), toolStyle);
			{
				GUILayout.Label(tool.menuItem);

				GUILayout.BeginHorizontal();
				{
					if (tool.MethodInfo != null) {
						bool validationException = false;

						try {
							EditorGUI.BeginDisabledGroup(tool.Validation != null && !(bool) tool.Validation.Invoke(null, null));
						} catch {
							validationException = true;
						}

						if (GUILayout.Button(new GUIContent("Run / Open", tool.menuItem), EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
							RunMethodInfo(tool.MethodInfo);

							if (!toolboxPrefs.dockable)
							{
								Close();
							}
						}

						if (!validationException) {
							EditorGUI.EndDisabledGroup();
						}

						if (favoriteTools.Content.FirstOrDefault(a => a.fingerprint == tool.fingerprint) != null) {
							if (GUILayout.Button("[Un-Pin]", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
								favoriteTools.Content.Remove(tool);
								SaveToolsPrefs();
								GUIUtility.ExitGUI();
							}
						} else {
							if (GUILayout.Button("[Pin]", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
								favoriteTools.Content.Add(tool);
								SaveToolsPrefs();
								GUIUtility.ExitGUI();
							}
						}
					} else {
						EditorUtil.BeginColorPocket(Color.red);
						GUILayout.Label("Missing!", EditorStyles.helpBox);

						if (GUILayout.Button("Remove")) {
							favoriteTools.Content.Remove(tool);
							SaveToolsPrefs();
							GUIUtility.ExitGUI();
						}

						EditorUtil.EndColorPocket();
					}
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();

			GUILayout.Space(4);

			EditorUtil.EndColorPocket();
		}

		private void RunMethodInfo(MethodInfo methodInfo)
		{
			var methodInfoParams = methodInfo.GetParameters();

			if (methodInfoParams.Length > 0) {
				SearchableToolboxParamsWizard.Open(methodInfoParams, (p) => { methodInfo.Invoke(null, p); });
			} else {
				methodInfo.Invoke(null, null);
			}
		}

		private void DrawRecursiveTree(ToolNode node, bool forceExpandable = false)
		{
			bool peersHaveChildren = false;
			var nodePeers = node.peers;
			if (nodePeers != null) {
				foreach (var nodePeer in nodePeers) {
					if (nodePeer.children.Count > 0) {
						peersHaveChildren = true;
						break;
					}
				}
			}

			if (!peersHaveChildren && !forceExpandable && node.children.Count == 0) {
				if (node.content != null) {
					foreach (var toolId in node.content) {
						DrawTool(tools[toolId]);
					}
				}

				return;
			}

			node.Expand = EditorUtil.ExpandButton(node.Expand, node.Name);

			if (node.Expand) {
				GUILayout.BeginHorizontal();
				{
					EditorUtil.VerticalRule();
					GUILayout.Space(6);

					GUILayout.BeginVertical();
					{
						if (node.content != null) {
							foreach (var toolId in node.content) {
								DrawTool(tools[toolId]);
							}
						}

						foreach (var child in node.children) {
							DrawRecursiveTree(child);
						}

						GUILayout.Space(6);
					}
					GUILayout.EndVertical();
				}

				GUILayout.EndHorizontal();
			}
		}

		private static void CollectToolsTask(object state)
		{
			CollectToolsTaskParamsHandle handle = (CollectToolsTaskParamsHandle) state;
			
			try {
				var assemblies = AppDomain.CurrentDomain.GetAssemblies();

				int assemblyIndex = -1;
				foreach (var assembly in assemblies) {
					assemblyIndex++;

					var types = assembly.GetTypes();
					var validations = new List<ValidationMethodPiece>();

					int typeIndex = 0;
					foreach (var type in types) {
						handle.progress = (float) assemblyIndex / assemblies.Length + (float) typeIndex / (types.Length) / assemblies.Length;
						typeIndex++;

						foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
							if (handle.requestCancelTask) {
								return;
							}

							if (!handle.toolboxPrefs.collectMethodsWithParams && method.GetParameters().Length > 0) {
								continue;
							}

							var menuItemAttributes = method.GetCustomAttributes(false).OfType<MenuItem>().ToList();
							var nonValidateMenuItemAttribute = menuItemAttributes.FirstOrDefault(a => !a.validate);

							if (menuItemAttributes.Count == 0) {
								continue;
							}

							foreach (var menuItemAtt in menuItemAttributes) {
								if (menuItemAtt.validate) {
									validations.Add(new ValidationMethodPiece() {menuItem = menuItemAtt.menuItem, method = method});
								}
							}

							if (nonValidateMenuItemAttribute == null) {
								continue;
							}

							ToolNode node = FindOrCreateNode(handle.toolNodeRoot, nonValidateMenuItemAttribute.menuItem);

							var toolId = handle.tools.Count;
							node.content.Add(toolId);

							var toolDisplayName = nonValidateMenuItemAttribute.menuItem.Split('/', '.').LastOrDefault(a => !string.IsNullOrEmpty(a));

							var tool = new Tool() {
								displayName = toolDisplayName,
								MethodInfo = method,
								menuItem = nonValidateMenuItemAttribute.menuItem,
							};

							tool.GenerateFingerprint();
							handle.tools.Add(tool);

							handle.requestRepaint = true;
						}
					}

					// hook up validations
					foreach (var validation in validations) {
						var node = FindOrCreateNode(handle.toolNodeRoot, validation.menuItem, false);

						if (node != null) {
							foreach (var toolId in node.content) {
								handle.tools[toolId].Validation = validation.method;
							}
						}
					}

					// sort results
					handle.toolNodeRoot.children.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.Ordinal));
				}
			} catch (Exception e) {
				Debug.LogError(e);
			} finally {
				handle.requestRepaint = true;
				handle.requestCancelTask = false;
				handle.progress = 100;
				handle.working = false;

				if (handle.onTaskDone != null)
					handle.onTaskDone.Invoke();
			}
		}

		private static ToolNode FindOrCreateNode(ToolNode toolNodeRoot, string menuItem, bool create = true)
		{
			if (menuItem == null) {
				menuItem = string.Empty;
			}

			var elements = menuItem.Split('/', '.');

			ToolNode node = toolNodeRoot;

			for (int i = 0; i < elements.Length; i++) {
				if (string.IsNullOrEmpty(elements[i])) {
					continue;
				}

				foreach (var genericTreeNode in node.children) {
					var child = genericTreeNode;

					if (child.Name.Equals(elements[i], StringComparison.OrdinalIgnoreCase)) {
						node = child;
						goto innerLoopExit;
					}
				}

				if (create) {
					node = new ToolNode(node, new List<int>()) {
						Name = elements[i],
					};
				} else {
					return null;
				}

				innerLoopExit: ;
			}

			return node;
		}

		private void SaveToolsPrefs()
		{
			var json = JsonUtility.ToJson(toolboxPrefs);
			EditorPrefs.SetString(prefsKey, json);
		}

		private void OnSelectionChange()
		{
			Repaint();
		}


		// -------- tool proxies ---------
#if UNITY_2018_1_OR_NEWER
		// The package manager does not show up in the search, so we have to add it manually.
		[MenuItem("Window/Package Manager")]
		private static void OpenPackageManager ()
		{
			UnityEditor.PackageManager.UI.Window.Open("");
		}
#endif
	}
}