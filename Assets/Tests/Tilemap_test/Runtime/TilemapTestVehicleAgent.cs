using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.TilemapTest
{
    public sealed class TilemapTestVehicleAgent : MonoBehaviour
    {
        private readonly List<Vector3> path = new List<Vector3>();
        private float speed;
        private int targetIndex;

        public bool IsRunning { get; private set; }

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

        private void Update()
        {
            if (!IsRunning || targetIndex >= path.Count)
            {
                return;
            }

            Vector3 target = path[targetIndex];
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            LookAtTarget(target);

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
