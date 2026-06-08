using System;
using UnityEngine;

namespace PaintBucketSim.Configs
{
    public enum BucketShapeType
    {
        Cylinder = 0,
        TaperedCylinder = 1,
        CustomMeshVisualOnly = 2
    }

    public enum BucketMotionMode
    {
        LockedInitialPose = 0,
        DynamicFree = 1,
        KinematicFollowTransform = 2
    }

    public enum BucketHoleShape
    {
        Circular = 0,
        Square = 1
    }

    [Serializable]
    public class BucketHoleConfig
    {
        public string name = "Bottom Hole";

        public BucketHoleShape shape = BucketHoleShape.Circular;

        [Tooltip("If true, the hole is automatically placed at the bottom center of the bucket.")]
        public bool autoPlaceAtBottomCenter = true;

        public Vector3 localCenter = new Vector3(0.0f, -0.5f, 0.0f);
        public Vector3 localNormal = Vector3.down;

        [Min(0.001f)]
        public float radiusMeters = 0.035f;

        [Min(0.0001f)]
        public float wallThicknessMeters = 0.01f;

        public bool active = true;
    }

    [CreateAssetMenu(
        fileName = "BucketConfig",
        menuName = "Paint Bucket Sim/Bucket Config")]
    public class BucketConfig : ScriptableObject
    {
        [Header("Shape")]
        public BucketShapeType shapeType = BucketShapeType.Cylinder;

        [Min(0.05f)]
        public float heightMeters = 0.55f;

        [Min(0.02f)]
        public float topRadiusMeters = 0.18f;

        [Min(0.02f)]
        public float bottomRadiusMeters = 0.18f;

        [Min(0.001f)]
        public float wallThicknessMeters = 0.01f;

        [Header("Mass / Inertia")]
        [Min(0.01f)]
        public float massKg = 0.7f;

        public bool automaticInertiaTensor = true;

        [Tooltip("Used only if automaticInertiaTensor is false.")]
        public Vector3 manualInertiaTensor = new Vector3(0.025f, 0.02f, 0.025f);

        [Header("Motion")]
        public BucketMotionMode motionMode = BucketMotionMode.LockedInitialPose;

        [Tooltip("In DynamicFree mode, gravity can be applied. Keep false until rope-bucket coupling is active.")]
        public bool applyGravityInDynamicMode = false;

        [Range(0.0f, 10.0f)]
        public float linearDampingPerSecond = 0.15f;

        [Range(0.0f, 10.0f)]
        public float angularDampingPerSecond = 0.15f;

        [Header("Attachment Joint")]
        [Tooltip("If true, attachment point is placed above the top center.")]
        public bool autoPlaceAttachmentTopCenter = true;

        [Min(0.0f)]
        public float handleHeightMeters = 0.08f;

        public Vector3 localAttachmentPoint = new Vector3(0.0f, 0.35f, 0.0f);

        [Min(0.005f)]
        public float jointVisualRadiusMeters = 0.035f;

        [Header("Holes")]
        public BucketHoleConfig[] holes =
        {
            new BucketHoleConfig()
        };

        [Header("Rendering")]
        [Range(8, 96)]
        public int visualRadialSegments = 48;

        public Color bucketColor = new Color(0.55f, 0.55f, 0.58f);
        public Color holeColor = new Color(0.1f, 0.1f, 0.1f);
        public Color jointColor = new Color(1.0f, 0.75f, 0.25f);

        private void OnValidate()
        {
            if (heightMeters < 0.05f)
                heightMeters = 0.05f;

            if (topRadiusMeters < 0.02f)
                topRadiusMeters = 0.02f;

            if (bottomRadiusMeters < 0.02f)
                bottomRadiusMeters = 0.02f;

            if (shapeType == BucketShapeType.Cylinder)
                bottomRadiusMeters = topRadiusMeters;

            if (massKg < 0.01f)
                massKg = 0.01f;

            if (visualRadialSegments < 8)
                visualRadialSegments = 8;

            if (holes == null || holes.Length == 0)
                holes = new[] { new BucketHoleConfig() };

            for (int i = 0; i < holes.Length; i++)
            {
                if (holes[i] == null)
                    holes[i] = new BucketHoleConfig();

                if (holes[i].radiusMeters < 0.001f)
                    holes[i].radiusMeters = 0.001f;

                if (holes[i].localNormal.sqrMagnitude < 1e-6f)
                    holes[i].localNormal = Vector3.down;
            }
        }

        public Vector3 GetResolvedAttachmentLocalPoint()
        {
            if (!autoPlaceAttachmentTopCenter)
                return localAttachmentPoint;

            return new Vector3(0.0f, heightMeters * 0.5f + handleHeightMeters, 0.0f);
        }

        public Vector3 GetResolvedHoleLocalCenter(BucketHoleConfig hole)
        {
            if (hole == null)
                return new Vector3(0.0f, -heightMeters * 0.5f, 0.0f);

            if (!hole.autoPlaceAtBottomCenter)
                return hole.localCenter;

            return new Vector3(0.0f, -heightMeters * 0.5f, 0.0f);
        }

        public Vector3 GetResolvedHoleLocalNormal(BucketHoleConfig hole)
        {
            if (hole == null)
                return Vector3.down;

            Vector3 n = hole.autoPlaceAtBottomCenter ? Vector3.down : hole.localNormal;

            if (n.sqrMagnitude < 1e-6f)
                n = Vector3.down;

            return n.normalized;
        }

        public float GetRepresentativeRadius()
        {
            return 0.5f * (topRadiusMeters + bottomRadiusMeters);
        }
    }
}