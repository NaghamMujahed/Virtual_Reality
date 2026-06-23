using UnityEngine;
using UnityEditor;

namespace Simulation.Editor
{
    public static class RopeSceneBuilder
    {
        [MenuItem("Simulation/Build Rope Scene", priority = 0)]
        static void Build()
        {
            // ── 1. Locate compute shader ──────────────────────────────────────
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Simulation/CosseratRod.compute");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Rope Scene Builder",
                    "CosseratRod.compute not found at Assets/Simulation/.",
                    "OK");
                return;
            }

            // ── 2. Remove old objects if rebuilding ───────────────────────────
            foreach (string n in new[] { "TopAnchor", "Rope", "Bucket" })
            {
                var old = GameObject.Find(n);
                if (old != null) Undo.DestroyObjectImmediate(old);
            }

            // ── 3. TopAnchor — fixed, no script ───────────────────────────────
            var anchorGO = new GameObject("TopAnchor");
            Undo.RegisterCreatedObjectUndo(anchorGO, "Create TopAnchor");
            anchorGO.transform.position = new Vector3(0f, 3f, 0f);

            // ── 4. Rope ───────────────────────────────────────────────────────
            var ropeGO = new GameObject("Rope");
            Undo.RegisterCreatedObjectUndo(ropeGO, "Create Rope");
            ropeGO.transform.position = Vector3.zero;

            var lr = ropeGO.AddComponent<LineRenderer>();
            lr.startWidth     = 0.045f;
            lr.endWidth       = 0.040f;
            lr.numCapVertices = 4;
            lr.useWorldSpace  = true;
            lr.positionCount  = 0;
            var ropeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            ropeMat.color = new Color(0.68f, 0.52f, 0.34f);
            ropeMat.SetFloat("_Smoothness", 0.10f);
            ropeMat.SetFloat("_Metallic", 0f);
            lr.material = ropeMat;

            var ropeRenderer = ropeGO.AddComponent<RopeRenderer>();
            var sim          = ropeGO.AddComponent<RopeSimulation>();
            ropeGO.AddComponent<RopeGrab>();    // auto-resolves RopeSimulation via GetComponent
            ropeGO.AddComponent<RopeDebugUI>(); // runtime parameter panel (F1 to toggle)

            // ── 5. Bucket ─────────────────────────────────────────────────────
            var bucketGO = new GameObject("Bucket");
            Undo.RegisterCreatedObjectUndo(bucketGO, "Create Bucket");
            bucketGO.transform.position = new Vector3(0f, 1f, 0f);

            var mf = bucketGO.AddComponent<MeshFilter>();
            mf.sharedMesh = BucketMeshBuilder.Build();

            var mr = bucketGO.AddComponent<MeshRenderer>();
            var bucketMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            bucketMat.color = new Color(0.85f, 0.50f, 0.25f);
            bucketMat.SetFloat("_Smoothness", 0.50f);
            bucketMat.SetFloat("_Metallic", 0.0f);
            mr.sharedMaterial = bucketMat;

            var bucket = bucketGO.AddComponent<BucketController>();

            // ── 6. Wire RopeSimulation references ─────────────────────────────
            var simSO = new SerializedObject(sim);
            simSO.FindProperty("_shader").objectReferenceValue    = shader;
            simSO.FindProperty("_topAnchor").objectReferenceValue = anchorGO.transform;
            simSO.FindProperty("_bucket").objectReferenceValue    = bucket;
            simSO.FindProperty("_renderer").objectReferenceValue  = ropeRenderer;
            simSO.ApplyModifiedProperties();

            // RopeGrab auto-resolves RopeSimulation via GetComponent — no wiring needed

            // ── 8. Position camera + add orbit control ────────────────────────
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 2f, -6f);
                cam.transform.LookAt(new Vector3(0f, 1.5f, 0f));
                if (!cam.TryGetComponent<CameraOrbit>(out _))
                    cam.gameObject.AddComponent<CameraOrbit>();
            }

            Selection.activeGameObject = ropeGO;
            EditorUtility.SetDirty(ropeGO);

            Debug.Log("[RopeSceneBuilder] Scene ready. Press Play → click rope/bucket to grab.");
        }

        [MenuItem("Simulation/Build Rope Scene", validate = true)]
        static bool ValidateBuild() => !EditorApplication.isPlaying;
    }
}
