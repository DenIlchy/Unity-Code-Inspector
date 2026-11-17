using System.Collections;
using UnityEngine;

namespace Denid.CodeInspector.Demo
{
    public class Spawner : MonoBehaviour
    {
        public Enemy prefab;
        public float interval = 3f;
        public GameConfig config;

        public void StopSpawning()
        {
            StopAllCoroutines();
        }

        IEnumerator Start()
        {
            var wait = new WaitForSeconds(config != null ? config.spawnInterval : interval);
            while (true)
            {
                yield return wait;
                if (prefab != null)
                {
                    Instantiate(prefab, transform.position, Quaternion.identity);
                    GameManager.Instance?.AddScore(1);
                }
            }
        }
    }
}
