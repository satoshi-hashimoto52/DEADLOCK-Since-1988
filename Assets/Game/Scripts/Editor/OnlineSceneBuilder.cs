using System.IO;
using Deadlock.Combat;
using Deadlock.Network;
using Deadlock.UI;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Deadlock.Editor
{
    /// <summary>
    /// Online LAN test scene (Proto_Online) and the network player prefab. Same local player rig, HUD, touch controls
    /// and menu as the offline scenes; no zombies and no offline pause.
    /// </summary>
    public static partial class PrototypeSceneBuilder
    {
        public const string OnlineScenePath = "Assets/Game/Scenes/Proto_Online.unity";
        public const string AvatarPrefabPath = "Assets/Game/Prefabs/Net_Avatar.prefab";
        const string ZombiePrefabPath = "Assets/Game/Prefabs/Net_Zombie.prefab";
        const string AmmoBoxPrefabPath = "Assets/Game/Prefabs/Net_AmmoBox.prefab";
        const string RoundPrefabPath = "Assets/Game/Prefabs/Net_PvPvERound.prefab";
        const string PrefabListPath = "Assets/Game/Prefabs/Net_Prefabs.asset";
        const string OnlineNavMeshPath = "Assets/Game/Generated/NavMesh_Online.asset";

        [MenuItem("DEADLOCK/Prototype/Open Online Test")]
        public static void OpenOnlineScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(OnlineScenePath);
        }

        static void BuildOnlineScene(int playerLayer, int viewModelLayer, int enemyLayer, int hitboxLayer, int ownHitboxLayer)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var mats = CreateMaterials();
            var (weapon, melee) = LoadDefinitions();
            var prefab = BuildAvatarPrefab(weapon, melee, hitboxLayer);
            // Online zombies see players through their hit volumes (not the local movement capsule), not through walls.
            int zombieSight = ~((1 << playerLayer) | (1 << enemyLayer) | (1 << viewModelLayer) | (1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << LayerMask.NameToLayer("UI")));
            var zombiePrefab = BuildNetworkZombiePrefab(enemyLayer, zombieSight);
            var boxPrefab = BuildNetworkAmmoBoxPrefab(mats);
            var roundPrefab = BuildRoundPrefab();
            var prefabList = BuildPrefabList(zombiePrefab, boxPrefab, roundPrefab);

            SetupLighting();
            BuildOnlineArena(mats);

            int notPlayers = (1 << playerLayer) | (1 << viewModelLayer) | (1 << LayerMask.NameToLayer("Ignore Raycast")) | (1 << LayerMask.NameToLayer("UI"));
            int hitMask = ~notPlayers;
            int worldMask = ~(notPlayers | (1 << hitboxLayer) | (1 << ownHitboxLayer));

            // Display-only shots (this machine's own and the others'), and the host's damaging simulation (invisible).
            var visual = BuildProjectiles(mats, playerLayer, viewModelLayer);
            visual.gameObject.name = "VisualProjectiles";
            visual.ApplyDamage = false;
            var server = new GameObject("ServerProjectiles").AddComponent<ProjectileSystem>();
            server.HitMask = hitMask;

            var spawns = new GameObject("SpawnPoints").transform;
            var spawnA = new GameObject("Spawn_A").transform;
            spawnA.SetParent(spawns, false);
            spawnA.SetPositionAndRotation(new Vector3(0f, 0.05f, -8f), Quaternion.identity);
            var spawnB = new GameObject("Spawn_B").transform;
            spawnB.SetParent(spawns, false);
            spawnB.SetPositionAndRotation(new Vector3(0f, 0.05f, 8f), Quaternion.Euler(0f, 180f, 0f));

            var player = BuildPlayer(weapon, melee, visual, playerLayer, viewModelLayer, spawnA.position);
            var canvas = BuildUi(player, visual);

            var netGo = new GameObject("NetworkManager");
            var transport = netGo.AddComponent<UnityTransport>();
            var manager = netGo.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = prefab,
                ConnectionApproval = true,
                EnableSceneManagement = false,
                TickRate = 30,
            };
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);

            var sessionGo = new GameObject("NetworkSession");
            var session = sessionGo.AddComponent<NetworkSession>();
            Set(session, "manager", manager);
            Set(session, "transport", transport);
            Set(session, "localRig", player.Rig);
            Set(session, "serverProjectiles", server);
            Set(session, "visualProjectiles", visual);
            SetArray(session, "spawnPoints", new Object[] { spawnA, spawnB });
            SetMask(session, "hitMask", hitMask);
            SetMask(session, "worldMask", worldMask);
            Set(session, "roundPrefab", roundPrefab);
            Set(session, "zombiePrefab", zombiePrefab);
            Set(session, "ammoBoxPrefab", boxPrefab);
            SetArray(session, "zombieSpawns", Points("ZombieSpawns", new Vector3(-10f, 0f, 11f), new Vector3(10f, 0f, -11f), new Vector3(-10f, 0f, -11f)));
            SetArray(session, "ammoBoxSpawns", Points("AmmoBoxSpawns", new Vector3(-6f, 0f, 1f), new Vector3(6f, 0f, -1f)));

            // The online items must exist before the menu builds itself.
            var ui = canvas.AddComponent<OnlineSessionUi>();
            Set(ui, "session", session);
            Set(ui, "input", player.Input);
            Set(ui, "icons", uiIcons);
            var menu = canvas.AddComponent<GameMenu>();
            Set(menu, "input", player.Input);
            Set(menu, "icons", uiIcons);
            Set(menu, "skins", skinCatalog);
            // No OfflineSessionPause here: online, the menu never stops the match.

            EditorSceneManager.SaveScene(scene, OnlineScenePath);
        }

        static void BuildOnlineArena(Materials m)
        {
            var root = new GameObject("Environment").transform;
            Box(root, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(24f, 0.5f, 28f), m.Floor);
            Box(root, "Wall_North", new Vector3(0f, 2f, 14.25f), new Vector3(25f, 4f, 0.5f), m.Wall);
            Box(root, "Wall_South", new Vector3(0f, 2f, -14.25f), new Vector3(25f, 4f, 0.5f), m.Wall);
            Box(root, "Wall_East", new Vector3(12.25f, 2f, 0f), new Vector3(0.5f, 4f, 29f), m.Wall);
            Box(root, "Wall_West", new Vector3(-12.25f, 2f, 0f), new Vector3(0.5f, 4f, 29f), m.Wall);
            // Centre wall between the two spawns (blocks shots; the lanes at x = ±6 are open).
            Box(root, "CentreWall", new Vector3(0f, 1.5f, 0f), new Vector3(5f, 3f, 0.4f), m.Wall);
            Box(root, "LowCover_L", new Vector3(-6f, 0.55f, -3f), new Vector3(3f, 1.1f, 0.4f), m.Concrete);
            Box(root, "LowCover_R", new Vector3(6f, 0.55f, 3f), new Vector3(3f, 1.1f, 0.4f), m.Concrete);
            Box(root, "Pillar_L", new Vector3(-8f, 2f, 6f), new Vector3(1.2f, 4f, 1.2f), m.Concrete);
            Box(root, "Pillar_R", new Vector3(8f, 2f, -6f), new Vector3(1.2f, 4f, 1.2f), m.Concrete);
            Box(root, "Crate_A", new Vector3(-9f, 0.5f, -10f), new Vector3(1f, 1f, 1f), m.Crate);
            Box(root, "Crate_B", new Vector3(9f, 0.5f, 10f), new Vector3(1f, 1f, 1f), m.Crate);
            // Crawl gap (ceiling 0.85 m) and crouch gap (1.35 m) along the west wall.
            Box(root, "CrouchGap_Roof", new Vector3(-10.5f, 1.45f, 2f), new Vector3(3f, 0.2f, 3f), m.Concrete);
            Box(root, "ProneGap_Roof", new Vector3(-10.5f, 0.95f, -3f), new Vector3(3f, 0.2f, 3f), m.Concrete);

            // Zombie paths (PvPvE check), baked here like the training arena.
            var surface = root.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << LayerMask.NameToLayer("Default");
            surface.BuildNavMesh();
            AssetDatabase.DeleteAsset(OnlineNavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, OnlineNavMeshPath);
        }

        static Object[] Points(string name, params Vector3[] positions)
        {
            var root = new GameObject(name).transform;
            var result = new Object[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                var t = new GameObject($"{name}_{i + 1}").transform;
                t.SetParent(root, false);
                t.position = positions[i];
                result[i] = t;
            }
            return result;
        }

        // ------------------------------------------------------------------ PvPvE network prefabs

        static GameObject BuildNetworkZombiePrefab(int enemyLayer, int sightMask)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ZombiePrefabPath));
            var (go, zombie, view) = CreateZombieObject("Net_Zombie", enemyLayer, sightMask);
            go.AddComponent<NetworkObject>();
            var net = go.AddComponent<NetworkZombie>();
            // AI and movement are switched on by the host only (clients mirror the synced state).
            zombie.enabled = false;
            var agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
            agent.enabled = false;
            Set(net, "zombie", zombie);
            Set(net, "agent", agent);
            Set(net, "view", view);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, ZombiePrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject BuildNetworkAmmoBoxPrefab(Materials m)
        {
            var go = new GameObject("Net_AmmoBox");
            go.AddComponent<NetworkObject>();
            var box = go.AddComponent<NetworkAmmoBox>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            visual.AddComponent<MeshFilter>().sharedMesh = WorldUvBox(new Vector3(0.6f, 0.4f, 0.4f));
            visual.AddComponent<MeshRenderer>().sharedMaterial = m.Ammo;
            Set(box, "visual", visual);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, AmmoBoxPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject BuildRoundPrefab()
        {
            var go = new GameObject("Net_PvPvERound");
            go.AddComponent<NetworkObject>();
            go.AddComponent<PvPvERound>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, RoundPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Network prefabs the host spawns at runtime (the player prefab is registered by itself).</summary>
        static NetworkPrefabsList BuildPrefabList(params GameObject[] prefabs)
        {
            AssetDatabase.DeleteAsset(PrefabListPath);
            var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            foreach (var p in prefabs)
                list.Add(new NetworkPrefab { Prefab = p });
            AssetDatabase.CreateAsset(list, PrefabListPath);
            return list;
        }

        // ------------------------------------------------------------------ network player prefab

        static GameObject BuildAvatarPrefab(WeaponDefinition weapon, MeleeDefinition melee, int hitboxLayer)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AvatarPrefabPath));
            var cloth = LoadOrCreateMaterial("Mat_AvatarCloth", "Universal Render Pipeline/Simple Lit");
            cloth.color = Color.white;
            var skin = LoadOrCreateMaterial("Mat_AvatarSkin", "Universal Render Pipeline/Simple Lit");
            skin.color = new Color(0.86f, 0.7f, 0.55f);
            var dark = LoadOrCreateMaterial("Mat_AvatarDark", "Universal Render Pipeline/Simple Lit");
            dark.color = new Color(0.12f, 0.12f, 0.14f);
            var steel = LoadOrCreateMaterial("Mat_AvatarSteel", "Universal Render Pipeline/Simple Lit");
            steel.color = new Color(0.75f, 0.77f, 0.8f);
            var flash = LoadOrCreateMaterial("Mat_AvatarFlash", "Universal Render Pipeline/Unlit");
            flash.color = new Color(1f, 0.85f, 0.35f);
            foreach (var mat in new[] { cloth, skin, dark, steel, flash })
                EditorUtility.SetDirty(mat);

            var root = new GameObject("Net_Avatar");
            root.AddComponent<NetworkObject>();
            var avatar = root.AddComponent<NetworkAvatar>();
            root.AddComponent<AvatarCheckChannel>(); // inert unless a -netCheck run is active

            // Hit volumes (posture shapes are set at runtime).
            var hitRoot = new GameObject("Hitboxes") { layer = hitboxLayer };
            hitRoot.transform.SetParent(root.transform, false);
            var body = new GameObject("Body") { layer = hitboxLayer };
            body.transform.SetParent(hitRoot.transform, false);
            var bodyCollider = body.AddComponent<CapsuleCollider>();
            bodyCollider.center = new Vector3(0f, 0.75f, 0f);
            bodyCollider.height = 1.5f;
            bodyCollider.radius = 0.3f;
            var head = new GameObject("Head") { layer = hitboxLayer };
            head.transform.SetParent(hitRoot.transform, false);
            var headCollider = head.AddComponent<SphereCollider>();
            headCollider.center = new Vector3(0f, 1.62f, 0f);
            headCollider.radius = 0.17f;
            var rb = hitRoot.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var hitboxes = hitRoot.AddComponent<AvatarHitboxes>();
            Set(hitboxes, "avatar", avatar);
            Set(hitboxes, "body", bodyCollider);
            Set(hitboxes, "head", headCollider);

            // Blocky original placeholder model (no colliders).
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            var bodyPivot = new GameObject("Body").transform;
            bodyPivot.SetParent(model, false);
            var legs = Part(bodyPivot, "Legs", new Vector3(0f, 0.4f, 0f), new Vector3(0.36f, 0.8f, 0.24f), cloth);
            var torso = Part(bodyPivot, "Torso", new Vector3(0f, 1.1f, 0f), new Vector3(0.46f, 0.62f, 0.28f), cloth);
            var neck = new GameObject("HeadPivot").transform;
            neck.SetParent(bodyPivot, false);
            neck.localPosition = new Vector3(0f, 1.44f, 0f);
            Part(neck, "Head", new Vector3(0f, 0.16f, 0f), new Vector3(0.26f, 0.28f, 0.26f), skin);
            Part(neck, "Visor", new Vector3(0f, 0.19f, 0.13f), new Vector3(0.22f, 0.07f, 0.03f), dark); // shows the facing
            var arms = new GameObject("Arms").transform;
            arms.SetParent(bodyPivot, false);
            arms.localPosition = new Vector3(0f, 1.3f, 0f);
            var armR = Part(arms, "Arm_R", new Vector3(0.2f, -0.08f, 0.2f), new Vector3(0.1f, 0.1f, 0.45f), cloth);
            var armL = Part(arms, "Arm_L", new Vector3(-0.12f, -0.1f, 0.3f), new Vector3(0.1f, 0.1f, 0.4f), cloth);
            var gun = new GameObject("Gun").transform;
            gun.SetParent(arms, false);
            Part(gun, "Receiver", new Vector3(0.06f, -0.06f, 0.4f), new Vector3(0.09f, 0.12f, 0.75f), dark);
            var muzzle = Part(gun, "MuzzleFlash", new Vector3(0.06f, -0.04f, 0.86f), new Vector3(0.16f, 0.16f, 0.16f), flash);
            var knife = new GameObject("Knife").transform;
            knife.SetParent(arms, false);
            Part(knife, "Blade", new Vector3(0.2f, -0.08f, 0.55f), new Vector3(0.03f, 0.06f, 0.3f), steel);

            var view = root.AddComponent<AvatarView>();
            Set(view, "model", model);
            Set(view, "body", bodyPivot);
            Set(view, "head", neck);
            Set(view, "arms", arms);
            Set(view, "gun", gun.gameObject);
            Set(view, "knife", knife.gameObject);
            Set(view, "muzzleFlash", muzzle.gameObject);
            SetArray(view, "tinted", new Object[] { legs.GetComponent<Renderer>(), torso.GetComponent<Renderer>(), armR.GetComponent<Renderer>(), armL.GetComponent<Renderer>() });

            Set(avatar, "gunDefinition", weapon);
            Set(avatar, "meleeDefinition", melee);
            Set(avatar, "hitboxes", hitboxes);
            Set(avatar, "view", view);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, AvatarPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Transform Part(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }
    }
}
