using UnityEngine;

namespace Denid.CodeInspector.Demo
{
    [CreateAssetMenu(menuName = "Code Inspector Demo/Game Config")]
    public class GameConfig : ScriptableObject
    {
        public int baseEnemyDamage = 10;
        public float spawnInterval = 3f;
        public float difficulty = 1f;
    }
}
