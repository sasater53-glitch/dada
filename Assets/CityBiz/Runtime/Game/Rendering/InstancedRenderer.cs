using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBiz.Game
{
    /// <summary>
    /// GPU Instancing для повторяющихся объектов (деревья, фонари, скамейки, урны, столбики…):
    /// экземпляры сгруппированы по (меш, подмеш, материал, ячейка 48×48 м); каждая группа — один вызов
    /// Graphics.RenderMeshInstanced с отсечением по frustum и дистанции. Данные сериализуются при запекании.
    /// </summary>
    public sealed class InstancedRenderer : MonoBehaviour
    {
        const float CellSize = 48f;
        const int MaxPerBatch = 511;

        [Serializable]
        public sealed class Batch
        {
            public Mesh Mesh;
            public int Submesh;
            public Material Material;
            public bool Shadows = true;
            public float MaxDistance = 260f;
            public Bounds Bounds;
            public List<Matrix4x4> Matrices = new List<Matrix4x4>();
        }

        [SerializeField] List<Batch> _batches = new List<Batch>();
        readonly Plane[] _planes = new Plane[6];
        public int DrawnBatches { get; private set; }
        public int TotalInstances { get; private set; }

        public IReadOnlyList<Batch> Batches => _batches;

        public void Clear() => _batches.Clear();

        public void Add(Mesh mesh, Material[] materials, Matrix4x4 matrix, bool shadows = true, float maxDistance = 260f)
        {
            var pos = matrix.GetColumn(3);
            var cell = new Vector2Int(Mathf.FloorToInt(pos.x / CellSize), Mathf.FloorToInt(pos.z / CellSize));
            var worldBounds = TransformBounds(mesh.bounds, matrix);
            for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
            {
                var batch = Find(mesh, s, materials[s], cell, shadows, maxDistance);
                if (batch.Matrices.Count == 0) batch.Bounds = worldBounds; else batch.Bounds.Encapsulate(worldBounds);
                batch.Matrices.Add(matrix);
            }
        }

        Batch Find(Mesh mesh, int sub, Material mat, Vector2Int cell, bool shadows, float maxDistance)
        {
            for (int i = _batches.Count - 1; i >= 0; i--)
            {
                var b = _batches[i];
                if (b.Mesh == mesh && b.Submesh == sub && b.Material == mat && b.Matrices.Count < MaxPerBatch &&
                    Mathf.FloorToInt(b.Bounds.center.x / CellSize) == cell.x && Mathf.FloorToInt(b.Bounds.center.z / CellSize) == cell.y)
                    return b;
            }
            mat.enableInstancing = true;
            var nb = new Batch { Mesh = mesh, Submesh = sub, Material = mat, Shadows = shadows, MaxDistance = maxDistance };
            _batches.Add(nb);
            return nb;
        }

        static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            var center = m.MultiplyPoint3x4(b.center);
            var ext = b.extents;
            var x = m.MultiplyVector(new Vector3(ext.x, 0, 0));
            var y = m.MultiplyVector(new Vector3(0, ext.y, 0));
            var z = m.MultiplyVector(new Vector3(0, 0, ext.z));
            var size = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(center, size * 2f);
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            var camPos = cam.transform.position;
            int drawn = 0, total = 0;
            foreach (var b in _batches)
            {
                if (b.Mesh == null || b.Material == null || b.Matrices.Count == 0) continue;
                total += b.Matrices.Count;
                if (b.Bounds.SqrDistance(camPos) > b.MaxDistance * b.MaxDistance) continue;
                if (!GeometryUtility.TestPlanesAABB(_planes, b.Bounds)) continue;
                var rp = new RenderParams(b.Material)
                {
                    worldBounds = b.Bounds,
                    shadowCastingMode = b.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = gameObject.layer,
                };
                Graphics.RenderMeshInstanced(rp, b.Mesh, b.Submesh, b.Matrices);
                drawn++;
            }
            DrawnBatches = drawn;
            TotalInstances = total;
        }
    }
}
