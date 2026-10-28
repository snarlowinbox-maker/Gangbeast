using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using BeefCity.Core;
using BeefCity.Ragdoll;
using BeefCity.Stages;

namespace BeefCity.AI
{
    /// <summary>
    /// Navigation for the procedurally built stages.
    /// 1) Bakes a Unity NavMesh at runtime from the stage's colliders (kill zones are marked not walkable) and asks
    ///    NavMesh.CalculatePath for routes.
    /// 2) Also builds a multi-level A* grid (raycast-sampled floors, kill-zone cells blocked, edge cells penalised).
    ///    It is used whenever the NavMesh is unavailable or cannot reach the goal, and it handles stages with several
    ///    floor levels (Parking, Tower, Subway) without any authoring.
    /// Built once per stage instance; the match rebuilds it whenever the stage is rebuilt.
    /// </summary>
    public sealed class ArenaNavigation
    {
        public static ArenaNavigation Current { get; private set; }

        const float Cell = 1.25f;
        const int Layers = 4;
        const float StepHeight = 0.75f;

        struct HeapNode
        {
            public float F;
            public int Id;
        }

        NavMeshDataInstance _navInstance;
        bool _hasNavMesh;
        readonly NavMeshPath _navPath = new NavMeshPath();

        Bounds _bounds;
        int _w, _h;
        float[] _height;     // [cell * Layers + layer], NaN = no surface
        bool[] _blocked;
        float[] _edgeCost;

        // A* scratch (reused so searches do not allocate)
        int _stamp;
        int[] _seen;
        float[] _g;
        int[] _parent;
        bool[] _closed;
        readonly List<HeapNode> _heap = new List<HeapNode>();
        readonly List<int> _nodeBuffer = new List<int>();

        public bool HasNavMesh => _hasNavMesh;

        // ------------------------------------------------------------------ lifetime

        public static ArenaNavigation Build(ArenaInfo arena)
        {
            DisposeCurrent();
            var nav = new ArenaNavigation();
            nav.Init(arena);
            Current = nav;
            return nav;
        }

        public static void DisposeCurrent()
        {
            if (Current != null) Current.Dispose();
        }

        public void Dispose()
        {
            if (_hasNavMesh)
            {
                NavMesh.RemoveNavMeshData(_navInstance);
                _hasNavMesh = false;
            }
            if (Current == this) Current = null;
        }

        void Init(ArenaInfo arena)
        {
            ComputeBounds(arena);
            BakeNavMesh(arena);
            BuildGrid();
        }

        void ComputeBounds(ArenaInfo arena)
        {
            bool any = false;
            Bounds b = new Bounds(arena.center, Vector3.one);
            if (arena.root != null)
            {
                foreach (Collider c in arena.root.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger) continue;
                    if (!any) { b = c.bounds; any = true; }
                    else b.Encapsulate(c.bounds);
                }
            }
            if (!any) b = new Bounds(arena.center, new Vector3(arena.radius * 2f, 20f, arena.radius * 2f));

            Vector3 size = b.size;
            size.x = Mathf.Min(size.x, 100f);
            size.z = Mathf.Min(size.z, 100f);
            b.size = size;
            _bounds = b;
        }

        // ------------------------------------------------------------------ NavMesh

        void BakeNavMesh(ArenaInfo arena)
        {
            if (arena.root == null) return;
            try
            {
                var sources = new List<NavMeshBuildSource>();
                var markups = new List<NavMeshBuildMarkup>();
                foreach (KillZone kz in arena.root.GetComponentsInChildren<KillZone>())
                    markups.Add(new NavMeshBuildMarkup { root = kz.transform, area = 1, overrideArea = true, applyToChildren = true });

                NavMeshBuilder.CollectSources(arena.root.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
                if (sources.Count == 0) return;

                NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
                settings.agentRadius = 0.5f;
                settings.agentHeight = 1.9f;
                settings.agentClimb = 0.5f;
                settings.agentSlope = 42f;

                Bounds bake = _bounds;
                bake.Expand(2f);
                NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bake, Vector3.zero, Quaternion.identity);
                if (data == null) return;
                _navInstance = NavMesh.AddNavMeshData(data);
                _hasNavMesh = _navInstance.valid;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Navigation] NavMesh bake failed, using the A* grid only: " + e.Message);
                _hasNavMesh = false;
            }
        }

        bool TryNavMeshPath(Vector3 from, Vector3 to, List<Vector3> corners)
        {
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 3f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out NavMeshHit b, 3f, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, _navPath)) return false;
            if (_navPath.status != NavMeshPathStatus.PathComplete) return false;
            corners.AddRange(_navPath.corners);
            return corners.Count > 0;
        }

        // ------------------------------------------------------------------ A* grid

        void BuildGrid()
        {
            _w = Mathf.Max(1, Mathf.CeilToInt(_bounds.size.x / Cell));
            _h = Mathf.Max(1, Mathf.CeilToInt(_bounds.size.z / Cell));
            int cells = _w * _h;
            int nodes = cells * Layers;

            _height = new float[nodes];
            _blocked = new bool[nodes];
            _edgeCost = new float[nodes];
            _seen = new int[nodes];
            _g = new float[nodes];
            _parent = new int[nodes];
            _closed = new bool[nodes];
            for (int i = 0; i < nodes; i++) _height[i] = float.NaN;

            var hits = new RaycastHit[32];
            var overlap = new Collider[8];
            var heights = new List<float>(8);
            Vector3 min = _bounds.min;
            float top = _bounds.max.y + 3f;
            float range = _bounds.size.y + 6f;

            for (int z = 0; z < _h; z++)
            {
                for (int x = 0; x < _w; x++)
                {
                    var origin = new Vector3(min.x + (x + 0.5f) * Cell, top, min.z + (z + 0.5f) * Cell);
                    int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, range, ~0, QueryTriggerInteraction.Ignore);

                    heights.Clear();
                    for (int k = 0; k < n; k++)
                    {
                        if (hits[k].normal.y < 0.7f || IsEntity(hits[k].collider)) continue;
                        heights.Add(hits[k].point.y);
                    }
                    heights.Sort((p, q) => q.CompareTo(p)); // highest first

                    int layer = 0;
                    float last = float.NaN;
                    foreach (float y in heights)
                    {
                        if (layer >= Layers) break;
                        if (!float.IsNaN(last) && last - y < 1.2f) continue; // thin stacked surfaces count as one floor
                        last = y;

                        int id = (z * _w + x) * Layers + layer;
                        _height[id] = y;

                        Vector3 p = new Vector3(origin.x, y + 0.6f, origin.z);
                        int cnt = Physics.OverlapSphereNonAlloc(p, 0.7f, overlap, ~0, QueryTriggerInteraction.Collide);
                        for (int m = 0; m < cnt; m++)
                        {
                            if (overlap[m].GetComponent<KillZone>() != null) { _blocked[id] = true; break; }
                        }
                        layer++;
                    }
                }
            }

            // Edge penalty: cells next to a drop cost more, so paths keep away from the rim.
            for (int z = 0; z < _h; z++)
            {
                for (int x = 0; x < _w; x++)
                {
                    for (int layer = 0; layer < Layers; layer++)
                    {
                        int id = (z * _w + x) * Layers + layer;
                        if (float.IsNaN(_height[id])) continue;
                        float cost = 0f;
                        cost += HasNeighborSurface(x + 1, z, _height[id]) ? 0f : 3f;
                        cost += HasNeighborSurface(x - 1, z, _height[id]) ? 0f : 3f;
                        cost += HasNeighborSurface(x, z + 1, _height[id]) ? 0f : 3f;
                        cost += HasNeighborSurface(x, z - 1, _height[id]) ? 0f : 3f;
                        _edgeCost[id] = cost;
                    }
                }
            }
        }

        static bool IsEntity(Collider c)
        {
            if (c.GetComponentInParent<RagdollRig>() != null) return true;
            Rigidbody rb = c.attachedRigidbody;
            return rb != null && !rb.isKinematic && rb.mass < 100f; // props and loose cars are not floors
        }

        bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < _w && z < _h;

        bool TryLayer(int x, int z, float refY, float tolerance, out int node)
        {
            node = -1;
            if (!InGrid(x, z)) return false;
            float best = tolerance;
            for (int layer = 0; layer < Layers; layer++)
            {
                int id = (z * _w + x) * Layers + layer;
                float h = _height[id];
                if (float.IsNaN(h) || _blocked[id]) continue;
                float d = Mathf.Abs(h - refY);
                if (d <= best) { best = d; node = id; }
            }
            return node >= 0;
        }

        bool HasNeighborSurface(int x, int z, float refY) => TryLayer(x, z, refY, StepHeight, out _);

        bool WorldToCell(Vector3 p, out int x, out int z)
        {
            x = Mathf.FloorToInt((p.x - _bounds.min.x) / Cell);
            z = Mathf.FloorToInt((p.z - _bounds.min.z) / Cell);
            return InGrid(x, z);
        }

        Vector3 NodePosition(int id)
        {
            int cell = id / Layers;
            int z = cell / _w;
            int x = cell % _w;
            return new Vector3(_bounds.min.x + (x + 0.5f) * Cell, _height[id], _bounds.min.z + (z + 0.5f) * Cell);
        }

        bool NearestNode(Vector3 p, out int node)
        {
            node = -1;
            if (!WorldToCell(p, out int cx, out int cz)) return false;
            float refY = p.y - 0.9f; // positions are pelvis heights; surfaces are ~0.9 m lower
            for (int r = 0; r <= 2; r++)
            {
                float best = float.MaxValue;
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        if (!TryLayer(cx + dx, cz + dz, refY, 2.2f, out int id)) continue;
                        float d = Mathf.Abs(_height[id] - refY) + (Mathf.Abs(dx) + Mathf.Abs(dz)) * 0.3f;
                        if (d < best) { best = d; node = id; }
                    }
                }
                if (node >= 0) return true;
            }
            return false;
        }

        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        bool FindGridPath(Vector3 from, Vector3 to, List<Vector3> result)
        {
            if (_height == null) return false;
            if (!NearestNode(from, out int start) || !NearestNode(to, out int goal)) return false;
            if (start == goal)
            {
                result.Add(to);
                return true;
            }

            _stamp++;
            _heap.Clear();
            Vector3 goalPos = NodePosition(goal);
            _seen[start] = _stamp;
            _g[start] = 0f;
            _parent[start] = -1;
            _closed[start] = false;
            Push(new HeapNode { F = Vector3.Distance(NodePosition(start), goalPos), Id = start });

            int expansions = 0;
            while (_heap.Count > 0 && expansions++ < 8000)
            {
                int cur = Pop().Id;
                if (_seen[cur] == _stamp && _closed[cur]) continue;
                _closed[cur] = true;
                if (cur == goal) return Reconstruct(start, goal, to, result);

                int cell = cur / Layers;
                int cz = cell / _w;
                int cx = cell % _w;
                float curY = _height[cur];

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Dx[d], nz = cz + Dz[d];
                    if (!TryLayer(nx, nz, curY, StepHeight, out int next)) continue;
                    if (d >= 4) // diagonal moves must not cut a corner
                    {
                        if (!TryLayer(cx + Dx[d], cz, curY, StepHeight, out _)) continue;
                        if (!TryLayer(cx, cz + Dz[d], curY, StepHeight, out _)) continue;
                    }

                    float step = (d >= 4 ? 1.414f : 1f) * Cell + _edgeCost[next] + Mathf.Abs(_height[next] - curY);
                    float g = _g[cur] + step;
                    if (_seen[next] == _stamp)
                    {
                        if (_closed[next] || g >= _g[next]) continue;
                    }
                    else
                    {
                        _seen[next] = _stamp;
                        _closed[next] = false;
                    }
                    _g[next] = g;
                    _parent[next] = cur;
                    Push(new HeapNode { F = g + Vector3.Distance(NodePosition(next), goalPos), Id = next });
                }
            }
            return false;
        }

        bool Reconstruct(int start, int goal, Vector3 target, List<Vector3> result)
        {
            _nodeBuffer.Clear();
            for (int n = goal; n != -1; n = _parent[n]) _nodeBuffer.Add(n);
            for (int i = _nodeBuffer.Count - 1; i >= 0; i--) result.Add(NodePosition(_nodeBuffer[i]));
            result.Add(target);
            return result.Count > 0;
        }

        // Binary min-heap on F
        void Push(HeapNode n)
        {
            _heap.Add(n);
            int i = _heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_heap[parent].F <= _heap[i].F) break;
                (_heap[parent], _heap[i]) = (_heap[i], _heap[parent]);
                i = parent;
            }
        }

        HeapNode Pop()
        {
            HeapNode top = _heap[0];
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, smallest = i;
                if (l < _heap.Count && _heap[l].F < _heap[smallest].F) smallest = l;
                if (r < _heap.Count && _heap[r].F < _heap[smallest].F) smallest = r;
                if (smallest == i) break;
                (_heap[smallest], _heap[i]) = (_heap[i], _heap[smallest]);
                i = smallest;
            }
            return top;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Path between two world positions (pelvis height is fine). NavMesh first, A* grid as the fallback.</summary>
        public bool TryPath(Vector3 from, Vector3 to, List<Vector3> corners)
        {
            corners.Clear();
            if (_hasNavMesh && TryNavMeshPath(from, to, corners)) return true;
            corners.Clear();
            return FindGridPath(from, to, corners);
        }
    }
}
