using UnityEngine;
using UnityEditor;

public static class StudioPolishV03
{
    private static Material matWallMain;
    private static Material matWallSide;
    private static Material matFloor;
    private static Material matCeiling;
    private static Material matMetal;
    private static Material matWood;
    private static Material matCanvas;
    private static Material matSoftGuideBlue;
    private static Material matSoftGuideYellow;
    private static Material matPanelDark;
    private static Material matWarmAccent;
    private static Material matCoolAccent;
    private static Material matArtLight;
    private static Material matPaintMutedRed;
    private static Material matPaintMutedBlue;
    private static Material matPaintMutedGreen;
    private static Material matPaintMutedYellow;
    private static Material matPaintMutedWhite;

    [MenuItem("Tools/VR Pendulum/Apply Studio Polish V03")]
    public static void ApplyStudioPolish()
    {
        GameObject root = GameObject.Find("Environment_Studio");

        if (root == null)
        {
            Debug.LogError("Environment_Studio not found. Build the studio first.");
            return;
        }

        CreateMaterials();

        ApplyRoomMaterials(root.transform);
        SoftenGuideMarks(root.transform);
        ImproveShelvesAndTable(root.transform);
        ImproveLighting(root.transform);
        AddWallLayering(root.transform);
        AddAtmosphereProps(root.transform);
        ImproveCameras();

        Debug.Log("Studio Polish V03 applied successfully.");
    }

    private static void CreateMaterials()
    {
        matWallMain = CreateMat("MAT_V03_Wall_Main", new Color(0.80f, 0.78f, 0.73f));
        matWallSide = CreateMat("MAT_V03_Wall_Side", new Color(0.72f, 0.74f, 0.76f));
        matFloor = CreateMat("MAT_V03_Floor", new Color(0.38f, 0.39f, 0.40f));
        matCeiling = CreateMat("MAT_V03_Ceiling", new Color(0.71f, 0.70f, 0.66f));
        matMetal = CreateMat("MAT_V03_Metal", new Color(0.06f, 0.06f, 0.065f));
        matWood = CreateMat("MAT_V03_Wood", new Color(0.42f, 0.25f, 0.15f));
        matCanvas = CreateMat("MAT_V03_Canvas", new Color(0.86f, 0.80f, 0.70f));

        matSoftGuideBlue = CreateMat("MAT_V03_Guide_Blue", new Color(0.18f, 0.58f, 0.95f, 0.28f));
        MakeTransparent(matSoftGuideBlue, 0.28f);

        matSoftGuideYellow = CreateMat("MAT_V03_Guide_Yellow", new Color(0.95f, 0.72f, 0.14f, 0.38f));
        MakeTransparent(matSoftGuideYellow, 0.38f);

        matPanelDark = CreateMat("MAT_V03_Panel_Dark", new Color(0.10f, 0.10f, 0.11f));
        matWarmAccent = CreateMat("MAT_V03_Warm_Accent", new Color(0.95f, 0.84f, 0.62f));
        matCoolAccent = CreateMat("MAT_V03_Cool_Accent", new Color(0.65f, 0.75f, 0.88f));
        matArtLight = CreateMat("MAT_V03_Art_Light", new Color(1.0f, 0.90f, 0.72f));

        matPaintMutedRed = CreateMat("MAT_V03_Paint_Red", new Color(0.72f, 0.10f, 0.09f, 0.58f));
        MakeTransparent(matPaintMutedRed, 0.58f);

        matPaintMutedBlue = CreateMat("MAT_V03_Paint_Blue", new Color(0.10f, 0.28f, 0.78f, 0.58f));
        MakeTransparent(matPaintMutedBlue, 0.58f);

        matPaintMutedGreen = CreateMat("MAT_V03_Paint_Green", new Color(0.12f, 0.52f, 0.20f, 0.58f));
        MakeTransparent(matPaintMutedGreen, 0.58f);

        matPaintMutedYellow = CreateMat("MAT_V03_Paint_Yellow", new Color(0.88f, 0.72f, 0.12f, 0.58f));
        MakeTransparent(matPaintMutedYellow, 0.58f);

        matPaintMutedWhite = CreateMat("MAT_V03_Paint_White", new Color(0.90f, 0.88f, 0.80f, 0.50f));
        MakeTransparent(matPaintMutedWhite, 0.50f);
    }

    private static Material CreateMat(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.name = name;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

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

    private static void ApplyRoomMaterials(Transform root)
    {
        SetMaterialIfExists(root, "Floor_Concrete", matFloor);
        SetMaterialIfExists(root, "Ceiling", matCeiling);

        SetMaterialIfExists(root, "Back_Wall", matWallMain);
        SetMaterialIfExists(root, "Left_Wall", matWallMain);
        SetMaterialIfExists(root, "Right_Wall", matWallSide);

        SetMaterialIfExists(root, "Canvas_Platform_4x4", matCanvas);

        SetMaterialIfExists(root, "Canvas_Border_Front", matMetal);
        SetMaterialIfExists(root, "Canvas_Border_Back", matMetal);
        SetMaterialIfExists(root, "Canvas_Border_Left", matMetal);
        SetMaterialIfExists(root, "Canvas_Border_Right", matMetal);

        SetMaterialIfExists(root, "Work_Table_Top", matWood);
        SetMaterialIfExists(root, "Work_Table_Leg_FL", matMetal);
        SetMaterialIfExists(root, "Work_Table_Leg_FR", matMetal);
        SetMaterialIfExists(root, "Work_Table_Leg_BL", matMetal);
        SetMaterialIfExists(root, "Work_Table_Leg_BR", matMetal);

        SetMaterialIfExists(root, "Paint_Shelf_Level_1", matWood);
        SetMaterialIfExists(root, "Paint_Shelf_Level_2", matWood);
        SetMaterialIfExists(root, "Paint_Shelf_Level_3", matWood);
        SetMaterialIfExists(root, "Paint_Shelf_Back_Panel", matMetal);
    }

    private static void SoftenGuideMarks(Transform root)
    {
        Transform guideGroup = FindDeepChild(root, "Floor_Composition_Marks");
        if (guideGroup != null)
        {
            foreach (Transform child in guideGroup)
            {
                if (child.name.Contains("Tape"))
                    ApplyMat(child.gameObject, matSoftGuideYellow);
                else
                    ApplyMat(child.gameObject, matSoftGuideYellow);
            }
        }

        Transform ringGroup = FindDeepChild(root, "Pendulum_Safe_Zone_Ring_Debug");
        if (ringGroup != null)
        {
            foreach (Transform child in ringGroup)
            {
                ApplyMat(child.gameObject, matSoftGuideBlue);
            }
        }

        Transform splatters = FindDeepChild(root, "Paint_Splatter_Decals_Blockout");
        if (splatters != null)
        {
            foreach (Transform child in splatters)
            {
                if (child.name.Contains("Red")) ApplyMat(child.gameObject, matPaintMutedRed);
                else if (child.name.Contains("Blue")) ApplyMat(child.gameObject, matPaintMutedBlue);
                else if (child.name.Contains("Green")) ApplyMat(child.gameObject, matPaintMutedGreen);
                else if (child.name.Contains("Yellow")) ApplyMat(child.gameObject, matPaintMutedYellow);
                else if (child.name.Contains("White")) ApplyMat(child.gameObject, matPaintMutedWhite);
            }
        }
    }

    private static void ImproveShelvesAndTable(Transform root)
    {
        Transform paintArea = FindDeepChild(root, "02_Paint_Work_Area");
        if (paintArea == null) return;

        // add small back shelf support bars
        CreateCube(paintArea, "Shelf_Support_Left", new Vector3(3.82f, 1.48f, -2.2f), new Vector3(0.05f, 1.6f, 0.05f), matMetal);
        CreateCube(paintArea, "Shelf_Support_Right", new Vector3(4.67f, 1.48f, -2.2f), new Vector3(0.05f, 1.6f, 0.05f), matMetal);

        // tabletop props
        CreateCylinder(paintArea, "Table_Paint_Cup_01", new Vector3(-3.85f, 1.02f, -3.10f), new Vector3(0.10f, 0.12f, 0.10f), matCoolAccent);
        CreateCylinder(paintArea, "Table_Paint_Cup_02", new Vector3(-3.45f, 1.02f, -3.35f), new Vector3(0.09f, 0.10f, 0.09f), matWarmAccent);

        CreateCube(paintArea, "Brush_Box", new Vector3(-3.15f, 1.00f, -3.05f), new Vector3(0.28f, 0.14f, 0.18f), matPanelDark);
    }

    private static void ImproveLighting(Transform root)
    {
        Transform lighting = FindDeepChild(root, "05_Lighting");
        if (lighting == null) return;

        // soften or retune existing lights
        SetLightValues(lighting, "Directional_Light_Soft", 0.95f, new Color(1.0f, 0.95f, 0.88f));
        SetLightValues(lighting, "Pendulum_Center_Spot_Light", 4.6f, new Color(1.0f, 0.90f, 0.76f));
        SetLightValues(lighting, "Soft_Fill_Light", 0.70f, new Color(0.70f, 0.80f, 0.92f));
        SetLightValues(lighting, "Extra_Warm_Center_Point_Light", 1.0f, new Color(1.0f, 0.84f, 0.68f));

        // add side fill lights
        CreatePointLight(lighting, "Cool_Fill_Right", new Vector3(4.2f, 2.7f, -2.4f), new Color(0.66f, 0.76f, 0.95f), 0.45f, 8f);
        CreatePointLight(lighting, "Warm_Back_Glow", new Vector3(0, 2.5f, 5.0f), new Color(1.0f, 0.82f, 0.60f), 0.35f, 6f);

        // visible art lights on back wall
        Transform props = FindDeepChild(root, "04_Props_Decoration");
        if (props != null)
        {
            CreateCube(props, "Art_Light_Bar_Back_01", new Vector3(2.8f, 3.25f, 5.84f), new Vector3(1.2f, 0.05f, 0.08f), matArtLight);
            CreateCube(props, "Art_Light_Bar_Back_02", new Vector3(-3.0f, 3.20f, 5.84f), new Vector3(1.4f, 0.05f, 0.08f), matArtLight);
        }
    }

    private static void AddWallLayering(Transform root)
    {
        Transform room = FindDeepChild(root, "00_Room_Structure");
        if (room == null) return;

        CreateCube(room, "Back_Wall_Panel_Left", new Vector3(-3.8f, 2.4f, 6.00f), new Vector3(1.45f, 2.2f, 0.02f), matPanelDark);
        CreateCube(room, "Back_Wall_Panel_Right", new Vector3(3.8f, 2.4f, 6.00f), new Vector3(1.45f, 2.2f, 0.02f), matPanelDark);

        CreateCube(room, "Right_Wall_Vertical_Accent", new Vector3(5.00f, 2.2f, -3.8f), new Vector3(0.02f, 2.8f, 1.2f), matPanelDark);
        CreateCube(room, "Left_Wall_Vertical_Accent", new Vector3(-5.00f, 2.2f, -3.2f), new Vector3(0.02f, 2.6f, 1.0f), matPanelDark);
    }

    private static void AddAtmosphereProps(Transform root)
    {
        Transform props = FindDeepChild(root, "04_Props_Decoration");
        if (props == null) return;

        // add two more previous art boards
        CreateCube(props, "Old_Canvas_Leaning_03", new Vector3(4.55f, 0.85f, 5.66f), new Vector3(0.75f, 1.45f, 0.05f), matCanvas);
        CreateCube(props, "Old_Canvas_Leaning_04", new Vector3(2.2f, 0.78f, 5.68f), new Vector3(0.72f, 1.20f, 0.05f), matCanvas);

        // floor rolled cloths
        CreateCylinder(props, "Floor_Cloth_Roll_01", new Vector3(-4.20f, 0.10f, 4.7f), new Vector3(0.16f, 0.28f, 0.16f), matCanvas)
            .transform.rotation = Quaternion.Euler(90, 0, 0);

        CreateCylinder(props, "Floor_Cloth_Roll_02", new Vector3(-3.75f, 0.10f, 4.45f), new Vector3(0.13f, 0.22f, 0.13f), matCanvas)
            .transform.rotation = Quaternion.Euler(90, 0, 0);

        // subtle side guide bench marker
        CreateCube(props, "Front_Floor_Accent", new Vector3(0, 0.012f, -5.0f), new Vector3(2.4f, 0.01f, 0.6f), matCoolAccent);
    }

    private static void ImproveCameras()
    {
        Camera main = Camera.main;
        if (main != null)
        {
            main.fieldOfView = 50f;
            main.backgroundColor = new Color(0.15f, 0.16f, 0.18f);
            main.clearFlags = CameraClearFlags.SolidColor;
        }
    }

    private static void SetLightValues(Transform root, string lightName, float intensity, Color color)
    {
        Transform t = FindDeepChild(root, lightName);
        if (t == null) return;

        Light l = t.GetComponent<Light>();
        if (l != null)
        {
            l.intensity = intensity;
            l.color = color;
        }
    }

    private static void CreatePointLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = pos;

        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
    }

    private static void SetMaterialIfExists(Transform root, string childName, Material mat)
    {
        Transform t = FindDeepChild(root, childName);
        if (t != null) ApplyMat(t.gameObject, mat);
    }

    private static void ApplyMat(GameObject obj, Material mat)
    {
        Renderer r = obj.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = mat;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;

            Transform found = FindDeepChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static GameObject CreateCube(Transform parent, string name, Vector3 position, Vector3 scale, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        ApplyMat(obj, mat);

        Collider c = obj.GetComponent<Collider>();
        if (c != null) c.enabled = false;
        return obj;
    }

    private static GameObject CreateCylinder(Transform parent, string name, Vector3 position, Vector3 scale, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        ApplyMat(obj, mat);

        Collider c = obj.GetComponent<Collider>();
        if (c != null) c.enabled = false;
        return obj;
    }
}