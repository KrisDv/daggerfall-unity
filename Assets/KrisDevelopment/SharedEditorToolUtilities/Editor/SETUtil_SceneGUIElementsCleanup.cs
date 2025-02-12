#if UNITY_EDITOR
using E = UnityEditor;

namespace SETUtil.EditorOnly
{
    [E.InitializeOnLoad]
    internal static class SceneGUIElementsCleanup
    {
        static SceneGUIElementsCleanup ()
        {
            UnityEditor.SceneManagement.EditorSceneManager.activeSceneChanged += (s1, s2) => {
                EditorUtil.ClearSceneGUI();
            };
        }
    }
}
#endif