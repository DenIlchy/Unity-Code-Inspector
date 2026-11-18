using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Denid.CodeInspector.Editor
{
    /// <summary>
    /// Attaches runtime (non-persistent) listeners to UnityEvents located by
    /// serialized property path, including generic UnityEvent&lt;T0..T3&gt;.
    /// </summary>
    public static class UnityEventRelay
    {
        public sealed class Registration
        {
            public Object Owner;
            public string EventPath;
            public UnityEventBase Event;
            public Delegate Listener;
        }

        /// <summary>Resolves the UnityEventBase instance at a serialized property path.</summary>
        public static UnityEventBase ResolveEvent(Object owner, string eventPath)
        {
            if (owner == null || string.IsNullOrEmpty(eventPath))
                return null;
            try
            {
                var so = new SerializedObject(owner);
                var p = so.FindProperty(eventPath);
                if (p?.boxedValue is UnityEventBase evt)
                    return evt;
            }
            catch (Exception)
            {
                // Fall through to reflection.
            }
            return WalkPath(owner, eventPath) as UnityEventBase;
        }

        /// <summary>Reflection fallback: walk fields, incl. "Array.data[i]" segments.</summary>
        internal static object WalkPath(object root, string path)
        {
            object current = root;
            foreach (var seg in path.Split('.'))
            {
                if (current == null)
                    return null;
                if (seg == "Array")
                    continue;
                var name = seg;
                var index = -1;
                var bracket = seg.IndexOf('[');
                if (bracket >= 0)
                {
                    name = seg.Substring(0, bracket);
                    int.TryParse(seg.Substring(bracket + 1).TrimEnd(']'), out index);
                }
                var type = current.GetType();
                FieldInfo f = null;
                for (var t = type; t != null && f == null; t = t.BaseType)
                {
                    f = t.GetField(name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (f == null)
                    return null;
                current = f.GetValue(current);
                if (index >= 0)
                {
                    if (current is System.Collections.IList list && index < list.Count)
                        current = list[index];
                    else
                        return null;
                }
            }
            return current;
        }

        /// <summary>Adds a runtime listener that invokes onFire(owner, eventPath).</summary>
        public static Registration Register(Object owner, string eventPath,
            Action<Object, string> onFire)
        {
            var evt = ResolveEvent(owner, eventPath);
            if (evt == null)
                return null;
            var del = CreateListener(evt, () => onFire(owner, eventPath));
            if (del == null)
                return null;
            AddListener(evt, del);
            return new Registration
            {
                Owner = owner,
                EventPath = eventPath,
                Event = evt,
                Listener = del,
            };
        }

        public static void Unregister(Registration r)
        {
            if (r?.Event == null || r.Listener == null)
                return;
            try
            {
                RemoveListener(r.Event, r.Listener);
            }
            catch (Exception)
            {
                // Object may already be gone.
            }
            r.Listener = null;
        }

        // ---- listener construction --------------------------------------------

        class Relay0 { public Action Callback; public void Invoke() => Callback?.Invoke(); }
        class Relay1<T0> { public Action Callback; public void Invoke(T0 a) => Callback?.Invoke(); }
        class Relay2<T0, T1> { public Action Callback; public void Invoke(T0 a, T1 b) => Callback?.Invoke(); }
        class Relay3<T0, T1, T2> { public Action Callback; public void Invoke(T0 a, T1 b, T2 c) => Callback?.Invoke(); }
        class Relay4<T0, T1, T2, T3> { public Action Callback; public void Invoke(T0 a, T1 b, T2 c, T3 d) => Callback?.Invoke(); }

        /// <summary>Reflection handles resolved once per concrete event type.</summary>
        class EventCache
        {
            public Type RelayType;
            public Type DelegateType;
            public MethodInfo Invoke;
            public FieldInfo Callback;
            public MethodInfo AddListener;
            public MethodInfo RemoveListener;
        }

        static readonly Dictionary<Type, EventCache> eventCaches =
            new Dictionary<Type, EventCache>();

        static EventCache CacheFor(Type eventType)
        {
            if (eventCaches.TryGetValue(eventType, out var c))
                return c;
            eventCaches[eventType] = c = BuildCache(eventType);
            return c;
        }

        static EventCache BuildCache(Type eventType)
        {
            Type relayType;
            Type delegateType;
            if (typeof(UnityEvent).IsAssignableFrom(eventType))
            {
                relayType = typeof(Relay0);
                delegateType = typeof(UnityAction);
            }
            else
            {
                var generic = GenericEventType(eventType);
                if (generic == null)
                    return null;
                var args = generic.GetGenericArguments();
                switch (args.Length)
                {
                    case 1:
                        relayType = typeof(Relay1<>).MakeGenericType(args);
                        delegateType = typeof(UnityAction<>).MakeGenericType(args);
                        break;
                    case 2:
                        relayType = typeof(Relay2<,>).MakeGenericType(args);
                        delegateType = typeof(UnityAction<,>).MakeGenericType(args);
                        break;
                    case 3:
                        relayType = typeof(Relay3<,,>).MakeGenericType(args);
                        delegateType = typeof(UnityAction<,,>).MakeGenericType(args);
                        break;
                    case 4:
                        relayType = typeof(Relay4<,,,>).MakeGenericType(args);
                        delegateType = typeof(UnityAction<,,,>).MakeGenericType(args);
                        break;
                    default:
                        return null;
                }
            }
            return new EventCache
            {
                RelayType = relayType,
                DelegateType = delegateType,
                Invoke = relayType.GetMethod("Invoke"),
                Callback = relayType.GetField("Callback"),
                AddListener = ListenerMethod(eventType, delegateType, "AddListener"),
                RemoveListener = ListenerMethod(eventType, delegateType, "RemoveListener"),
            };
        }

        /// <summary>Finds the UnityEvent&lt;T...&gt; base type and its generic args.</summary>
        static Type GenericEventType(Type t)
        {
            for (var x = t; x != null; x = x.BaseType)
            {
                if (x.IsGenericType && typeof(UnityEventBase).IsAssignableFrom(x))
                {
                    var def = x.GetGenericTypeDefinition();
                    var name = def.Name;
                    if (name.StartsWith("UnityEvent`", StringComparison.Ordinal))
                        return x;
                }
            }
            return null;
        }

        static Delegate CreateListener(UnityEventBase evt, Action callback)
        {
            var c = CacheFor(evt.GetType());
            if (c == null)
                return null;
            var instance = Activator.CreateInstance(c.RelayType);
            c.Callback.SetValue(instance, callback);
            return Delegate.CreateDelegate(c.DelegateType, instance, c.Invoke);
        }

        static MethodInfo ListenerMethod(Type eventType, Type delegateType, string name)
        {
            for (var t = eventType; t != null; t = t.BaseType)
            {
                var m = t.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { delegateType }, null);
                if (m != null)
                    return m;
            }
            return null;
        }

        static void AddListener(UnityEventBase evt, Delegate del)
        {
            CacheFor(evt.GetType())?.AddListener?.Invoke(evt, new object[] { del });
        }

        static void RemoveListener(UnityEventBase evt, Delegate del)
        {
            CacheFor(evt.GetType())?.RemoveListener?.Invoke(evt, new object[] { del });
        }
    }
}
