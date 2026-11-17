using UnityEngine;
using UnityEngine.Events;

namespace Denid.CodeInspector.Demo
{
    [RequireComponent(typeof(Collider))]
    public class TriggerZone : MonoBehaviour
    {
        public UnityEvent onEnter;

        void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            other.SendMessage("OnZoneEntered", SendMessageOptions.DontRequireReceiver);
            onEnter?.Invoke();
        }
    }
}
