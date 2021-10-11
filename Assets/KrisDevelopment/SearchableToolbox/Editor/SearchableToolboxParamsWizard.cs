using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SearchableToolbox
{
	public class SearchableToolboxParamsWizard : EditorWindow
	{
		private ParameterInfo[] methodParams;
		private object[] methodParamsValues;
		private Action<object[]> onConfirm;

		public static void Open(ParameterInfo[] methodParams, Action<object[]> onConfirm)
		{
			var window = SETUtil.EditorUtil.ShowUtilityWindow<SearchableToolboxParamsWizard>("Run Method With Params");
			window.methodParams = methodParams;
			window.methodParamsValues = new object[methodParams.Length];
			window.onConfirm = onConfirm;
		}

		private void OnGUI()
		{
			bool parametersError = false;
			
			for (int i = 0; i < methodParams.Length; i++) {
				var _type = methodParams[i].ParameterType;
				
				if (_type == typeof(int)) {
					methodParamsValues[i] = EditorGUILayout.IntField(methodParams[i].Name, (int) methodParamsValues[i]);
				} else if (_type == typeof(float)) {
					methodParamsValues[i] = EditorGUILayout.FloatField(methodParams[i].Name, (float) methodParamsValues[i]);
				} else if (_type == typeof(string)) {
					methodParamsValues[i] = EditorGUILayout.TextField(methodParams[i].Name, (string) methodParamsValues[i]);
				} else if (typeof(Object).IsAssignableFrom(_type)) {
					methodParamsValues[i] = EditorGUILayout.ObjectField(methodParams[i].Name, (Object) methodParamsValues[i], _type, true);
				} else {
					EditorGUILayout.HelpBox(String.Format("Unhandled Type: {0}", _type.ToString()), MessageType.Error);
					parametersError = true;
				}
			}

			EditorGUI.BeginDisabledGroup(parametersError);
			{
				if (GUILayout.Button("Confirm")) {
					onConfirm.Invoke(methodParams);
				}
			}
			EditorGUI.EndDisabledGroup();
		}
	}
}