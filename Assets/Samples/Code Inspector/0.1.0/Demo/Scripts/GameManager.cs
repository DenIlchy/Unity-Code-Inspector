using UnityEngine;
using UnityEngine.Events;

namespace Denid.CodeInspector.Demo
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public UnityEvent onGameOver;
        public ScoreService score = new ScoreService();

        void Awake()
        {
            Instance = this;
        }

        public void AddScore(int points)
        {
            score.AddPoints(points);
        }

        public void GameOver()
        {
            onGameOver?.Invoke();
        }
    }
}
