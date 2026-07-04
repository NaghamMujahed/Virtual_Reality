using PaintBucketSim.Systems.Rope;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RopeGrabSceneViewEditor
{
    private static readonly int ControlHint =
        "PaintBucketSim.RopeGrabSceneViewEditor".GetHashCode();

    private static RopeGrabInteractor _activeInteractor;

    static RopeGrabSceneViewEditor()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (!Application.isPlaying || sceneView == null || sceneView.camera == null)
            return;

        RopeGrabInteractor.EnsureSceneInteractors();

        Event current = Event.current;
        if (current == null)
            return;

        int controlId = GUIUtility.GetControlID(ControlHint, FocusType.Passive);
        EventType eventType = current.GetTypeForControl(controlId);

        if (_activeInteractor != null && !_activeInteractor.IsDragging)
            _activeInteractor = null;

        if (eventType == EventType.MouseDown &&
            current.button == 0 &&
            !current.alt)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            RopeGrabInteractor interactor = FindInteractorHit(ray, sceneView.camera);

            if (interactor != null)
            {
                _activeInteractor = interactor;
                GUIUtility.hotControl = controlId;
                current.Use();
                sceneView.Repaint();
            }
        }
        else if (eventType == EventType.MouseDrag &&
            GUIUtility.hotControl == controlId &&
            _activeInteractor != null)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            _activeInteractor.UpdateGrab(ray, sceneView.camera);
            current.Use();
            sceneView.Repaint();
        }
        else if (eventType == EventType.ScrollWheel &&
            GUIUtility.hotControl == controlId &&
            _activeInteractor != null)
        {
            _activeInteractor.AdjustGrabDepth(-current.delta.y);
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            _activeInteractor.UpdateGrab(ray, sceneView.camera);
            current.Use();
            sceneView.Repaint();
        }
        else if ((eventType == EventType.MouseUp || eventType == EventType.Ignore) &&
            GUIUtility.hotControl == controlId)
        {
            if (_activeInteractor != null)
                _activeInteractor.EndGrab();

            _activeInteractor = null;
            GUIUtility.hotControl = 0;
            current.Use();
            sceneView.Repaint();
        }

        DrawActiveGrab();
    }

    private static RopeGrabInteractor FindInteractorHit(Ray ray, Camera camera)
    {
        RopeGrabInteractor[] interactors =
            Object.FindObjectsByType<RopeGrabInteractor>();

        for (int i = 0; i < interactors.Length; i++)
        {
            RopeGrabInteractor interactor = interactors[i];
            if (interactor != null && interactor.TryBeginGrab(ray, camera))
                return interactor;
        }

        return null;
    }

    private static void DrawActiveGrab()
    {
        if (_activeInteractor == null || !_activeInteractor.IsDragging)
            return;

        RopeSystem rope = _activeInteractor.GetComponent<RopeSystem>();
        if (rope == null || !rope.IsGrabActive)
            return;

        Vector3 target = rope.GetGrabTargetPosition();
        Handles.color = new Color(1.0f, 0.72f, 0.15f, 1.0f);
        float size = HandleUtility.GetHandleSize(target) * 0.08f;
        Handles.SphereHandleCap(0, target, Quaternion.identity, size, EventType.Repaint);
    }
}
