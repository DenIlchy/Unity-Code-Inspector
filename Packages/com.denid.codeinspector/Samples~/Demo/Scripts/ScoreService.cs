namespace Denid.CodeInspector.Demo
{
    public class ScoreService
    {
        public static int HighScore;

        public int Current { get; private set; }

        public void AddPoints(int points)
        {
            Current += points;
            if (Current > HighScore)
                HighScore = Current;
        }
    }
}
