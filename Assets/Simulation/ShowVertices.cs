using UnityEngine;

namespace Simulation
{
    public class ShowVertices : MonoBehaviour
    {
        [Header("Display Settings")]
        [SerializeField, Min(0.001f)] float _sphereSize = 0.01f;
        [SerializeField] Color _vertexColor = Color.red;
        [SerializeField] bool _showOnlyInEditMode = true;

        private void OnDrawGizmos()
        {
            if (_showOnlyInEditMode && Application.isPlaying) return;

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            Vector3[] vertices = mf.sharedMesh.vertices;

            Gizmos.color = _vertexColor;

            foreach (Vector3 v in vertices)
            {
                Vector3 worldPos = transform.TransformPoint(v);
                Gizmos.DrawSphere(worldPos, _sphereSize);
            }
        }

        private void OnValidate()
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Debug.Log($"[ShowVertices] {gameObject.name}: {mf.sharedMesh.vertexCount} vertices");
            }
        }
    }
}