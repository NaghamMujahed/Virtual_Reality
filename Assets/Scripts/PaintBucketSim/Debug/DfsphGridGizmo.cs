using PaintBucketSim.Configs;
using UnityEngine;

namespace PaintBucketSim.Debugging
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class DfsphGridGizmo : MonoBehaviour
    {
        [Header("References")]
        public GpuDfsphSolverConfig config;
        public Transform bucketTransform;

        [Header("Grid Placement")]
        public bool centerOnBucket = true;
        public Vector3 centerOffset = Vector3.zero;

        [Header("Display")]
        public bool drawGrid = true;
        public bool drawOrigin = true;
        public Color gridColor = Color.cyan;
        public Color originColor = Color.yellow;

        private void OnDrawGizmos()
        {
            if (!drawGrid)
                return;

            if (config == null)
                return;

            Vector3 gridSize = new Vector3(
                config.gridResolution.x * config.gridCellSize,
                config.gridResolution.y * config.gridCellSize,
                config.gridResolution.z * config.gridCellSize
            );

            Vector3 origin = config.gridOriginWorld;

            if (centerOnBucket && bucketTransform != null)
            {
                origin =
                    bucketTransform.position +
                    centerOffset -
                    0.5f * gridSize;
            }

            Vector3 center =
                origin + 0.5f * gridSize;

            Gizmos.color = gridColor;
            Gizmos.DrawWireCube(center, gridSize);

            if (drawOrigin)
            {
                Gizmos.color = originColor;
                Gizmos.DrawSphere(origin, Mathf.Max(config.gridCellSize * 1.5f, 0.04f));
            }
        }
    }
}