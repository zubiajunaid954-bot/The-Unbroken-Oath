using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using System.IO;

public static class SceneMerger
{
    [MenuItem("GAMECON/Combine DemoScene and Scene_A into One Scene")]
    public static void MergeScenes()
    {
        string demoScenePath = "Assets/scgFullBodyController/Scenes/DemoScene.unity";
        string sceneAPath = "Assets/Flooded_Grounds/Scenes/Scene_A.unity";
        string combinedScenePath = "Assets/Scenes/Combined_Scene.unity";

        if (!File.Exists(demoScenePath) || !File.Exists(sceneAPath))
        {
            EditorUtility.DisplayDialog("Error", "Could not find DemoScene or Scene_A!", "OK");
            return;
        }

        if (!Directory.Exists("Assets/Scenes"))
        {
            Directory.CreateDirectory("Assets/Scenes");
        }

        // 1. Open Scene_A
        Scene sceneA = EditorSceneManager.OpenScene(sceneAPath, OpenSceneMode.Single);

        // 2. Open DemoScene additively
        Scene demoScene = EditorSceneManager.OpenScene(demoScenePath, OpenSceneMode.Additive);

        // 3. In Scene_A, locate the old FpsController and get its spawn position
        Vector3 playerSpawnPos = new Vector3(456.03f, 29.5f, 153.83f);
        GameObject oldFps = GameObject.Find("FpsController");
        if (oldFps != null)
        {
            playerSpawnPos = oldFps.transform.position + Vector3.up * 0.5f;
            oldFps.SetActive(false);
            Debug.Log("[SceneMerger] Disabled existing FpsController in Scene_A.");
        }

        // Disable standalone Camera in Scene_A
        Camera[] cams = Object.FindObjectsOfType<Camera>();
        foreach (Camera c in cams)
        {
            if (c.gameObject.scene == sceneA && c.name == "Camera")
            {
                c.gameObject.SetActive(false);
            }
        }

        // 4. Transfer key objects from DemoScene into Scene_A
        GameObject[] demoRoots = demoScene.GetRootGameObjects();
        foreach (GameObject go in demoRoots)
        {
            string name = go.name;
            if (name.Contains("FullBodyController") || 
                name == "Canvas" || 
                name == "EventSystem" || 
                name == "sceneController")
            {
                SceneManager.MoveGameObjectToScene(go, sceneA);

                if (name.Contains("FullBodyController"))
                {
                    go.transform.position = playerSpawnPos;
                    Debug.Log($"[SceneMerger] Moved FullBodyController to position: {playerSpawnPos}");
                }
            }
        }

        // 5. Close / Unload DemoScene
        EditorSceneManager.CloseScene(demoScene, true);

        // 6. Spawn Soldier AI Squad
        SpawnSoldierSquad(playerSpawnPos, 12);

        // 7. Save as Combined_Scene.unity
        EditorSceneManager.SaveScene(sceneA, combinedScenePath);
        Debug.Log($"[SceneMerger] Successfully created combined scene at: {combinedScenePath}");

        EditorUtility.DisplayDialog("Scene Combined Successfully!", 
            $"Created combined scene at:\nAssets/Scenes/Combined_Scene.unity\n\nFullBodyController and a squad of 12 armed enemy soldiers have been deployed around the map!", 
            "Great!");
    }

    [MenuItem("GAMECON/Deploy 12 Enemy Soldiers in Current Scene")]
    public static void DeploySoldiersToCurrentScene()
    {
        // Find player position
        Vector3 playerPos = new Vector3(456.03f, 29.5f, 153.83f);
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) player = GameObject.Find("FullBodyController");
        if (player != null) playerPos = player.transform.position;

        SpawnSoldierSquad(playerPos, 12);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Enemies Deployed!", "12 Enemy Soldiers deployed across tactical positions around the player!", "OK");
    }

    public static void SpawnSoldierSquad(Vector3 centerPos, int count)
    {
        string hardBotPath = "Assets/scgFullBodyController/Prefabs/AI/Bot(Ai) Hard.prefab";
        string mediumBotPath = "Assets/scgFullBodyController/Prefabs/AI/Bot(Ai) Medium.prefab";

        GameObject hardBotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hardBotPath);
        GameObject mediumBotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(mediumBotPath);

        if (hardBotPrefab == null && mediumBotPrefab == null)
        {
            Debug.LogError("[SceneMerger] Could not find enemy soldier prefabs!");
            return;
        }

        GameObject squadParent = GameObject.Find("Enemy_Soldiers_Squad");
        if (squadParent == null)
        {
            squadParent = new GameObject("Enemy_Soldiers_Squad");
        }

        // Relative spawn offsets around player (different directions and tactical distances: 15m to 45m)
        Vector3[] spawnOffsets = new Vector3[]
        {
            new Vector3(15, 0, 10),
            new Vector3(-18, 0, 12),
            new Vector3(25, 0, -15),
            new Vector3(-22, 0, -20),
            new Vector3(30, 0, 5),
            new Vector3(-35, 0, 0),
            new Vector3(10, 0, 35),
            new Vector3(-12, 0, 30),
            new Vector3(20, 0, 25),
            new Vector3(-28, 0, 22),
            new Vector3(40, 0, -10),
            new Vector3(-15, 0, -35),
            new Vector3(0, 0, 42),
            new Vector3(-40, 0, 18),
            new Vector3(32, 0, 32)
        };

        for (int i = 0; i < count; i++)
        {
            Vector3 offset = spawnOffsets[i % spawnOffsets.Length];
            Vector3 targetSpawn = centerPos + offset;

            // Sample ground height with raycast or NavMesh
            RaycastHit hit;
            if (Physics.Raycast(targetSpawn + Vector3.up * 50f, Vector3.down, out hit, 100f))
            {
                targetSpawn.y = hit.point.y + 0.1f;
            }

            GameObject prefabToSpawn = (i % 2 == 0 && hardBotPrefab != null) ? hardBotPrefab : mediumBotPrefab;
            GameObject botInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefabToSpawn);
            botInstance.name = $"Enemy_Soldier_{i + 1}";
            botInstance.transform.position = targetSpawn;
            botInstance.transform.LookAt(new Vector3(centerPos.x, targetSpawn.y, centerPos.z));
            botInstance.transform.parent = squadParent.transform;

            // Configure AI parameters so they actively seek and shoot
            var ai = botInstance.GetComponent<scgFullBodyController.AiController>();
            if (ai != null)
            {
                ai.sightRange = Mathf.Max(ai.sightRange, 50f);    // Can spot player from further away
                ai.attackRange = Mathf.Max(ai.attackRange, 35f);  // Can engage and shoot at good range
            }

            Debug.Log($"[SceneMerger] Spawned {botInstance.name} at {targetSpawn}");
        }
    }
}
