#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;

// Tools 메뉴에서 클릭 한 번으로 NetworkManager 오브젝트를 조립한다.
// GUI를 직접 조작할 수 없는 상황에서, Unity 자신의 오브젝트 모델(AddComponent 등)을
// 통해 만들기 때문에 씬 파일을 손으로 편집하는 것보다 안전하다.
public static class NetworkSetupMenu
{
    private const string ServerAddress = "34.50.24.161";
    private const ushort ServerPort = 7777;
    private const string BotExePathKey = "Filerpark.BotClientExePath";

    // NGO 2.x의 NetworkManager Inspector에는 (1.x와 달리) Play 모드에서 자동으로
    // 뜨는 Start Host/Server/Client 버튼이 없다. 그 존재 여부를 더 알아보는 대신,
    // 여기 메뉴 항목으로 바로 대체한다 -- Play 모드에서만 활성화된다.
    [MenuItem("Tools/Coop Setup/Debug: Start Host (Play 모드 중에만)")]
    public static void DebugStartHost()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager.Singleton이 없습니다. Play 모드인지, 씬에 NetworkManager가 있는지 확인하세요.");
            return;
        }

        if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
        {
            Debug.LogWarning("이미 Host/Server/Client가 실행 중입니다.");
            return;
        }

        bool started = NetworkManager.Singleton.StartHost();
        Debug.Log(started ? "StartHost() 성공 -- 캐릭터가 스폰됐는지 Game 뷰를 확인하세요." : "StartHost() 실패.");
    }

    [MenuItem("Tools/Coop Setup/Debug: Start Host (Play 모드 중에만)", true)]
    public static bool ValidateDebugStartHost()
    {
        return EditorApplication.isPlaying;
    }

    [MenuItem("Tools/Coop Setup/1. Create NetworkManager")]
    public static void CreateNetworkManager()
    {
        GameObject existing = GameObject.Find("NetworkManager");
        if (existing != null)
        {
            Debug.LogWarning("NetworkManager 오브젝트가 이미 씬에 있어서 새로 만들지 않았습니다. 기존 오브젝트를 선택합니다.");
            Selection.activeGameObject = existing;
            return;
        }

        GameObject go = new GameObject("NetworkManager");

        NetworkManager networkManager = go.AddComponent<NetworkManager>();
        UnityTransport transport = go.AddComponent<UnityTransport>();

        transport.ConnectionData.Address = ServerAddress;
        transport.ConnectionData.Port = ServerPort;

        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.EnableSceneManagement = true;

        go.AddComponent<NetworkBootstrapper>();

        Undo.RegisterCreatedObjectUndo(go, "Create NetworkManager");
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"NetworkManager 생성 완료 -- UnityTransport {ServerAddress}:{ServerPort}, NetworkBootstrapper 부착됨. Ctrl+S로 씬 저장하세요.");
    }

    [MenuItem("Tools/Coop Setup/2. Create Player Prefab")]
    public static void CreatePlayerPrefab()
    {
        const string prefabFolder = "Assets/Prefabs";
        const string prefabPath = prefabFolder + "/Player.prefab";
        EnsureFolder(prefabFolder);

        Sprite placeholderSprite = GetOrCreatePlaceholderSprite();

        GameObject go = new GameObject("Player");
        go.tag = "Player";
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
        {
            go.layer = playerLayer;
        }

        SpriteRenderer spriteRenderer = go.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = placeholderSprite;

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.gravityScale = 3f;

        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.9f, 0.9f);

        go.AddComponent<NetworkObject>();
        go.AddComponent<NetworkTransform>();

        PlayerMovementNGO movement = go.AddComponent<PlayerMovementNGO>();
        go.AddComponent<PlayerSetupNGO>();
        go.AddComponent<PlayerColorNGO>();
        go.AddComponent<BotController>();

        GameObject groundCheck = new GameObject("groundCheck");
        groundCheck.transform.SetParent(go.transform);
        groundCheck.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        movement.groundCheck = groundCheck.transform;

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0)
        {
            movement.groundLayer = 1 << groundLayer;
        }

        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(go, prefabPath, out bool success);
        Object.DestroyImmediate(go);

        if (!success || prefabAsset == null)
        {
            Debug.LogError("Player 프리팹 저장에 실패했습니다.");
            return;
        }

        GameObject networkManagerObj = GameObject.Find("NetworkManager");
        if (networkManagerObj != null)
        {
            NetworkManager networkManager = networkManagerObj.GetComponent<NetworkManager>();
            if (networkManager != null)
            {
                networkManager.NetworkConfig.PlayerPrefab = prefabAsset;
                EditorUtility.SetDirty(networkManagerObj);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
        }

        EditorUtility.SetDirty(prefabAsset);
        AssetDatabase.SaveAssets();
        Selection.activeObject = prefabAsset;

        Debug.Log($"Player 프리팹 생성 완료: {prefabPath}. NetworkManager의 Player Prefab 슬롯에 자동 연결을 시도했습니다 -- Inspector에서 실제로 채워졌는지 꼭 확인해주세요.");
    }

    [MenuItem("Tools/Coop Setup/3. Setup Main Camera")]
    public static void SetupMainCamera()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("씬에 Main Camera가 없습니다. Hierarchy에 'MainCamera' 태그가 붙은 카메라가 있는지 확인하세요.");
            return;
        }

        mainCamera.orthographic = true;
        mainCamera.orthographicSize = 5f;

        GameObject camObj = mainCamera.gameObject;
        if (camObj.GetComponent<CoopCameraFollow>() == null)
        {
            camObj.AddComponent<CoopCameraFollow>();
        }

        EditorUtility.SetDirty(camObj);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("Main Camera를 Orthographic으로 바꾸고 CoopCameraFollow를 부착했습니다. Ctrl+S로 저장하세요.");
    }

    [MenuItem("Tools/Coop Setup/4. Create Test Ground")]
    public static void CreateTestGround()
    {
        if (GameObject.Find("TestGround") != null)
        {
            Debug.LogWarning("TestGround가 이미 씬에 있어서 새로 만들지 않았습니다.");
            return;
        }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
        {
            Debug.LogError("Ground 레이어가 없습니다. ProjectSettings/TagManager.asset을 확인하세요.");
            return;
        }

        GameObject ground = new GameObject("TestGround");
        ground.layer = groundLayer;
        ground.transform.position = new Vector3(0f, -3f, 0f);
        ground.transform.localScale = new Vector3(12f, 1f, 1f);

        SpriteRenderer spriteRenderer = ground.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetOrCreatePlaceholderSprite();
        spriteRenderer.color = new Color(0.4f, 0.3f, 0.2f);

        BoxCollider2D collider = ground.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;

        GameObject spawnPoint = new GameObject("SpawnPoint1");
        spawnPoint.tag = "SpawnPoint";
        spawnPoint.transform.position = new Vector3(0f, 0f, 0f);

        Undo.RegisterCreatedObjectUndo(ground, "Create Test Ground");
        Selection.activeGameObject = ground;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("TestGround(바닥)와 SpawnPoint1을 생성했습니다. Ctrl+S로 저장하세요.");
    }

    // 실제 빌드에서는 Editor의 Tools 메뉴가 존재하지 않으므로, StartClient()를 부를
    // 진짜 UI 버튼이 필요하다. TextMeshPro는 "Import TMP Essentials"를 거치지 않으면
    // 폰트가 비어 깨질 수 있어(에디터 GUI로 직접 확인 못 하는 상황이라 위험), 항상
    // 존재하는 레거시 UI.Text + 내장 폰트(LegacyRuntime.ttf)로 만든다.
    [MenuItem("Tools/Coop Setup/5. Create Connect UI")]
    public static void CreateConnectUI()
    {
        if (GameObject.Find("ConnectCanvas") != null)
        {
            Debug.LogWarning("ConnectCanvas가 이미 씬에 있어서 새로 만들지 않았습니다.");
            return;
        }

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<StandaloneInputModule>();
            Undo.RegisterCreatedObjectUndo(eventSystemObj, "Create EventSystem");
        }

        GameObject canvasObj = new GameObject("ConnectCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject buttonObj = new GameObject("ConnectButton");
        buttonObj.transform.SetParent(canvasObj.transform, false);
        RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(280f, 80f);
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = Vector2.zero;

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        buttonImage.type = Image.Type.Sliced;
        buttonImage.color = new Color(0.2f, 0.6f, 0.95f);

        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = buttonImage;

        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(buttonObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        Text label = textObj.AddComponent<Text>();
        label.text = "서버 접속";
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 28;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;

        GameObject networkManagerObj = GameObject.Find("NetworkManager");
        NetworkBootstrapper bootstrapper = networkManagerObj != null
            ? networkManagerObj.GetComponent<NetworkBootstrapper>()
            : null;

        if (bootstrapper != null)
        {
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, bootstrapper.ConnectToServer);
            bootstrapper.startMenuUI = canvasObj;
            EditorUtility.SetDirty(networkManagerObj);
        }
        else
        {
            Debug.LogWarning("NetworkManager를 못 찾아서 버튼 OnClick을 자동 연결하지 못했습니다. 먼저 '1. Create NetworkManager'를 실행하세요.");
        }

        Undo.RegisterCreatedObjectUndo(canvasObj, "Create Connect UI");
        Selection.activeGameObject = canvasObj;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("ConnectCanvas 생성 완료 -- 버튼 클릭 시 NetworkBootstrapper.ConnectToServer() 호출되도록 연결됨. Ctrl+S로 저장하세요.");
    }

    // CLAUDE.md UI Style Guide 색상 토큰 (PICO PARK 2 bundle.css 기반). 새 UI/기믹
    // 색상을 추가할 때는 여기 토큰을 재사용하고, 임의 색을 새로 만들지 않는다.
    private static readonly Color ColorPrimary = new Color(0.282f, 0.678f, 0.082f);       // #48ad15
    private static readonly Color ColorBgSecondary = new Color(0.914f, 0.984f, 0.875f);   // #e9fbdf

    private const string ScenePath = "Assets/scense/main.unity";

    // -executeMethod로 헤드리스 실행하면 Editor 실행 시점에 어떤 씬도 자동으로
    // 열리지 않는다 (사람이 GUI로 열어둔 씬을 그대로 쓰는 게 아니라 빈 임시 씬에서
    // 시작함) -- main.unity를 명시적으로 열어야 우리가 만든 NetworkManager 등을
    // 찾을 수 있다.
    private static void EnsureSceneOpen()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
    }

    [MenuItem("Tools/Coop Setup/6. Create Coop Test (Button/Door/GoalZone)")]
    public static void CreateCoopTest()
    {
        EnsureSceneOpen();

        if (GameObject.Find("CoopButton1") != null)
        {
            Debug.LogWarning("CoopButton1이 이미 씬에 있어서 새로 만들지 않았습니다.");
            return;
        }

        Sprite sprite = GetOrCreatePlaceholderSprite();

        // 버튼: color-primary. 밟으면 바로 문이 열리도록 requiredButtons=1로 둔다 --
        // 봇 2개가 동시에 서로 다른 버튼 두 개를 밟는 걸 보장할 방법이 없어서
        // (같은 스폰에서 출발해 같은 Patrol 로직을 타므로 사실상 같이 움직인다),
        // 이 자동 테스트에서는 "버튼 1개 -> 문" 파이프라인 자체의 동작 검증에 집중한다.
        GameObject button = new GameObject("CoopButton1");
        button.transform.position = new Vector3(2f, -2.2f, 0f);
        SpriteRenderer buttonSprite = button.AddComponent<SpriteRenderer>();
        buttonSprite.sprite = sprite;
        buttonSprite.color = ColorPrimary;
        button.transform.localScale = new Vector3(0.6f, 0.2f, 1f);
        BoxCollider2D buttonCollider = button.AddComponent<BoxCollider2D>();
        buttonCollider.isTrigger = true;
        button.AddComponent<NetworkObject>();
        CoopButtonNGO buttonScript = button.AddComponent<CoopButtonNGO>();

        // 문: 색은 CoopDoorNGO.UpdateDoorVisuals가 OnNetworkSpawn 때 스스로 칠한다
        // (color-ice) -- 여기서는 크기/충돌체만 잡아준다.
        GameObject door = new GameObject("CoopDoor1");
        door.transform.position = new Vector3(4f, -1.7f, 0f);
        SpriteRenderer doorSprite = door.AddComponent<SpriteRenderer>();
        doorSprite.sprite = sprite;
        door.transform.localScale = new Vector3(0.4f, 1.2f, 1f);
        door.AddComponent<BoxCollider2D>();
        door.AddComponent<NetworkObject>();
        CoopDoorNGO doorScript = door.AddComponent<CoopDoorNGO>();
        doorScript.requiredButtons = 1;

        UnityEditor.Events.UnityEventTools.AddPersistentListener(buttonScript.OnButtonPress, doorScript.AddPress);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(buttonScript.OnButtonRelease, doorScript.RemovePress);

        // 골 존: color-bg-secondary, 반투명 트리거. BotController의 Patrol 범위
        // (스폰 기준 ±patrolHalfWidth, 기본 3유닛)를 폭 넉넉히 덮어서 봇들이
        // 패트롤하는 동안 자연스럽게 항상 안에 들어와 있도록 한다.
        GameObject goalZone = new GameObject("GoalZone1");
        goalZone.transform.position = new Vector3(0f, -2.2f, 0f);
        SpriteRenderer goalSprite = goalZone.AddComponent<SpriteRenderer>();
        goalSprite.sprite = sprite;
        goalSprite.color = new Color(ColorBgSecondary.r, ColorBgSecondary.g, ColorBgSecondary.b, 0.4f);
        goalZone.transform.localScale = new Vector3(7f, 1.5f, 1f);
        BoxCollider2D goalCollider = goalZone.AddComponent<BoxCollider2D>();
        goalCollider.isTrigger = true;
        goalZone.AddComponent<NetworkObject>();
        GoalZoneNGO goalScript = goalZone.AddComponent<GoalZoneNGO>();

        GameObject networkManagerObj = GameObject.Find("NetworkManager");
        if (networkManagerObj != null)
        {
            StageManagerNGO stageManager = networkManagerObj.GetComponent<StageManagerNGO>();
            if (stageManager == null)
            {
                stageManager = networkManagerObj.AddComponent<StageManagerNGO>();
            }
            stageManager.goalZone = goalScript;
            EditorUtility.SetDirty(networkManagerObj);
        }
        else
        {
            Debug.LogWarning("NetworkManager를 못 찾아서 StageManagerNGO를 연결하지 못했습니다. 먼저 '1. Create NetworkManager'를 실행하세요.");
        }

        Undo.RegisterCreatedObjectUndo(button, "Create Coop Test");
        Selection.objects = new Object[] { button, door, goalZone };
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("CoopButton1 -> CoopDoor1 -> GoalZone1 배치 및 연결 완료, 씬 저장까지 마쳤습니다.");
    }

    // 사람 파트너 없이 협동 기믹을 반복 테스트하기 위한 봇 클라이언트 실행기.
    // 실제로는 빌드된 클라이언트 실행 파일을 -bot 인자로 여러 개 띄우는 것 --
    // BotProcess/BotController가 각 프로세스 안에서 자동 접속 + 자동 조작을 담당한다.
    // 순서: Client 빌드 1회 -> 경로 지정 -> Editor Play 모드에서 Start Host -> 봇 실행.
    [MenuItem("Tools/Coop Setup/Bot Simulation/Set Bot Client Build Path...")]
    public static void SetBotClientPath()
    {
        string current = EditorPrefs.GetString(BotExePathKey, "");
        string startDir = !string.IsNullOrEmpty(current) ? Path.GetDirectoryName(current) : Application.dataPath;
        string path = EditorUtility.OpenFilePanel("봇으로 쓸 클라이언트 빌드(.exe) 선택", startDir, "exe");
        if (string.IsNullOrEmpty(path)) return;

        EditorPrefs.SetString(BotExePathKey, path);
        Debug.Log($"봇 클라이언트 경로 설정 완료: {path}");
    }

    [MenuItem("Tools/Coop Setup/Bot Simulation/Launch 1 Bot Client")]
    public static void LaunchOneBot() => LaunchBots(1);

    [MenuItem("Tools/Coop Setup/Bot Simulation/Launch 2 Bot Clients")]
    public static void LaunchTwoBots() => LaunchBots(2);

    [MenuItem("Tools/Coop Setup/Bot Simulation/Launch 3 Bot Clients")]
    public static void LaunchThreeBots() => LaunchBots(3);

    [MenuItem("Tools/Coop Setup/Bot Simulation/Stop All Bot Clients")]
    public static void StopAllBots()
    {
        string exePath = EditorPrefs.GetString(BotExePathKey, "");
        if (string.IsNullOrEmpty(exePath))
        {
            Debug.LogWarning("봇 클라이언트 경로가 지정되어 있지 않아 무엇을 종료할지 알 수 없습니다.");
            return;
        }

        string processName = Path.GetFileNameWithoutExtension(exePath);
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(processName);
        int killed = 0;
        foreach (System.Diagnostics.Process p in processes)
        {
            try
            {
                p.Kill();
                killed++;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"프로세스 종료 실패 (PID {p.Id}): {e.Message}");
            }
        }

        Debug.Log($"봇 클라이언트 {killed}개를 종료했습니다. (같은 이름의 다른 실행 중인 창도 함께 종료됨에 유의)");
    }

    private static void LaunchBots(int count)
    {
        string exePath = EditorPrefs.GetString(BotExePathKey, "");
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            Debug.LogError("봇 클라이언트 빌드 경로가 설정되지 않았습니다. 먼저 'Bot Simulation/Set Bot Client Build Path...'로 지정하세요 " +
                "(File > Build Settings > Windows, Mac, Linux 로 일반 클라이언트를 한 번 빌드해두어야 합니다).");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "-bot -serverip 127.0.0.1 -serverport 7777",
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(startInfo);
        }

        Debug.Log($"봇 클라이언트 {count}개를 실행했습니다 (127.0.0.1:7777로 접속 시도). " +
            "Editor에서 Play 모드로 들어가 'Debug: Start Host'를 먼저 눌러야 접속에 성공합니다.");
    }

    private static Sprite GetOrCreatePlaceholderSprite()
    {
        const string folder = "Assets/Sprites";
        const string path = folder + "/PlayerPlaceholder.png";

        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
        {
            return existing;
        }

        EnsureFolder(folder);

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.white;
        }
        texture.SetPixels(pixels);
        texture.Apply();

        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);

        File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 64;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path).Replace("\\", "/");
        string folderName = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }
        AssetDatabase.CreateFolder(parent, folderName);
    }
}
#endif
