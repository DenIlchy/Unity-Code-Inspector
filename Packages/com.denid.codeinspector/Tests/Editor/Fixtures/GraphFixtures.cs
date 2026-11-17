using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Denid.CodeInspector.Editor.Tests
{
    public abstract class FxBase : MonoBehaviour
    {
    }

    public interface IFxDamageDealer
    {
        void Deal();
    }

    public class FxHealth : MonoBehaviour
    {
        public int hp = 10;
        public event Action Died;

        public void TakeDamage(int amount)
        {
            Died?.Invoke();
        }

        public void Heal()
        {
        }
    }

    public class FxEnemy : FxBase, IFxDamageDealer
    {
        public FxHealth target;
        [SerializeField] FxHealth[] others = null;

        public void Deal()
        {
        }

        void Attack()
        {
            target.TakeDamage(1);
            if (others != null && others.Length > 0)
                others[0].TakeDamage(1);
            GetComponent<FxHealth>();
        }
    }

    public class FxListener : MonoBehaviour
    {
        void Awake()
        {
            GetComponent<FxHealth>().Died += OnDied;
        }

        void OnDied()
        {
        }

        void Update()
        {
        }

        void OnTriggerEnter(Collider c)
        {
        }
    }

    public class FxMessenger : MonoBehaviour
    {
        void Start()
        {
            SendMessage("TakeDamage", 1);
        }
    }

    public class FxUsage : MonoBehaviour
    {
        public void Use(FxHealth h)
        {
        }
    }

    public class FxCoroutineUser : MonoBehaviour
    {
        public FxHealth h;

        IEnumerator Start()
        {
            yield return null;
            h.TakeDamage(2);
        }
    }

    public class FxLambdaUser : MonoBehaviour
    {
        public FxHealth h;

        void Awake()
        {
            Action a = () => h.TakeDamage(3);
            a();
        }
    }

    public class FxService
    {
        public int score;
        public static FxService Instance = new FxService();

        public void Add(int v)
        {
            score += v;
        }
    }

    public class FxScorer : MonoBehaviour
    {
        void Start()
        {
            FxService.Instance.Add(1);
            var s = FxService.Instance.score;
            _ = s;
        }
    }

    public class FxInterfaceCaller : MonoBehaviour
    {
        public FxEnemy e;

        void Start()
        {
            IFxDamageDealer d = e;
            d.Deal();
        }
    }

    [Serializable]
    public class FxNested
    {
        public UnityEvent nestedEvent = new UnityEvent();
    }

    public class FxEventSource : MonoBehaviour
    {
        public UnityEvent onFire = new UnityEvent();
        public UnityEvent<int> onValue = new UnityEvent<int>();
        public FxNested nested = new FxNested();
    }

    public class FxFinder : MonoBehaviour
    {
        void Start()
        {
            FindFirstObjectByType<FxHealth>().TakeDamage(1);
        }
    }

    public class FxConfig : ScriptableObject
    {
        public int value = 5;
    }

    public class FxConfigUser : MonoBehaviour
    {
        public FxConfig config;
    }

    public enum FxEnum { A, B, C }

    public class FxFields : MonoBehaviour
    {
        public int i = 1;
        public float f = 1.5f;
        public bool b = true;
        public string s = "hello";
        public FxEnum e = FxEnum.B;
        public Vector3 v = new Vector3(1f, 2f, 3f);
        public FxConfig reference;
    }
}
