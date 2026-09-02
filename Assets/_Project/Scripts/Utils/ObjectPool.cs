using System.Collections.Generic;
using UnityEngine;

namespace Pivot.Utils
{
    /// <summary>
    /// Optional hook for pooled prefabs that need to reset themselves between uses.
    /// </summary>
    public interface IPooled
    {
        void OnRented();
        void OnReturned();
    }

    /// <summary>
    /// A prewarmed pool of prefab instances. Nodes, edges, labels, particle bursts
    /// and audio sources all come from one of these, so nothing is instantiated or
    /// destroyed once the scene is running.
    /// </summary>
    public sealed class ComponentPool<T> where T : Component
    {
        readonly T _prefab;
        readonly Transform _parent;
        readonly Stack<T> _idle;
        readonly List<T> _live;

        public ComponentPool(T prefab, Transform parent, int prewarm = 0)
        {
            _prefab = prefab;
            _parent = parent;
            _idle = new Stack<T>(Mathf.Max(4, prewarm));
            _live = new List<T>(Mathf.Max(4, prewarm));

            for (int i = 0; i < prewarm; i++)
            {
                T instance = Create();
                instance.gameObject.SetActive(false);
                _idle.Push(instance);
            }
        }

        public int LiveCount
        {
            get { return _live.Count; }
        }

        public int IdleCount
        {
            get { return _idle.Count; }
        }

        public IReadOnlyList<T> Live
        {
            get { return _live; }
        }

        T Create()
        {
            return Object.Instantiate(_prefab, _parent);
        }

        public T Rent()
        {
            T instance = _idle.Count > 0 ? _idle.Pop() : Create();
            instance.gameObject.SetActive(true);
            _live.Add(instance);

            IPooled hook = instance as IPooled;
            if (hook != null) hook.OnRented();

            return instance;
        }

        public void Return(T instance)
        {
            if (instance == null) return;

            IPooled hook = instance as IPooled;
            if (hook != null) hook.OnReturned();

            instance.gameObject.SetActive(false);
            if (instance.transform.parent != _parent) instance.transform.SetParent(_parent, false);

            _live.Remove(instance);
            _idle.Push(instance);
        }

        /// <summary>Returns everything currently out on loan. Used by Reset.</summary>
        public void ReturnAll()
        {
            for (int i = _live.Count - 1; i >= 0; i--) Return(_live[i]);
        }
    }
}
