using UnityEngine;

namespace Denid.CodeInspector.Demo
{
    public class HUD : MonoBehaviour
    {
        public Health health;

        void OnEnable()
        {
            if (health != null)
                health.Changed += OnHealthChanged;
        }

        void OnDisable()
        {
            if (health != null)
                health.Changed -= OnHealthChanged;
        }

        void OnHealthChanged(int value)
        {
            Debug.Log("Health changed: " + value);
        }
    }
}
