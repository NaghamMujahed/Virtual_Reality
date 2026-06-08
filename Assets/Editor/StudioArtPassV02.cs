using UnityEngine;
using UnityEditor;

public static class StudioArtPassV02
{
    private static Material matRing;
    private static Material matMetal;
    private static Material matWarmLight;
    private static Material matPaintRed;
    private static Material matPaintBlue;
    private static Material matPaintYellow;
    private static Material matPaintGreen;
    private static Material matPaintWhite;
    private static Material matWallArt;
    private static Material matDarkPanel;
    private static Material matTape;

    [MenuItem("Tools/VR Pendulum/Apply Studio Art Pass V02")]
    public static void ApplyArtPass()
    {
        GameObject root = GameObject.Find("Environment_Studio");

        if (root == null)
        {
            Debug.LogError("Environment_Studio not found. Please run Build Studio Blockout first.");
            return;
        }

        CreateMaterials();

        Transform pendulumArea = FindOrCreate(root.transform, "01_Pendulum_Area");
        Transform props = FindOrCreate(root.transform, "04_Props_Decoration");
        Transform lighting = FindOrCreate(root.transform, "05_Lighting");
        Transform room = FindOrCreate(root.transform, "00_Room_Structure");
        Transform observation = FindOrCreate(root.transform, "03_Observation_Area");

        ReplaceSafeZoneDisk(pendulumArea);
        ImproveCeilingRig(pendulumArea);
        AddFloorCompositionMarks(pendulumArea);
        AddPaintSplatters(props);
        AddWallArtPanels(props);
        ImproveBackInfoBoard(observation);
        AddStudioLightPanels(lighting);
        AddRoomAccentPanels(room);
        AddMorePaintProps(props);

        Debug.Log("Studio Art Pass V02 applied successfully.");
    }

    private static Transform FindOrCreate(Transform parent, string name)
    {
        Transform child = parent.Find(name);

        if (child != null)
        {
            return child;
        }

        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent);
        obj.transform.localPosition = Vector3.zero;
        return obj.transform;
    }

    private static void CreateMaterials()
    {
        matRing = CreateMat("MAT_Safe_Zone_Ring_Subtle", new Color(0.05f, 0.55f, 0.95f, 0.55f));
        MakeTransparent(matRing, 0.55f);

        matMetal = CreateMat("MAT_Deep_Black_Metal", new Color(0.025f, 0.025f, 0.025f));
        matWarmLight = CreateMat("MAT_Warm_Light_Panel", new Color(1.0f, 0.88f, 0.55f));
        matPaintRed = CreateMat("MAT_Splatter_Red", new Color(0.85f, 0.04f, 0.03f, 0.75f));
        matPaintBlue = CreateMat("MAT_Splatter_Blue", new Color(0.03f, 0.20f, 0.9f, 0.75f));
        matPaintYellow = CreateMat("MAT_Splatter_Yellow", new Color(1.0f, 0.78f, 0.02f, 0.75f));
        matPaintGreen = CreateMat("MAT_Splatter_Green", new Color(0.02f, 0.62f, 0.18f, 0.75f));
        matPaintWhite = CreateMat("MAT_Splatter_White", new Color(0.95f, 0.90f, 0.80f, 0.75f));
        matWallArt = CreateMat("MAT_Previous_Canvas_Abstract", new Color(0.78f, 0.70f, 0.55f));
        matDarkPanel = CreateMat("MAT_Dark_Display_Panel", new Color(0.01f, 0.012f, 0.014f));
        matTape = CreateMat("MAT_Floor_Measure_Tape", new Color(1.0f, 0.78f, 0.08f));
    }

    private static Material CreateMat(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material mat = new Material(shader);
        mat.name = name;
        mat.color = color;

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }

        return mat;
    }

    private static void MakeTransparent(Material mat, float alpha)
    {
        if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor");
            c.a = alpha;
            mat.SetColor("_BaseColor", c);
        }

        if (mat.HasProperty("_Color"))
        {
            Color c = mat.GetColor("_Color");
            c.a = alpha;
            mat.SetColor("_Color", c);
        }

        mat.SetFloat("_Surface", 1);
        mat.renderQueue = 3000;
    }

    private static void ReplaceSafeZoneDisk(Transform parent)
    {
        Transform oldDisk = parent.Find("Pendulum_Safe_Zone_Radius_3m");
        if (oldDisk != null)
        {
            Object.DestroyImmediate(oldDisk.gameObject);
        }

        GameObject ringGroup = new GameObject("Pendulum_Safe_Zone_Ring_Debug");
        ringGroup.transform.SetParent(parent);
        ringGroup.transform.position = Vector3.zero;

        int segments = 72;
        float radius = 3.0f;
        float segmentLength = 0.26f;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;

            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = "Safe_Ring_Segment_" + i.ToString("00");
            segment.transform.SetParent(ringGroup.transform);
            segment.transform.position = new Vector3(x, 0.018f, z);
            segment.transform.rotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
            segment.transform.localScale = new Vector3(segmentLength, 0.015f, 0.035f);

            ApplyMaterial(segment, matRing);
            segment.GetComponent<Collider>().enabled = false;
        }
    }

    private static void ImproveCeilingRig(Transform parent)
    {
        GameObject rig = new GameObject("Ceiling_Heavy_Duty_Anchor_Rig_V02");
        rig.transform.SetParent(parent);

        CreateCube(rig.transform, "Rig_Main_Plate", new Vector3(0, 4.91f, 0), new Vector3(1.2f, 0.08f, 1.2f), matMetal);
        CreateCube(rig.transform, "Rig_Cross_Beam_X", new Vector3(0, 4.78f, 0), new Vector3(2.4f, 0.09f, 0.16f), matMetal);
        CreateCube(rig.transform, "Rig_Cross_Beam_Z", new Vector3(0, 4.76f, 0), new Vector3(0.16f, 0.09f, 2.4f), matMetal);

        GameObject hook = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hook.name = "Anchor_Hook_Cylinder";
        hook.transform.SetParent(rig.transform);
        hook.transform.position = new Vector3(0, 4.52f, 0);
        hook.transform.localScale = new Vector3(0.07f, 0.18f, 0.07f);
        ApplyMaterial(hook, matMetal);
    }

    private static void AddFloorCompositionMarks(Transform parent)
    {
        GameObject group = new GameObject("Floor_Composition_Marks");
        group.transform.SetParent(parent);

        // Yellow tape marks around canvas
        CreateCube(group.transform, "Tape_Front", new Vector3(0, 0.022f, -2.45f), new Vector3(4.8f, 0.012f, 0.04f), matTape);
        CreateCube(group.transform, "Tape_Back", new Vector3(0, 0.022f, 2.45f), new Vector3(4.8f, 0.012f, 0.04f), matTape);
        CreateCube(group.transform, "Tape_Left", new Vector3(-2.45f, 0.022f, 0), new Vector3(0.04f, 0.012f, 4.8f), matTape);
        CreateCube(group.transform, "Tape_Right", new Vector3(2.45f, 0.022f, 0), new Vector3(0.04f, 0.012f, 4.8f), matTape);

        // Center crosshair
        CreateCube(group.transform, "Canvas_Center_Mark_X", new Vector3(0, 0.13f, 0), new Vector3(0.55f, 0.015f, 0.035f), matTape);
        CreateCube(group.transform, "Canvas_Center_Mark_Z", new Vector3(0, 0.13f, 0), new Vector3(0.035f, 0.015f, 0.55f), matTape);
    }

    private static void AddPaintSplatters(Transform parent)
    {
        GameObject group = new GameObject("Paint_Splatter_Decals_Blockout");
        group.transform.SetParent(parent);

        CreateFlatSplatter(group.transform, "Blue_Splatter_01", new Vector3(-1.8f, 0.025f, 1.6f), 0.55f, matPaintBlue);
        CreateFlatSplatter(group.transform, "Red_Splatter_01", new Vector3(1.6f, 0.026f, -1.3f), 0.42f, matPaintRed);
        CreateFlatSplatter(group.transform, "Yellow_Splatter_01", new Vector3(2.4f, 0.027f, 1.0f), 0.35f, matPaintYellow);
        CreateFlatSplatter(group.transform, "Green_Splatter_01", new Vector3(-2.2f, 0.028f, -1.7f), 0.38f, matPaintGreen);
        CreateFlatSplatter(group.transform, "White_Splatter_01", new Vector3(0.7f, 0.029f, 2.6f), 0.30f, matPaintWhite);

        // Small random-looking dots around central zone
        Material[] mats = { matPaintRed, matPaintBlue, matPaintYellow, matPaintGreen };
        Vector3[] positions =
        {
            new Vector3(-2.8f, 0.03f, 0.8f),
            new Vector3(2.7f, 0.03f, -0.6f),
            new Vector3(1.2f, 0.03f, 2.8f),
            new Vector3(-1.1f, 0.03f, -2.6f),
            new Vector3(0.2f, 0.03f, 3.1f),
            new Vector3(-3.0f, 0.03f, -0.9f)
        };

        for (int i = 0; i < positions.Length; i++)
        {
            CreateFlatSplatter(group.transform, "Small_Paint_Dot_" + i, positions[i], 0.13f, mats[i % mats.Length]);
        }
    }

    private static void CreateFlatSplatter(Transform parent, string name, Vector3 position, float size, Material mat)
    {
        GameObject splatter = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        splatter.name = name;
        splatter.transform.SetParent(parent);
        splatter.transform.position = position;
        splatter.transform.rotation = Quaternion.identity;
        splatter.transform.localScale = new Vector3(size, 0.006f, size);

        ApplyMaterial(splatter, mat);
        splatter.GetComponent<Collider>().enabled = false;
    }

    private static void AddWallArtPanels(Transform parent)
    {
        GameObject group = new GameObject("Previous_Pendulum_Art_On_Walls");
        group.transform.SetParent(parent);

        CreateCube(group.transform, "Back_Wall_Art_Large", new Vector3(2.8f, 2.55f, 5.91f), new Vector3(1.6f, 1.15f, 0.05f), matWallArt);
        CreateCube(group.transform, "Back_Wall_Art_Frame", new Vector3(2.8f, 2.55f, 5.875f), new Vector3(1.75f, 1.30f, 0.035f), matMetal);

        CreateCube(group.transform, "Left_Wall_Art_01", new Vector3(-5.0f, 2.45f, 2.4f), new Vector3(0.05f, 1.1f, 1.55f), matWallArt);
        CreateCube(group.transform, "Left_Wall_Art_Frame_01", new Vector3(-4.965f, 2.45f, 2.4f), new Vector3(0.04f, 1.25f, 1.70f), matMetal);
    }

    private static void ImproveBackInfoBoard(Transform parent)
    {
        Transform oldBoard = parent.Find("Info_Board_Back_Wall");
        if (oldBoard != null)
        {
            ApplyMaterial(oldBoard.gameObject, matDarkPanel);
        }

        GameObject labels = new GameObject("Info_Board_UI_Blockout_Lines");
        labels.transform.SetParent(parent);

        CreateCube(labels.transform, "Info_Line_01", new Vector3(-3.0f, 2.85f, 5.86f), new Vector3(1.55f, 0.035f, 0.025f), matRing);
        CreateCube(labels.transform, "Info_Line_02", new Vector3(-3.0f, 2.65f, 5.86f), new Vector3(1.25f, 0.035f, 0.025f), matTape);
        CreateCube(labels.transform, "Info_Line_03", new Vector3(-3.0f, 2.45f, 5.86f), new Vector3(1.45f, 0.035f, 0.025f), matRing);
        CreateCube(labels.transform, "Info_Line_04", new Vector3(-3.0f, 2.25f, 5.86f), new Vector3(0.95f, 0.035f, 0.025f), matTape);
    }

    private static void AddStudioLightPanels(Transform parent)
    {
        GameObject panels = new GameObject("Visible_Ceiling_Light_Panels");
        panels.transform.SetParent(parent);

        CreateCube(panels.transform, "Light_Panel_Center", new Vector3(0, 4.86f, -2.4f), new Vector3(2.2f, 0.035f, 0.55f), matWarmLight);
        CreateCube(panels.transform, "Light_Panel_Left", new Vector3(-3.2f, 4.86f, 1.4f), new Vector3(1.5f, 0.035f, 0.45f), matWarmLight);
        CreateCube(panels.transform, "Light_Panel_Right", new Vector3(3.2f, 4.86f, 1.4f), new Vector3(1.5f, 0.035f, 0.45f), matWarmLight);

        GameObject centerAreaLight = new GameObject("Extra_Warm_Center_Point_Light");
        centerAreaLight.transform.SetParent(parent);
        centerAreaLight.transform.position = new Vector3(0, 4.2f, -1.2f);

        Light l = centerAreaLight.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 7f;
        l.intensity = 1.2f;
        l.color = new Color(1.0f, 0.82f, 0.62f);
    }

    private static void AddRoomAccentPanels(Transform parent)
    {
        GameObject group = new GameObject("Room_Accent_Panels");
        group.transform.SetParent(parent);

        CreateCube(group.transform, "Back_Wall_Wide_Accent_Panel", new Vector3(0, 1.15f, 5.90f), new Vector3(8.4f, 0.09f, 0.035f), matMetal);
        CreateCube(group.transform, "Right_Wall_Lower_Accent_Panel", new Vector3(4.96f, 1.15f, 1.2f), new Vector3(0.035f, 0.09f, 5.0f), matMetal);
        CreateCube(group.transform, "Left_Wall_Lower_Accent_Panel", new Vector3(-4.96f, 1.15f, 1.2f), new Vector3(0.035f, 0.09f, 5.0f), matMetal);
    }

    private static void AddMorePaintProps(Transform parent)
    {
        GameObject group = new GameObject("Additional_Studio_Props_Blockout");
        group.transform.SetParent(parent);

        // Buckets near work area
        CreateBucketProxy(group.transform, "Empty_Bucket_Proxy_01", new Vector3(-3.9f, 0.22f, -1.9f), matMetal);
        CreateBucketProxy(group.transform, "Paint_Bucket_Proxy_02", new Vector3(-4.45f, 0.22f, -1.45f), matPaintBlue);

        // Rolled canvas cylinders
        CreateCylinder(group.transform, "Rolled_Canvas_01", new Vector3(3.7f, 0.18f, 4.8f), new Vector3(0.16f, 0.7f, 0.16f), matWallArt).transform.rotation = Quaternion.Euler(90, 0, 0);
        CreateCylinder(group.transform, "Rolled_Canvas_02", new Vector3(4.2f, 0.18f, 4.55f), new Vector3(0.13f, 0.55f, 0.13f), matWallArt).transform.rotation = Quaternion.Euler(90, 0, 0);
    }

    private static void CreateBucketProxy(Transform parent, string name, Vector3 position, Material mat)
    {
        GameObject bucket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bucket.name = name;
        bucket.transform.SetParent(parent);
        bucket.transform.position = position;
        bucket.transform.localScale = new Vector3(0.28f, 0.22f, 0.28f);
        ApplyMaterial(bucket, mat);
    }

    private static GameObject CreateCube(Transform parent, string name, Vector3 position, Vector3 scale, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        ApplyMaterial(obj, mat);

        Collider col = obj.GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }

        return obj;
    }

    private static GameObject CreateCylinder(Transform parent, string name, Vector3 position, Vector3 scale, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        ApplyMaterial(obj, mat);

        Collider col = obj.GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }

        return obj;
    }

    private static void ApplyMaterial(GameObject obj, Material mat)
    {
        Renderer r = obj.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
        }
    }
}