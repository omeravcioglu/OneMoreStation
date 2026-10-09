using UnityEngine;
using UnityEngine.Rendering;

namespace Metro
{
    /// <summary>One dynamic mesh drawn with the vector shader at a fixed render-queue position.</summary>
    public sealed class MeshLayer
    {
        public readonly GameObject go;
        public readonly Mesh mesh;
        public readonly Material material;
        public readonly MeshRenderer renderer;
        public readonly VectorMeshBuilder b = new VectorMeshBuilder();

        public MeshLayer(string name, Transform parent, Shader shader, int queue)
        {
            go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;
            material = new Material(shader) { renderQueue = queue, name = name };
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
        }

        public static Bounds WorldBounds(CityModel city) =>
            new Bounds(new Vector3(city.width * 0.5f, city.height * 0.5f, 0f), new Vector3(city.width + 400f, city.height + 400f, 100f));

        public void Commit(Bounds bounds) => b.Apply(mesh, bounds);

        public bool Visible
        {
            get => renderer.enabled;
            set => renderer.enabled = value;
        }

        public void SetTint(Color c) => material.SetColor("_Tint", c);

        public void Destroy()
        {
            Object.Destroy(mesh);
            Object.Destroy(material);
            Object.Destroy(go);
        }
    }
}
