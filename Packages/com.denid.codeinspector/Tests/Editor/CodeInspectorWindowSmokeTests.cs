using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Denid.CodeInspector.Editor.Tests
{
    public class CodeInspectorWindowSmokeTests
    {
        [UnityTest]
        [Explicit("Requires a graphics device; cannot run under -nographics batchmode.")]
        public IEnumerator WindowOpensWithToolbarAndCanvas()
        {
            if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device available in this run.");
            var window = EditorWindow.GetWindow<CodeInspectorWindow>();
            yield return null;

            try
            {
                var toolbar = window.rootVisualElement.Q<Toolbar>();
                Assert.IsNotNull(toolbar, "toolbar missing");
                var canvas = window.rootVisualElement.Q<GraphCanvas>();
                Assert.IsNotNull(canvas, "graph canvas missing");
            }
            finally
            {
                window.Close();
            }
        }
    }
}
