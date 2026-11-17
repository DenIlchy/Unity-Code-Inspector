using System;
using UnityEngine;

namespace Denid.CodeInspector.Demo
{
    public class Health : MonoBehaviour, IDamageable
    {
        public event Action<int> Changed;
        public event Action Died;

        [SerializeField] int maxHealth = 100;
        public float regenPerSecond;

        int current;

        void Awake()
        {
            current = maxHealth;
        }

        void Update()
        {
            if (regenPerSecond > 0f && current < maxHealth)
            {
                current = Mathf.Min(maxHealth, current + Mathf.RoundToInt(regenPerSecond * Time.deltaTime));
                Changed?.Invoke(current);
            }
        }

        public void TakeDamage(int amount)
        {
            if (current <= 0)
                return;
            current = Mathf.Max(0, current - amount);
            Changed?.Invoke(current);
            if (current == 0)
                Died?.Invoke();
        }
    }
}
