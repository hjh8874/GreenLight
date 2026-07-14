using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.TilemapTest
{
    public sealed class TilemapTestVehicleAgent : MonoBehaviour
    {
        // ponytail: minimum bumper gap; visual tuning knob, adjust in inspector if prefab size changes.
        [SerializeField] private float followGap = 0.28f;

        // Registry so each agent can see the car ahead. n<=maxActiveVehicles(80) -> O(n^2) scan is fine.
        private static readonly List<TilemapTestVehicleAgent> Active = new List<TilemapTestVehicleAgent>();

        private readonly List<Vector3> path = new List<Vector3>();
        private float speed;
        private int targetIndex;

        public bool IsRunning { get; private set; }
        public Vector3 Heading { get; private set; }

        public void Configure(IReadOnlyList<Vector3> worldPath, float moveSpeed)
        {
            path.Clear();
            path.AddRange(worldPath);
            speed = Mathf.Max(0.01f, moveSpeed);
            targetIndex = path.Count > 1 ? 1 : 0;
            IsRunning = path.Count > 1;

            if (path.Count > 0)
            {
                transform.position = path[0];
            }
        }

        private void OnEnable() => Active.Add(this);

        private void OnDisable() => Active.Remove(this);

        private void Update()
        {
            if (!IsRunning || targetIndex >= path.Count)
            {
                return;
            }

            Vector3 target = path[targetIndex];
            Vector3 position = transform.position;
            Vector3 dir = target - position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f)
            {
                Heading = dir.normalized;
            }

            LookAtTarget(target);

            // Congestion handling: hold position this frame if a same-lane, same-direction car
            // is within followGap ahead. Cars queue with a gap instead of overlapping.
            if (IsBlockedAhead(position))
            {
                return;
            }

            transform.position = Vector3.MoveTowards(position, target, speed * Time.deltaTime);

            if (Vector3.SqrMagnitude(transform.position - target) <= 0.0004f)
            {
                targetIndex++;

                if (targetIndex >= path.Count)
                {
                    IsRunning = false;
                    Destroy(gameObject);
                }
            }
        }

        private bool IsBlockedAhead(Vector3 position)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                TilemapTestVehicleAgent other = Active[i];
                if (other == this || !other.IsRunning)
                {
                    continue;
                }

                if (WithinFollowZone(other.transform.position - position, Heading, other.Heading, followGap))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="relative"/> (other - self) sits in the follow zone: ahead of us,
        /// within <paramref name="gap"/>, roughly the same lane, and travelling the same direction.
        /// Oncoming and cross traffic are ignored so head-on cars don't deadlock.
        /// </summary>
        public static bool WithinFollowZone(Vector3 relative, Vector3 heading, Vector3 otherHeading, float gap)
        {
            relative.y = 0f;
            float forward = Vector3.Dot(relative, heading);
            if (forward <= 0.01f || forward > gap)
            {
                return false;
            }

            Vector3 lateral = relative - forward * heading;
            if (lateral.magnitude > gap * 0.6f)
            {
                return false;
            }

            return Vector3.Dot(heading, otherHeading) >= 0.5f;
        }

        private void LookAtTarget(Vector3 target)
        {
            Vector3 direction = target - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }
}

/*
Unity implementation:
1. This component is added to runtime vehicle instances by TilemapTestVehicleTripSystem.
2. It receives a world-space route and moves the visual vehicle along that route.
3. It destroys itself at the destination because the simulation owns economy/arrival logic.
*/
