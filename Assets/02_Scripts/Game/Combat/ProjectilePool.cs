using UnityEngine;

namespace VisionToWall.Game
{
    /// <summary>
    /// Fixed set of projectile objects moved in straight lines. Instances are created once in
    /// <see cref="Prepare"/>; launching and moving allocate nothing.
    /// </summary>
    public sealed class ProjectilePool : MonoBehaviour
    {
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField, Min(1), Tooltip("One shot in flight plus the reflected or missed one still leaving.")]
        private int size = 4;

        private struct Shot
        {
            public GameObject gameObject;
            public Transform transform;
            public Vector3 from;
            public Vector3 to;
            public float elapsed;
            public float duration;
            public bool active;
        }

        private Shot[] _shots;

        public int ActiveCount
        {
            get
            {
                if (_shots == null) return 0;
                int count = 0;
                for (int i = 0; i < _shots.Length; i++)
                {
                    if (_shots[i].active) count++;
                }
                return count;
            }
        }

        /// <summary>Creates the instances once. Further calls do nothing.</summary>
        public void Prepare(int layer)
        {
            if (_shots != null) return;

            if (projectilePrefab == null)
            {
                Debug.LogError("[Combat] ProjectilePool has no prefab.");
                _shots = new Shot[0];
                return;
            }

            _shots = new Shot[size];
            for (int i = 0; i < size; i++)
            {
                GameObject instance = Instantiate(projectilePrefab, transform);
                CombatLayers.SetRecursively(instance.transform, layer);
                instance.SetActive(false);
                _shots[i] = new Shot { gameObject = instance, transform = instance.transform };
            }
        }

        /// <summary>Returns the shot index, or -1 when every object is busy (the visual is skipped).</summary>
        public int Launch(Vector3 from, Vector3 to, float duration)
        {
            if (_shots == null) return -1;

            for (int i = 0; i < _shots.Length; i++)
            {
                if (_shots[i].active) continue;

                _shots[i].from = from;
                _shots[i].to = to;
                _shots[i].elapsed = 0f;
                _shots[i].duration = Mathf.Max(duration, 0.001f);
                _shots[i].active = true;
                _shots[i].transform.position = from;
                _shots[i].gameObject.SetActive(true);
                return i;
            }
            return -1;
        }

        public void Release(int index)
        {
            if (_shots == null || index < 0 || index >= _shots.Length || !_shots[index].active) return;

            _shots[index].active = false;
            _shots[index].gameObject.SetActive(false);
        }

        public void ReleaseAll()
        {
            if (_shots == null) return;

            for (int i = 0; i < _shots.Length; i++) Release(i);
        }

        /// <summary>Moves every shot. A shot that reached its end is released.</summary>
        public void Tick(float deltaTime)
        {
            if (_shots == null) return;

            for (int i = 0; i < _shots.Length; i++)
            {
                if (!_shots[i].active) continue;

                _shots[i].elapsed += deltaTime;
                if (_shots[i].elapsed >= _shots[i].duration)
                {
                    Release(i);
                    continue;
                }
                _shots[i].transform.position =
                    Vector3.Lerp(_shots[i].from, _shots[i].to, _shots[i].elapsed / _shots[i].duration);
            }
        }
    }
}
