using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEngine.SceneManagement;

namespace Pivot.EditorTools
{
    /// <summary>
    /// Builds the lab props as ProBuilder meshes and drops them into the Tree Lab scene.
    /// Each prop keeps its ProBuilderMesh component, so the geometry stays editable by
    /// hand in the Editor afterwards, and its generated mesh is saved as a real asset
    /// under Models/ rather than living only inside the scene.
    ///
    /// Deleted once the three props are authored. The scene and the mesh assets are the
    /// source of truth from then on.
    /// </summary>
    static class AuthorProps
    {
        const string ScenePath = "Assets/_Project/Scenes/01_TreeLab.unity";
        const string ModelDir = "Assets/_Project/Models";
        const string MaterialDir = "Assets/_Project/Materials";

        /// <summary>Top surface of the workbench. Everything else is placed relative to this.</summary>
        public const float BenchTop = 1.0f;

        // ------------------------------------------------------------------ shared

        static Scene OpenLab()
        {
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static Transform Environment(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "--- Environment ---") return root.transform;
            }

            Debug.LogError("Pivot: no --- Environment --- group in the scene.");
            return null;
        }

        static void Replace(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }

        /// <summary>
        /// Commits the generated mesh to an asset. ProBuilder rebuilds this same Mesh
        /// object in place whenever the geometry is edited, so the asset stays current
        /// without anything having to re-export it.
        /// </summary>
        static void SaveMesh(ProBuilderMesh pb, string assetName)
        {
            pb.ToMesh();
            pb.Refresh();

            MeshFilter filter = pb.GetComponent<MeshFilter>();
            Mesh mesh = filter.sharedMesh;
            mesh.name = assetName;

            string path = ModelDir + "/" + assetName + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);

            AssetDatabase.CreateAsset(mesh, path);
            Debug.Log("Pivot: saved " + path + " verts=" + mesh.vertexCount +
                      " tris=" + (mesh.triangles.Length / 3));
        }

        static Material PropMaterial(string name, Color colour, float smoothness)
        {
            Directory.CreateDirectory(MaterialDir);
            string path = MaterialDir + "/" + name + ".mat";

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                material = new Material(lit);
                AssetDatabase.CreateAsset(material, path);
                Debug.Log("Pivot: created " + path);
            }

            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Rounds every edge on the shape. Nothing in this lab has a sharp corner.</summary>
        static void BevelAll(ProBuilderMesh pb, float amount)
        {
            List<Edge> edges = new List<Edge>();
            for (int i = 0; i < pb.faces.Count; i++) edges.AddRange(pb.faces[i].edges);

            Bevel.BevelEdges(pb, edges, amount);
            pb.ToMesh();
            pb.Refresh();
        }

        static void Finish(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // --------------------------------------------------------------- workbench

        [MenuItem("Pivot/Author/Prop 1 - Workbench")]
        public static void Workbench()
        {
            Scene scene = OpenLab();
            Transform environment = Environment(scene);
            if (environment == null) return;

            Replace(environment, "Workbench");

            GameObject group = new GameObject("Workbench");
            group.transform.SetParent(environment, false);

            Material topMaterial = PropMaterial("Prop_Workbench",
                new Color(0.24f, 0.20f, 0.42f), 0.62f);
            Material plinthMaterial = PropMaterial("Prop_Plinth",
                new Color(0.15f, 0.13f, 0.29f), 0.45f);

            // The top slab. Its upper face lands exactly on BenchTop so the tree and the
            // bin can be placed against a single known height.
            ProBuilderMesh top = ShapeGenerator.GenerateCube(
                PivotLocation.Center, new Vector3(1.9f, 0.11f, 0.78f));
            top.gameObject.name = "Bench Top";
            top.transform.SetParent(group.transform, false);
            top.transform.localPosition = new Vector3(0f, BenchTop - 0.055f, 0.06f);
            BevelAll(top, 0.03f);
            top.GetComponent<MeshRenderer>().sharedMaterial = topMaterial;
            SaveMesh(top, "Prop_BenchTop");

            // A single chunky plinth rather than four legs: fewer draws, and it reads as
            // one solid toy object instead of furniture.
            ProBuilderMesh plinth = ShapeGenerator.GenerateCube(
                PivotLocation.Center, new Vector3(1.42f, 0.9f, 0.52f));
            plinth.gameObject.name = "Bench Plinth";
            plinth.transform.SetParent(group.transform, false);
            plinth.transform.localPosition = new Vector3(0f, 0.45f, 0.06f);
            BevelAll(plinth, 0.05f);
            plinth.GetComponent<MeshRenderer>().sharedMaterial = plinthMaterial;
            SaveMesh(plinth, "Prop_BenchPlinth");

            foreach (Transform t in group.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }

            Finish(scene);
            Debug.Log("Pivot: workbench authored, top surface at y=" + BenchTop);
        }

        // --------------------------------------------------------------------- bin

        [MenuItem("Pivot/Author/Prop 2 - Orb Bin")]
        public static void OrbBin()
        {
            Scene scene = OpenLab();
            Transform environment = Environment(scene);
            if (environment == null) return;

            Replace(environment, "Orb Bin");

            GameObject group = new GameObject("Orb Bin");
            group.transform.SetParent(environment, false);
            group.transform.localPosition = new Vector3(0.66f, BenchTop, 0.06f);

            Material material = PropMaterial("Prop_Bin",
                new Color(0.30f, 0.24f, 0.52f), 0.70f);

            // An open tub, so loose orbs are visible sitting in it. A pipe is exactly
            // that: a cylinder with the middle removed and both ends open.
            ProBuilderMesh wall = ShapeGenerator.GeneratePipe(
                PivotLocation.Center, 0.23f, 0.26f, 0.035f, 24, 1);
            wall.gameObject.name = "Bin Wall";
            wall.transform.SetParent(group.transform, false);
            wall.transform.localPosition = new Vector3(0f, 0.13f, 0f);
            wall.GetComponent<MeshRenderer>().sharedMaterial = material;
            SaveMesh(wall, "Prop_BinWall");

            ProBuilderMesh floor = ShapeGenerator.GenerateCylinder(
                PivotLocation.Center, 24, 0.213f, 0.03f, 0);
            floor.gameObject.name = "Bin Floor";
            floor.transform.SetParent(group.transform, false);
            floor.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = material;
            SaveMesh(floor, "Prop_BinFloor");

            foreach (Transform t in group.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }

            Finish(scene);
            Debug.Log("Pivot: orb bin authored at " + group.transform.localPosition);
        }

        // ----------------------------------------------------------------- console

        [MenuItem("Pivot/Author/Prop 3 - Console")]
        public static void Console()
        {
            Scene scene = OpenLab();
            Transform environment = Environment(scene);
            if (environment == null) return;

            Replace(environment, "Console");

            GameObject group = new GameObject("Console");
            group.transform.SetParent(environment, false);
            group.transform.localPosition = new Vector3(-0.62f, BenchTop, -0.06f);

            Material bodyMaterial = PropMaterial("Prop_Console",
                new Color(0.20f, 0.17f, 0.38f), 0.66f);
            Material faceMaterial = PropMaterial("Prop_ConsoleFace",
                new Color(0.34f, 0.29f, 0.60f), 0.78f);

            // A wedge base so the panel leans back towards the user at a readable angle
            // without needing a separate stand.
            ProBuilderMesh wedge = ShapeGenerator.GenerateCube(
                PivotLocation.Center, new Vector3(0.62f, 0.10f, 0.30f));
            wedge.gameObject.name = "Console Base";
            wedge.transform.SetParent(group.transform, false);
            wedge.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            BevelAll(wedge, 0.025f);
            wedge.GetComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
            SaveMesh(wedge, "Prop_ConsoleBase");

            // The face the buttons will sit on later. Tilted 32 degrees back: shallow
            // enough to read from standing height, steep enough to reach comfortably.
            ProBuilderMesh face = ShapeGenerator.GenerateCube(
                PivotLocation.Center, new Vector3(0.60f, 0.035f, 0.26f));
            face.gameObject.name = "Console Face";
            face.transform.SetParent(group.transform, false);
            face.transform.localPosition = new Vector3(0f, 0.155f, -0.01f);
            face.transform.localRotation = Quaternion.Euler(-32f, 0f, 0f);
            BevelAll(face, 0.012f);
            face.GetComponent<MeshRenderer>().sharedMaterial = faceMaterial;
            SaveMesh(face, "Prop_ConsoleFace");

            foreach (Transform t in group.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }

            Finish(scene);
            Debug.Log("Pivot: console authored at " + group.transform.localPosition);
        }

        // ------------------------------------------------------------------ layout

        /// <summary>
        /// Drops the tree so its lowest row clears the bench top, and frames the camera
        /// on the bench. Run after the props exist, because it depends on their heights.
        /// </summary>
        [MenuItem("Pivot/Author/Prop 4 - Settle Layout")]
        public static void SettleLayout()
        {
            Scene scene = OpenLab();

            Transform tree = null;
            Camera camera = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "--- Tree ---") tree = root.transform;
                Camera found = root.GetComponentInChildren<Camera>(true);
                if (found != null) camera = found;
            }

            if (tree == null || camera == null)
            {
                Debug.LogError("Pivot: could not find the tree group or the camera.");
                return;
            }

            // Three depths at 0.28 m each puts the deepest row 0.56 m below the root, and
            // a node radius of 0.14 clears the bench surface.
            const float treeHeight = 0.56f;
            const float clearance = 0.22f;
            tree.position = new Vector3(0f, BenchTop + clearance + treeHeight, 0.06f);

            camera.transform.position = new Vector3(0f, 1.55f, -1.75f);
            camera.transform.rotation = Quaternion.Euler(6f, 0f, 0f);

            Finish(scene);
            Debug.Log("Pivot: tree root at " + tree.position + ", camera settled.");
        }
    }
}
