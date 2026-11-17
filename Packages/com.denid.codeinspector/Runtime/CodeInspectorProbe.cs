using System;
using UnityEngine;

namespace Denid.CodeInspector
{
    /// <summary>Static hub for probe contact events (editor only).</summary>
    public static class CodeInspectorProbes
    {
#if UNITY_EDITOR
        /// <summary>(self, other, message name).</summary>
        public static event Action<GameObject, GameObject, string> Contact;

        public static void Raise(GameObject self, GameObject other, string message)
        {
            Contact?.Invoke(self, other, message);
        }
#endif
    }

    /// <summary>
    /// Attached by the editor in Play Mode to GameObjects that may send
    /// trigger/collision Unity messages. Inert in player builds.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class CodeInspectorProbe : MonoBehaviour
    {
#if UNITY_EDITOR
        static GameObject Other(Collider c) =>
            c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : c.gameObject;

        static GameObject Other(Collider2D c) =>
            c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : c.gameObject;

        static GameObject Other(Collision c) =>
            c.rigidbody != null ? c.rigidbody.gameObject : c.gameObject;

        static GameObject Other(Collision2D c) =>
            c.rigidbody != null ? c.rigidbody.gameObject : c.gameObject;

        void OnTriggerEnter(Collider c) => CodeInspectorProbes.Raise(gameObject, Other(c), "OnTriggerEnter");
        void OnTriggerExit(Collider c) => CodeInspectorProbes.Raise(gameObject, Other(c), "OnTriggerExit");
        void OnCollisionEnter(Collision c) => CodeInspectorProbes.Raise(gameObject, Other(c), "OnCollisionEnter");
        void OnCollisionExit(Collision c) => CodeInspectorProbes.Raise(gameObject, Other(c), "OnCollisionExit");
        void OnTriggerEnter2D(Collider2D c) => CodeInspectorProbes.Raise(gameObject, Other(c), "OnTriggerEnter2D");
        void OnTriggerExit2D(Collider2D c) => CodeInspectorProbes.Raise(gameObject, Other(c), "OnTriggerExit2D");
        void OnCollisionEnter2D(Collision2D c) => CodeInspectorProbes.Raise(gameObject, Other(c), "OnCollisionEnter2D");
        void OnCollisionExit2D(Collision2D c) => CodeInspectorProbes.Raise(gameObject, Other(c), "OnCollisionExit2D");
#endif
    }
}
