using UnityEngine;

namespace GravityFlip
{
    /// <summary>
    /// A LEVEL GENERATOR, not part of the game.
    ///
    /// This started out as the thing that made the game run: one empty GameObject, press
    /// Play, everything springs into existence. That was the right call for getting a
    /// mechanic working with zero scene setup to mis-wire.
    ///
    /// It is no longer load-bearing. Every component now reads its own references from
    /// [SerializeField] slots, so a saved scene runs on its own and this whole file can be
    /// deleted. What it's still good for is regenerating the level: right-click this
    /// component's header in the Inspector and pick "Generate Level In Editor" to get a
    /// fresh, fully-wired copy in the Hierarchy that you can then hand-tune.
    ///
    /// WHY THAT MATTERS, since it's a fair interview question: generating a scene from code
    /// is fast to iterate and trivially version-controlled, but designers can't work in it
    /// and you can't see the level without running the game. Authoring a scene is the
    /// opposite trade. Real projects author, and keep small generator tools like this one
    /// around for the repetitive parts. Having done both is the point.
    ///
    /// If nothing appears until you press Play, that's Start() below — Start is a Play-mode
    /// callback, so Unity never calls it while you're merely editing.
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        /// <summary>
        /// Everything Build() makes goes under one parent with this name. That gives edit
        /// mode a single object to delete for a clean regenerate, and gives Play mode a
        /// single object to look for to decide "already built, don't build again".
        /// </summary>
        private const string GeneratedRootName = "Generated";

        [SerializeField] private bool buildOnStart = true;
        [SerializeField] private Vector3 playerSpawn = new Vector3(0f, 1.2f, -10f);

        private Transform generatedRoot;

        private void Start()
        {
            // If the level is already sitting in the scene, there is nothing for me to do.
            // Every component now finds its own references through [SerializeField] slots,
            // so the game does not need this script at all — deleting the component (or
            // this file) is the intended end state once you're happy with the scene.
            if (FindGeneratedRoot() != null) return;

            if (buildOnStart) Build();
        }

        public void Build()
        {
            generatedRoot = new GameObject(GeneratedRootName).transform;

            Material floorMaterial  = MakeMaterial("Floor",    new Color(0.20f, 0.22f, 0.28f));
            Material wallMaterial   = MakeMaterial("Wall",     new Color(0.30f, 0.32f, 0.38f));
            Material accentMaterial = MakeMaterial("Accent",   new Color(0.85f, 0.45f, 0.30f));
            Material platformMat    = MakeMaterial("Platform", new Color(0.45f, 0.55f, 0.65f));
            Material playerMaterial = MakeMaterial("Player",   new Color(0.95f, 0.95f, 0.98f));
            Material pickupMaterial = MakeMaterial("Pickup",   new Color(1.00f, 0.82f, 0.25f));

            BuildLevel(floorMaterial, wallMaterial, accentMaterial, platformMat);
            BuildLights();

            GameManager manager = new GameObject("Game Manager").AddComponent<GameManager>();
            manager.transform.SetParent(generatedRoot, true);

            GameObject player = BuildPlayer(playerMaterial, accentMaterial);

            // Pickups are handed the manager as they're created; they register themselves
            // with it in their own Start, so the total is never a number we have to
            // remember to keep in sync by hand.
            BuildCollectibles(manager, pickupMaterial);

            WireCamera(player, manager);
        }

        // ------------------------------------------------------------------------------
        // Editor-time generation
        // ------------------------------------------------------------------------------

        /// <summary>
        /// [ContextMenu] puts this method in the right-click menu of the component header
        /// in the Inspector. It's the cheapest possible custom editor tool — no separate
        /// Editor folder, no UnityEditor namespace, no custom inspector class. Worth
        /// knowing: half the "tooling" a gameplay programmer writes is this small.
        ///
        /// Objects made here are ordinary scene objects. They persist, they save with the
        /// scene, and you can move them around like anything else. Objects made at runtime
        /// by Play mode are thrown away when you stop — that difference is the entire
        /// answer to "why can't I see anything before I press Play".
        /// </summary>
        [ContextMenu("Generate Level In Editor")]
        public void GenerateInEditor()
        {
            DeleteGenerated();
            Build();

#if UNITY_EDITOR
            // Build() filled in the same Inspector slots you'd otherwise drag things into.
            // Unity won't know the scene changed unless we say so, and an unmarked scene
            // can be closed without ever offering to save — losing the lot.
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
#endif

            Debug.Log(
                "Built '" + GeneratedRootName + "' in the Hierarchy, with every reference " +
                "wired into its Inspector slot. File > Save, and the game no longer needs " +
                "this component at all — you can remove it.");
        }

        [ContextMenu("Delete Generated Objects")]
        public void DeleteGenerated()
        {
            Transform root = FindGeneratedRoot();
            if (root == null) return;

            // Destroy is deferred to the end of the frame, and edit mode has no frames, so
            // calling it here would do nothing and log a complaint. DestroyImmediate is the
            // edit-mode counterpart. Never use it during Play — removing an object
            // mid-frame while other scripts still hold a reference to it is how you get
            // errors that look impossible.
            if (Application.isPlaying) Destroy(root.gameObject);
            else DestroyImmediate(root.gameObject);
        }

        private Transform FindGeneratedRoot()
        {
            if (generatedRoot != null) return generatedRoot;

            // Deliberately searching by name. Fine for one object at startup; never do it
            // per-frame, and never rely on it in a codebase where a designer might rename
            // things. A serialized reference is the grown-up version.
            GameObject found = GameObject.Find(GeneratedRootName);
            generatedRoot = found != null ? found.transform : null;
            return generatedRoot;
        }

        private void BuildLevel(Material floor, Material wall, Material accent, Material platform)
        {
            Transform root = new GameObject("Level").transform;
            root.SetParent(generatedRoot, true);

            // An open-topped room. Four walls give you vertical surfaces to flip onto, and
            // leaving the top open lets the directional light in so we don't have to fight
            // with ambient lighting settings just to see anything.
            Box(root, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(34f, 1f, 34f), floor);

            Box(root, "Wall -X", new Vector3(-17.5f, 8f, 0f), new Vector3(1f, 18f, 34f), wall);
            Box(root, "Wall +X", new Vector3(17.5f, 8f, 0f), new Vector3(1f, 18f, 34f), wall);
            Box(root, "Wall -Z", new Vector3(0f, 8f, -17.5f), new Vector3(34f, 18f, 1f), wall);
            Box(root, "Wall +Z", new Vector3(0f, 8f, 17.5f), new Vector3(34f, 18f, 1f), wall);

            // A partial roof. Its underside is a ceiling you can stand on, which is the
            // single most satisfying thing to discover in a game like this.
            Box(root, "Overhang", new Vector3(0f, 14f, 6f), new Vector3(22f, 1f, 16f), accent);

            // Central tower — walking up its side is the clearest demo of the mechanic.
            Box(root, "Tower", new Vector3(0f, 5f, 0f), new Vector3(4f, 10f, 4f), accent);

            // Scattered platforms at heights you can only reach by flipping.
            Box(root, "Platform A", new Vector3(-11f, 4f, -8f), new Vector3(5f, 0.6f, 5f), platform);
            Box(root, "Platform B", new Vector3(11f, 7f, -4f), new Vector3(5f, 0.6f, 5f), platform);
            Box(root, "Platform C", new Vector3(-9f, 10f, 8f), new Vector3(4f, 0.6f, 4f), platform);
            Box(root, "Platform D", new Vector3(12f, 11.5f, 11f), new Vector3(4f, 0.6f, 4f), platform);

            // Fins sticking off the walls. Flipping onto a fin means your "down" is now
            // sideways while a wall is at your back — good for testing camera comfort.
            Box(root, "Fin Left", new Vector3(-14f, 9f, -14f), new Vector3(6f, 0.6f, 3f), platform);
            Box(root, "Fin Right", new Vector3(14f, 12f, -12f), new Vector3(6f, 0.6f, 3f), platform);
        }

        /// <summary>
        /// Cube primitives are 1x1x1, so localScale doubles as the size in metres.
        /// They come with a BoxCollider already attached, which is why the physics and
        /// the flip raycasts just work with no extra setup.
        /// </summary>
        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private GameObject BuildPlayer(Material bodyMaterial, Material accentMaterial)
        {
            // A capsule primitive already has a CapsuleCollider sized height 2, radius 0.5,
            // which is what PlayerController's ground check assumes. Leave the scale at 1.
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.SetParent(generatedRoot, true);
            player.transform.position = playerSpawn;
            player.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;

            // A little nose so you can see which way you're facing. Without it a capsule
            // gives you no visual feedback that turning is working at all.
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing Marker";
            nose.transform.SetParent(player.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.35f, 0.42f);
            nose.transform.localScale = new Vector3(0.30f, 0.18f, 0.35f);
            nose.GetComponent<MeshRenderer>().sharedMaterial = accentMaterial;

            // Cube primitives ship with a collider. A stray collider on the player would
            // muddy both the ground sweep and the flip ray, so it goes. Destroy is a no-op
            // outside Play mode, hence the split.
            Collider noseCollider = nose.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(noseCollider);
            else DestroyImmediate(noseCollider);

            Rigidbody rb = player.AddComponent<Rigidbody>();
            rb.mass = 1f;

            // Order matters only for readability — [RequireComponent] would add anything
            // missing automatically, but being explicit makes the dependency obvious.
            player.AddComponent<GravityBody>();
            player.AddComponent<PlayerController>();
            player.AddComponent<GravityFlipper>();

            return player;
        }

        /// <summary>Where a pickup sits, and which way the surface it's mounted on faces.</summary>
        private struct CollectibleSpot
        {
            public Vector3 Position;
            public Vector3 SurfaceUp;

            public CollectibleSpot(Vector3 position, Vector3 surfaceUp)
            {
                Position = position;
                SurfaceUp = surfaceUp;
            }
        }

        /// <summary>
        /// Every pickup is placed somewhere you cannot reach without flipping. That's the
        /// whole design job here: a collectible on the floor teaches the player nothing,
        /// but one stuck to a ceiling forces them to use the mechanic and discover they
        /// enjoy it.
        /// </summary>
        private void BuildCollectibles(GameManager manager, Material material)
        {
            Transform root = new GameObject("Collectibles").transform;
            root.SetParent(generatedRoot, true);

            CollectibleSpot[] spots =
            {
                // Underside of the overhang. Requires standing on a ceiling.
                new CollectibleSpot(new Vector3(-6f, 12.9f, 3f), Vector3.down),
                new CollectibleSpot(new Vector3(6f, 12.9f, 9f), Vector3.down),

                // Stuck to the room walls, well above jumping height.
                new CollectibleSpot(new Vector3(-16.4f, 6f, 4f), Vector3.right),
                new CollectibleSpot(new Vector3(16.4f, 9f, -6f), Vector3.left),
                new CollectibleSpot(new Vector3(4f, 11f, -16.4f), Vector3.forward),
                new CollectibleSpot(new Vector3(-8f, 4f, 16.4f), Vector3.back),

                // On the vertical faces of the central tower.
                new CollectibleSpot(new Vector3(-2.6f, 7f, 0f), Vector3.left),
                new CollectibleSpot(new Vector3(2.6f, 3.5f, 0f), Vector3.right),

                // On top of the two highest platforms — ordinary gravity, but you can only
                // climb up there by flipping onto a wall first.
                new CollectibleSpot(new Vector3(-9f, 10.9f, 8f), Vector3.up),
                new CollectibleSpot(new Vector3(12f, 12.4f, 11f), Vector3.up),
            };

            foreach (CollectibleSpot spot in spots)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Pickup";
                go.transform.SetParent(root, true);
                go.transform.position = spot.Position;
                go.transform.localScale = Vector3.one * 0.55f;
                go.GetComponent<MeshRenderer>().sharedMaterial = material;

                // Trigger, not a solid collider, so you pass through it. The radius is in
                // local units, so the 0.55 scale above shrinks it — a value of 1.3 here
                // gives a real pickup radius of about 0.72 metres, forgiving enough that
                // you don't have to be precise while running.
                SphereCollider trigger = go.GetComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = 1.3f;

                // Configuration only — the pickup registers itself with the manager in its
                // own Start, so this works the same whether we're in Play mode or
                // generating into the scene from the editor.
                go.AddComponent<Collectible>().Initialise(manager, spot.SurfaceUp);
            }
        }

        /// <summary>
        /// Fills in every reference the scene needs. Each Set/Bind call below writes to a
        /// [SerializeField] on the target component, which is the same field you'd populate
        /// by dragging an object into an Inspector slot — so generating in the editor and
        /// saving produces a scene that no longer needs this script.
        /// </summary>
        private void WireCamera(GameObject player, GameManager manager)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            // The camera gets pulled tight against walls constantly in this game, so a
            // near clip plane of the default 0.3 will slice through geometry.
            cam.nearClipPlane = 0.05f;

            OrbitCamera orbit = cam.GetComponent<OrbitCamera>();
            if (orbit == null) orbit = cam.gameObject.AddComponent<OrbitCamera>();

            Crosshair crosshair = cam.GetComponent<Crosshair>();
            if (crosshair == null) crosshair = cam.gameObject.AddComponent<Crosshair>();

            GameHud hud = cam.GetComponent<GameHud>();
            if (hud == null) hud = cam.gameObject.AddComponent<GameHud>();

            GravityFlipper flipper = player.GetComponent<GravityFlipper>();
            PlayerController controller = player.GetComponent<PlayerController>();

            controller.SetCamera(cam.transform);
            flipper.SetCamera(cam);
            crosshair.Bind(flipper);
            hud.Bind(manager);

            // The manager needs these so it can take control away from the player when the
            // last pickup is collected.
            manager.BindPlayer(controller, flipper, orbit);

            // Last, because SetTarget snapshots the gravity frame and grabs the cursor.
            orbit.SetTarget(player.transform);
        }

        private void BuildLights()
        {
            GameObject sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(generatedRoot, true);
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.90f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;

            // Fill light so faces pointing away from the sun aren't solid black. Adding a
            // second light is cheaper to reason about than configuring ambient lighting
            // from script, and it survives a change of render pipeline.
            GameObject fillGo = new GameObject("Fill Light");
            fillGo.transform.SetParent(generatedRoot, true);
            fillGo.transform.position = new Vector3(0f, 10f, -6f);
            Light fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 50f;
            fill.intensity = 0.85f;
            fill.color = new Color(0.60f, 0.70f, 1f);
        }

        /// <summary>
        /// Wraps CreateMaterial with one extra concern: a material created in code exists
        /// only in memory. At runtime that's fine — it dies with the play session anyway.
        /// In edit mode it's a trap: the scene saves a reference to a material that was
        /// never written to disk, so reopening the project gives you missing-material
        /// magenta. So when generating in the editor we save it as a real asset first.
        /// </summary>
        private static Material MakeMaterial(string name, Color colour)
        {
            Material material = CreateMaterial(colour);
            material.name = "GravityFlip " + name;

#if UNITY_EDITOR
            if (!Application.isPlaying) material = PersistMaterial(material, name);
#endif
            return material;
        }

#if UNITY_EDITOR
        // Inside #if UNITY_EDITOR because the UnityEditor namespace doesn't exist in a built
        // game — referencing it without the guard compiles fine in the editor and then
        // fails the build, which is a genuinely common way to lose an afternoon.
        private static Material PersistMaterial(Material material, string name)
        {
            const string folder = "Assets/GravityFlipMaterials";

            if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
            {
                UnityEditor.AssetDatabase.CreateFolder("Assets", "GravityFlipMaterials");
            }

            string path = folder + "/" + name + ".mat";
            Material existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
            {
                // Reuse the asset rather than leaving a second one behind every regenerate.
                existing.CopyPropertiesFromMaterial(material);
                UnityEditor.EditorUtility.SetDirty(existing);
                return existing;
            }

            UnityEditor.AssetDatabase.CreateAsset(material, path);
            UnityEditor.AssetDatabase.SaveAssets();
            return material;
        }
#endif

        /// <summary>
        /// Creates a lit material without caring which render pipeline the project uses.
        ///
        /// NOTE FOR LATER: Shader.Find works reliably in the editor, but shaders that no
        /// asset references can get stripped out of a real build. When you make a WebGL
        /// build, either create proper material assets or add the shader under
        /// Project Settings > Graphics > Always Included Shaders.
        /// </summary>
        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            if (shader == null)
            {
                Debug.LogError("Bootstrap: no lit shader found. Objects will render as magenta.");
                return new Material(Shader.Find("Sprites/Default"));
            }

            Material material = new Material(shader);

            // URP and HDRP expose _BaseColor; the built-in Standard shader uses _Color.
            // Setting whichever one exists keeps this single code path pipeline-agnostic.
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.15f);

            return material;
        }
    }
}
