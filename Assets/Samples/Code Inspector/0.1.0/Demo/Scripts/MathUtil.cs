using UnityEngine;

namespace Denid.CodeInspector.Demo
{
    public static class MathUtil
    {
        public static Vector3 CirclePoint(Vector3 center, float radius, float angle)
        {
            return center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        }
    }
}
