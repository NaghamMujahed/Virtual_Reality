using PaintBucketSim.Systems.Interaction;
using UnityEditor;
using UnityEngine;

namespace PaintBucketSim.Editor
{
    [InitializeOnLoad]
    internal static class SimulationInteractionSceneViewBridge
    {
        private static SimulationInteractionSystem _activeSystem;

        static SimulationInteractionSceneViewBridge()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.playModeStateChanged += _ => _activeSystem = null;
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            if (!EditorApplication.isPlaying)
                return;

            SimulationInteractionSystem system = ResolveSystem();
            if (system == null)
                return;

            Event current = Event.current;
            if (current == null || current.alt)
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (system.BeginGrabFromRay(ray))
                {
                    current.Use();
                    sceneView.Repaint();
                }

                return;
            }

            if (!system.HasActiveGrab)
                return;

            if (current.type == EventType.MouseDrag && current.button == 0)
            {
                system.UpdateGrabFromRay(ray, 0.0f);
                current.Use();
                sceneView.Repaint();
                return;
            }

            if (current.type == EventType.ScrollWheel)
            {
                system.UpdateGrabFromRay(ray, -current.delta.y * 25.0f);
                current.Use();
                sceneView.Repaint();
                return;
            }

            if (current.type == EventType.MouseUp && current.button == 0)
            {
                system.EndGrab();
                current.Use();
                sceneView.Repaint();
                return;
            }

            if (current.type == EventType.Repaint)
                DrawGrabHandle(system);
        }

        private static SimulationInteractionSystem ResolveSystem()
        {
            if (_activeSystem != null)
                return _activeSystem;

            _activeSystem = Object.FindAnyObjectByType<SimulationInteractionSystem>();
            return _activeSystem;
        }

        private static void DrawGrabHandle(SimulationInteractionSystem system)
        {
            if (!system.HasActiveGrab)
                return;

            Handles.color = new Color(0.1f, 0.75f, 1.0f, 0.95f);
            Handles.DrawAAPolyLine(4.0f, system.GrabCurrentWorld, system.GrabTargetWorld);
            Handles.SphereHandleCap(
                0,
                system.GrabTargetWorld,
                Quaternion.identity,
                0.07f,
                EventType.Repaint
            );

            Handles.Label(
                system.GrabTargetWorld + Vector3.up * 0.08f,
                system.ActiveGrabLabel
            );
        }
    }
}
