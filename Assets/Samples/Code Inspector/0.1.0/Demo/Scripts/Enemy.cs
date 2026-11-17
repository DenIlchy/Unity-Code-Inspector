using System.Collections;
using UnityEngine;

namespace Denid.CodeInspector.Demo
{
    public class Enemy : MonoBehaviour
    {
        public float speed = 3f;
        public int damage = 10;
        public Transform target;
        public GameConfig config;

        void Start()
        {
            if (config != null)
                damage = config.baseEnemyDamage;
            StartCoroutine(AttackLoop());
        }

        IEnumerator AttackLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(1.5f);
                if (target != null)
                    target.GetComponent<IDamageable>()?.TakeDamage(damage);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            var damageable = collision.gameObject.GetComponent<IDamageable>();
            damageable?.TakeDamage(damage);
        }
    }
}
