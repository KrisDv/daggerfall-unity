using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace KrisFavoriteScenes {
	public class FavoriteScenes : EditorWindow {
		
		private static string GetPrefix() { return Application.productName + "_KFS_"; }

		[System.NonSerialized]
		private static List<string> _favoriteScenePaths = null;
		private static string[] FavoriteScenePaths
		{
			get
			{
				if(_favoriteScenePaths == null){
					_favoriteScenePaths = new List<string>();
					if(EditorPrefs.HasKey(GetPrefix() + "pinned")){
						_favoriteScenePaths.AddRange(EditorPrefs.GetString(GetPrefix() + "pinned").Split(';'));
						_favoriteScenePaths.RemoveAll(d => d.Equals(string.Empty));
						_favoriteScenePaths.TrimExcess();
					}
				}
				
				return _favoriteScenePaths.ToArray();
			}
		}


		private Vector2 scrollView = Vector2.zero;


		private void PinScene (string scenePath) {
			_favoriteScenePaths.Add(scenePath);
			EditorPrefs.SetString(GetPrefix() + "pinned", string.Join(";", _favoriteScenePaths.ToArray()));
		}

		private void UnPinScene (string scenePath){
			_favoriteScenePaths.Remove(scenePath);
			EditorPrefs.SetString(GetPrefix() + "pinned", string.Join(";", _favoriteScenePaths.ToArray()));
		}

		private void SortScenes (){
			_favoriteScenePaths.Sort();
			EditorPrefs.SetString(GetPrefix() + "pinned", string.Join(";", _favoriteScenePaths.ToArray()));
		}


		[MenuItem("Window/Kris Development/Favorite Scenes")]
		public static void ShowWindow ()
		{
			GetWindow<FavoriteScenes>("★ Fav. Scenes");
			_favoriteScenePaths = null;
		}

		private void OnGUI()
		{
			GUILayout.BeginHorizontal(EditorStyles.helpBox);
			{
				if(GUILayout.Button("Pin Current Scene", EditorStyles.miniButton)){
					string _currentScene = SceneManager.GetActiveScene().path;
					if(_currentScene != string.Empty)
						PinScene(_currentScene);
					else
						EditorUtility.DisplayDialog("Error:", "Can't add scene with no file path!", "Ok");
				}

				SceneAsset _sceneFromField = null;
				_sceneFromField = EditorGUILayout.ObjectField(_sceneFromField, typeof(SceneAsset), false) as SceneAsset;
				if(_sceneFromField != null){
					PinScene(AssetDatabase.GetAssetPath(_sceneFromField));
				}
			}
			GUILayout.EndHorizontal();

			GUILayout.BeginHorizontal(EditorStyles.toolbar);
			{
				GUILayout.Label("Pinned Scenes:");
				if(GUILayout.Button("▼ Sort Scenes", EditorStyles.toolbarButton)){
					SortScenes();
				}
			}
			GUILayout.EndHorizontal();

			scrollView = GUILayout.BeginScrollView(scrollView);

			foreach(string favoriteScenePath in FavoriteScenePaths){
				GUILayout.BeginHorizontal();
				{
					if(GUILayout.Button(new GUIContent("Q", "Ping Scene Asset"), GUILayout.ExpandWidth(false))){
						EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(favoriteScenePath));
					}

					if(GUILayout.Button(new GUIContent("Add", "Loads and adds the Scene to the currently open Scenes"), GUILayout.ExpandWidth(false))){
						EditorSceneManager.OpenScene(favoriteScenePath, OpenSceneMode.Additive);
					}

					if(GUILayout.Button(new GUIContent(System.IO.Path.GetFileNameWithoutExtension(favoriteScenePath), favoriteScenePath))){
						if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
							EditorSceneManager.OpenScene(favoriteScenePath, OpenSceneMode.Single);
					}

					if(GUILayout.Button(new GUIContent("X", "Un-pin"), GUILayout.ExpandWidth(false))){
						UnPinScene(favoriteScenePath);
						break;
					}
				}
				GUILayout.EndHorizontal();
			}

			GUILayout.EndScrollView();
		}
	}
}
