#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
