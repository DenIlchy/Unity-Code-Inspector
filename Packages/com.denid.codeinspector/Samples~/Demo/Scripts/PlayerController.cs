using UnityEngine;

namespace Denid.CodeInspector.Demo
{
    public class PlayerController : MonoBehaviour
    {
        public float speed = 2f;
        public float jumpHeight = 1.5f;
        public Health health;
        public float orbitRadius = 3f;

        float angle;

        void Update()
        {
            angle += speed * Time.deltaTime;
            transform.position = MathUtil.CirclePoint(Vector3.zero, orbitRadius, angle);
        }

        void OnZoneEntered()
        {
            Debug.Log(name + " entered a trigger zone");
        }
    }
}
