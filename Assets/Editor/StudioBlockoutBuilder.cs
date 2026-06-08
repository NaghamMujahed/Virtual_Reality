using UnityEngine;
using UnityEditor;

public static class StudioBlockoutBuilder
{
    private static Material wallMat;
    private static Material floorMat;
    private static Material ceilingMat;
    private static Material metalMat;
    private static Material canvasMat;
    private static Material woodMat;
    private static Material safeZoneMat;
    private static Material markerMat;
    private static Material redMat;
    private static Material blueMat;
    private static Material yellowMat;
    private static Material whiteMat;
    private static Material blackMat;
    private static Material greenMat;

    [MenuItem("Tools/VR Pendulum/Build Studio Blockout")]
    public static void BuildStudioBlockout()
    {
        ClearExisting();

        CreateMaterials();

        GameObject root = new GameObject("Environment_Studio");

        GameObject room = CreateGroup(root.transform, "00_Room_Structure");
        GameObject pendulum = CreateGroup(root.transform, "01_Pendulum_Area");
        GameObject paintArea = CreateGroup(root.transform, "02_Paint_Work_Area");
        GameObject observation = CreateGroup(root.transform, "03_Observation_Area");
        GameObject props = CreateGroup(root.transform, "04_Props_Decoration");
        GameObject lighting = CreateGroup(root.transform, "05_Lighting");
        GameObject cameras = CreateGroup(root.transform, "06_Cameras");

        BuildRoom(room.transform);
        BuildPendulumArea(pendulum.transform);
        BuildPaintWorkArea(paintArea.transform);
        BuildObservationArea(observation.transform);
        BuildBasicProps(props.transform);
        BuildLighting(lighting.transform);
        BuildCameras(cameras.transform);

        Selection.activeGameObject = root;

        Debug.Log("Studio Blockout Created Successfully.");
    }

    private static void ClearExisting()
    {
        GameObject existing = GameObject.Find("Environment_Studio");
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }
    }

    private static GameObject CreateGroup(Transform parent, string name)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent);
        group.transform.localPosition = Vector3.zero;
        return group;
    }

    private static void CreateMaterials()
    {
        wallMat = CreateMat("MAT_Warm_White_Walls", new Color(0.78f, 0.76f, 0.70f));
        floorMat = CreateMat("MAT_Light_Concrete_Floor", new Color(0.45f, 0.45f, 0.42f));
        ceilingMat = CreateMat("MAT_Ceiling_OffWhite", new Color(0.70f, 0.70f, 0.66f));
        metalMat = CreateMat("MAT_Dark_Metal", new Color(0.08f, 0.08f, 0.08f));
        canvasMat = CreateMat("MAT_Canvas_Warm", new Color(0.86f, 0.80f, 0.68f));
        woodMat = CreateMat("MAT_Wood_Blockout", new Color(0.45f, 0.27f, 0.12f));
        markerMat = CreateMat("MAT_Anchor_Marker", new Color(1.0f, 0.45f, 0.05f));

        redMat = CreateMat("MAT_Paint_Red", new Color(0.9f, 0.05f, 0.05f));
        blueMat = CreateMat("MAT_Paint_Blue", new Color(0.05f, 0.25f, 0.9f));
        yellowMat = CreateMat("MAT_Paint_Yellow", new Color(1.0f, 0.85f, 0.05f));
        whiteMat = CreateMat("MAT_Paint_White", new Color(0.9f, 0.9f, 0.85f));
        blackMat = CreateMat("MAT_Paint_Black", new Color(0.02f, 0.02f, 0.02f));
        greenMat = CreateMat("MAT_Paint_Green", new Color(0.05f, 0.65f, 0.18f));

        safeZoneMat = CreateMat("MAT_Safe_Zone_Transparent", new Color(0.2f, 0.7f, 1.0f, 0.18f));
        MakeTransparent(safeZoneMat);
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
        return mat;
    }

    private static void MakeTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1);
        mat.SetFloat("_Blend", 0);
        mat.SetFloat("_AlphaClip", 0);
        mat.renderQueue = 3000;

        if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor");
            c.a = 0.18f;
            mat.SetColor("_BaseColor", c);
        }

        if (mat.HasProperty("_Color"))
        {
            Color c = mat.GetColor("_Color");
            c.a = 0.18f;
            mat.SetColor("_Color", c);
        }
    }

    private static void BuildRoom(Transform parent)
    {
        // Room dimensions:
        // Width X = 10m, Length Z = 12m, Height Y = 5m

        CreateCube(parent, "Floor_Concrete", new Vector3(0, -0.05f, 0), new Vector3(10, 0.1f, 12), floorMat);

        CreateCube(parent, "Back_Wall", new Vector3(0, 2.5f, 6.05f), new Vector3(10, 5, 0.1f), wallMat);
        CreateCube(parent, "Left_Wall", new Vector3(-5.05f, 2.5f, 0), new Vector3(0.1f, 5, 12), wallMat);
        CreateCube(parent, "Right_Wall", new Vector3(5.05f, 2.5f, 0), new Vector3(0.1f, 5, 12), wallMat);

        // Keep front open for camera readability
        CreateCube(parent, "Ceiling", new Vector3(0, 5.05f, 0), new Vector3(10, 0.1f, 12), ceilingMat);

        // Industrial support beams
        CreateCube(parent, "Ceiling_Beam_X_Center", new Vector3(0, 4.82f, 0), new Vector3(8.5f, 0.12f, 0.18f), metalMat);
        CreateCube(parent, "Ceiling_Beam_Z_Center", new Vector3(0, 4.80f, 0), new Vector3(0.18f, 0.12f, 8.5f), metalMat);

        CreateCube(parent, "Back_Wall_Lower_Base", new Vector3(0, 0.25f, 5.92f), new Vector3(10, 0.5f, 0.12f), metalMat);
    }

    private static void BuildPendulumArea(Transform parent)
    {
        // Main canvas
        CreateCube(parent, "Canvas_Platform_4x4", new Vector3(0, 0.035f, 0), new Vector3(4.0f, 0.07f, 4.0f), canvasMat);

        // Slight border around canvas
        CreateCube(parent, "Canvas_Border_Front", new Vector3(0, 0.12f, -2.05f), new Vector3(4.2f, 0.12f, 0.08f), metalMat);
        CreateCube(parent, "Canvas_Border_Back", new Vector3(0, 0.12f, 2.05f), new Vector3(4.2f, 0.12f, 0.08f), metalMat);
        CreateCube(parent, "Canvas_Border_Left", new Vector3(-2.05f, 0.12f, 0), new Vector3(0.08f, 0.12f, 4.2f), metalMat);
        CreateCube(parent, "Canvas_Border_Right", new Vector3(2.05f, 0.12f, 0), new Vector3(0.08f, 0.12f, 4.2f), metalMat);

        // Transparent safe swing zone
        GameObject safeZone = CreateCylinder(parent, "Pendulum_Safe_Zone_Radius_3m", new Vector3(0, 0.01f, 0), new Vector3(6.0f, 0.01f, 6.0f), safeZoneMat);
        safeZone.GetComponent<Collider>().enabled = false;

        // Anchor frame under ceiling
        CreateCube(parent, "Anchor_Frame_Beam_X", new Vector3(0, 4.65f, 0), new Vector3(1.6f, 0.08f, 0.12f), metalMat);
        CreateCube(parent, "Anchor_Frame_Beam_Z", new Vector3(0, 4.63f, 0), new Vector3(0.12f, 0.08f, 1.6f), metalMat);

        // Anchor point marker
        GameObject anchor = CreateSphere(parent, "ANCHOR_POINT_Ceiling", new Vector3(0, 4.55f, 0), new Vector3(0.18f, 0.18f, 0.18f), markerMat);
        anchor.GetComponent<Collider>().enabled = false;

        // Bucket spawn marker
        GameObject bucketSpawn = CreateSphere(parent, "BUCKET_START_POSITION", new Vector3(0, 2.25f, 0), new Vector3(0.22f, 0.22f, 0.22f), blueMat);
        bucketSpawn.GetComponent<Collider>().enabled = false;

        // Debug rope path placeholder
        CreateCube(parent, "Rope_Debug_Line_Placeholder", new Vector3(0, 3.4f, 0), new Vector3(0.035f, 2.3f, 0.035f), metalMat);

        // Low splash guard around action zone
        CreateCube(parent, "Splash_Guard_Back", new Vector3(0, 0.35f, 3.25f), new Vector3(6.6f, 0.7f, 0.08f), metalMat);
        CreateCube(parent, "Splash_Guard_Left", new Vector3(-3.25f, 0.35f, 0), new Vector3(0.08f, 0.7f, 6.6f), metalMat);
        CreateCube(parent, "Splash_Guard_Right", new Vector3(3.25f, 0.35f, 0), new Vector3(0.08f, 0.7f, 6.6f), metalMat);
    }

    private static void BuildPaintWorkArea(Transform parent)
    {
        // Work table on left side
        CreateCube(parent, "Work_Table_Top", new Vector3(-3.6f, 0.9f, -3.2f), new Vector3(2.0f, 0.12f, 0.9f), woodMat);
        CreateCube(parent, "Work_Table_Leg_FL", new Vector3(-4.45f, 0.45f, -3.55f), new Vector3(0.12f, 0.9f, 0.12f), metalMat);
        CreateCube(parent, "Work_Table_Leg_FR", new Vector3(-2.75f, 0.45f, -3.55f), new Vector3(0.12f, 0.9f, 0.12f), metalMat);
        CreateCube(parent, "Work_Table_Leg_BL", new Vector3(-4.45f, 0.45f, -2.85f), new Vector3(0.12f, 0.9f, 0.12f), metalMat);
        CreateCube(parent, "Work_Table_Leg_BR", new Vector3(-2.75f, 0.45f, -2.85f), new Vector3(0.12f, 0.9f, 0.12f), metalMat);

        // Paint shelves on right wall
        CreateCube(parent, "Paint_Shelf_Back_Panel", new Vector3(4.75f, 1.6f, -2.2f), new Vector3(0.15f, 2.4f, 2.2f), metalMat);
        CreateCube(parent, "Paint_Shelf_Level_1", new Vector3(4.25f, 0.8f, -2.2f), new Vector3(0.9f, 0.08f, 2.2f), woodMat);
        CreateCube(parent, "Paint_Shelf_Level_2", new Vector3(4.25f, 1.45f, -2.2f), new Vector3(0.9f, 0.08f, 2.2f), woodMat);
        CreateCube(parent, "Paint_Shelf_Level_3", new Vector3(4.25f, 2.1f, -2.2f), new Vector3(0.9f, 0.08f, 2.2f), woodMat);

        // Paint cans
        Material[] mats = { redMat, blueMat, yellowMat, greenMat, whiteMat, blackMat };
        for (int i = 0; i < mats.Length; i++)
        {
            float z = -3.0f + i * 0.32f;
            CreateCylinder(parent, "Paint_Can_" + i, new Vector3(4.25f, 1.05f, z), new Vector3(0.22f, 0.25f, 0.22f), mats[i]);
        }
    }

    private static void BuildObservationArea(Transform parent)
    {
        // Monitor / info board on back wall
        CreateCube(parent, "Info_Board_Back_Wall", new Vector3(-3.0f, 2.5f, 5.92f), new Vector3(2.2f, 1.2f, 0.06f), blackMat);
        CreateCube(parent, "Info_Board_Frame", new Vector3(-3.0f, 2.5f, 5.88f), new Vector3(2.35f, 1.35f, 0.04f), metalMat);

        // Small observation platform marker
        CreateCube(parent, "Observation_Floor_Marker", new Vector3(0, 0.01f, -5.0f), new Vector3(3.0f, 0.02f, 0.8f), safeZoneMat);
    }

    private static void BuildBasicProps(Transform parent)
    {
        // Old canvases leaning on back wall
        CreateCube(parent, "Old_Canvas_Leaning_01", new Vector3(3.2f, 0.9f, 5.7f), new Vector3(1.0f, 1.6f, 0.06f), canvasMat);
        CreateCube(parent, "Old_Canvas_Leaning_02", new Vector3(4.0f, 0.75f, 5.65f), new Vector3(0.8f, 1.3f, 0.06f), canvasMat);

        // Simple cable / hose placeholder
        CreateCube(parent, "Cable_Hose_Placeholder", new Vector3(3.4f, 0.04f, -0.8f), new Vector3(0.08f, 0.08f, 2.2f), blackMat);
    }

    private static void BuildLighting(Transform parent)
    {
        GameObject directional = new GameObject("Directional_Light_Soft");
        directional.transform.SetParent(parent);
        Light dirLight = directional.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.intensity = 1.1f;
        dirLight.color = new Color(1.0f, 0.94f, 0.84f);
        directional.transform.rotation = Quaternion.Euler(50, -35, 0);

        GameObject spot = new GameObject("Pendulum_Center_Spot_Light");
        spot.transform.SetParent(parent);
        spot.transform.position = new Vector3(0, 4.6f, -1.8f);
        spot.transform.rotation = Quaternion.Euler(65, 0, 0);
        Light spotLight = spot.AddComponent<Light>();
        spotLight.type = LightType.Spot;
        spotLight.range = 8f;
        spotLight.spotAngle = 55f;
        spotLight.intensity = 3.5f;
        spotLight.color = new Color(1.0f, 0.92f, 0.82f);

        GameObject fill = new GameObject("Soft_Fill_Light");
        fill.transform.SetParent(parent);
        fill.transform.position = new Vector3(-4, 3, -4);
        Light fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Point;
        fillLight.range = 9f;
        fillLight.intensity = 0.9f;
        fillLight.color = new Color(0.65f, 0.78f, 1.0f);
    }

    private static void BuildCameras(Transform parent)
    {
        Camera mainCam = Camera.main;

        if (mainCam == null)
        {
            GameObject camObj = new GameObject("MainCamera");
            mainCam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
        }

        mainCam.transform.SetParent(parent);
        mainCam.name = "MainCamera_Wide_Studio_View";
        mainCam.transform.position = new Vector3(0, 3.0f, -8.5f);
        mainCam.transform.rotation = Quaternion.Euler(18f, 0, 0);
        mainCam.fieldOfView = 55f;

        CreateCamera(parent, "Camera_Top_View", new Vector3(0, 8.0f, 0), Quaternion.Euler(90, 0, 0), 60f);
        CreateCamera(parent, "Camera_Diagonal_Hero_View", new Vector3(6.5f, 3.2f, -6.5f), Quaternion.Euler(22f, -42f, 0), 50f);
    }

    private static void CreateCamera(Transform parent, string name, Vector3 position, Quaternion rotation, float fov)
    {
        GameObject camObj = new GameObject(name);
        camObj.transform.SetParent(parent);
        camObj.transform.position = position;
        camObj.transform.rotation = rotation;

        Camera cam = camObj.AddComponent<Camera>();
        cam.fieldOfView = fov;
        cam.enabled = false;
    }

    private static GameObject CreateCube(Transform parent, string name, Vector3 position, Vector3 scale, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        ApplyMaterial(obj, mat);
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
        return obj;
    }

    private static GameObject CreateSphere(Transform parent, string name, Vector3 position, Vector3 scale, Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.position = position;
        obj.transform.localScale = scale;
        ApplyMaterial(obj, mat);
        return obj;
    }

    private static void ApplyMaterial(GameObject obj, Material mat)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = mat;
        }
    }
}