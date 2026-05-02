using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

[RequireComponent(typeof(FOWFogRenderer))]
public class FOWManager : MonoBehaviour
{
    public static FOWManager instance_;

    /// <summary>
    /// 战争迷雾的网格大小，通常覆盖整个场景地图
    /// </summary>
    public Vector2                          FOG_SIZE = new Vector2(200, 200);
    /// <summary>
    /// 地图单个网格的大小，影响实际创建的地图数据，单个网格越小迷雾越精细，性能越差
    /// </summary>
    public float                            MAP_TILE_SIZE = 1.0f;
    /// <summary>
    /// 网格相对于单个网格的尺寸，宽和高各有多少单个网格组成
    /// </summary>
    public Vector2Int                       GRID_RECT => new Vector2Int((int)(FOG_SIZE.x / MAP_TILE_SIZE), (int)(FOG_SIZE.y / MAP_TILE_SIZE));

    /// <summary> 更新频率 </summary>
    public float                            update_time_ = 0.2f;
    /// <summary> 是否储存已探索区域 </summary>
    public bool                             is_save_explored_ = true;

    // --- editor相关 ---
    /// <summary> 是否唤醒时生成FOW </summary>
    public bool                             awake_generate_ = true;

    // --- 地图相关 ---
    /// <summary> 迷雾地图 </summary>
    public FOWMap                           map_;
    /// <summary> 网格左上角到中心的距离 </summary>
    public float                            HALF_LENGTH_X => (int)(FOG_SIZE.x / MAP_TILE_SIZE) / 2f * MAP_TILE_SIZE;
    /// <summary> 网格左上角到中心的距离 </summary>
    public float                            HALF_LENGTH_Y => (int)(FOG_SIZE.y / MAP_TILE_SIZE) / 2f * MAP_TILE_SIZE;
    /// <summary> 碰撞检测的半径 </summary>
    public Vector3                          HALF_EXTENTS => new Vector3(MAP_TILE_SIZE / 2f, 0.01f, MAP_TILE_SIZE / 2f);
    /// <summary> 障碍物层级 </summary>
    public LayerMask                        block_layer_ = -1;

    // --- 着色相关 ---
    /// <summary> 迷雾着色器 </summary>
    public Shader                           fog_shader_;
    /// <summary> 迷雾颜色 </summary>
    public Color32                          fog_color_ = Color.black;
    /// <summary> 不可见区域透明度 </summary>
    public byte                             invisible_alpha_ = 255;
    /// <summary> 可见区域透明度 </summary>
    public byte                             visible_alpha_ = 0;
    /// <summary> 已探索区域透明度 </summary>
    public byte                             explored_alpha_ = 128;


    private List<FOWViewer>                 viewer_list_ = new List<FOWViewer>();
    private Vector2Int[]                    viewer_grid_pos_cache_;
    public bool                             generated_fow_ = false;


    private void Awake()
    {
        instance_ = this;

        /// 查找场景中所有观察者组件
        viewer_list_ = FindObjectsByType<FOWViewer>().ToList();
    }
    private void Start()
    {
        if (awake_generate_) GenerateMapFOW();
    }
    protected void OnDestroy()
    {
        map_?.Release();
    }


    // ========================================================
    // 公共接口
    // ========================================================
    public void GenerateMapFOW()
    {
        CancelInvoke();
        StopAllCoroutines();

        InitMap();                                              // 初始化地图
        InvokeRepeating(nameof(FogUpdate), 0, update_time_);    // 开始更新地图
        StartCoroutine(StartLerp());                            // 开始渲染平滑协程
        StartCoroutine(SetAllCullingObjState(false));
        generated_fow_ = true;
    }
    public void ClearMapFOW()
    {
        CancelInvoke();
        map_?.Release();

        for (int i = 0; i < viewer_list_.Count; ++i)
        {
            viewer_list_[i].ResetViewer();
        }

        StartCoroutine(SetAllCullingObjState(true));
        generated_fow_ = false;
    }
    public void AddViewer(FOWViewer viewer)
    {
        if (!viewer_list_.Contains(viewer))
        {
            viewer_list_.Add(viewer);
        }
    }
    public void RemoveViewer(FOWViewer viewer)
    {
        if (viewer_list_.Contains(viewer))
        {
            viewer_list_.Remove(viewer);
        }
    }


    // ========================================================
    // 网格更新（精简五步流程）
    // 1. 清零 → 2. 各观察者独立计算OR写入 → 3. 标记已探索 → 4. 应用颜色+模糊 → 5. Culling
    // ========================================================
    public void FogUpdate()
    {
        if (map_ == null || viewer_list_.Count == 0) return;
        int viewer_count = viewer_list_.Count;

        // 缓存观察者网格位置
        if (viewer_grid_pos_cache_ == null || viewer_grid_pos_cache_.Length != viewer_count)
            viewer_grid_pos_cache_ = new Vector2Int[viewer_count];
        for (int i = 0; i < viewer_count; i++)
            viewer_grid_pos_cache_[i] = ScenePos2GridPos(viewer_list_[i].transform.position);

        // 步骤1：清零所有可见性标记
#if UNITY_EDITOR
        Profiler.BeginSample("FogUpdate.Reset");
#endif
        map_.ResetAllVisible();
#if UNITY_EDITOR
        Profiler.EndSample();
#endif

        // 步骤2：每个观察者独立计算可见性，直接OR写入visible_flags_（只写1，绝不覆盖）
#if UNITY_EDITOR
        Profiler.BeginSample("FogUpdate.ComputeFlags");
#endif
        for (int i = 0; i < viewer_count; ++i)
        {
            map_.ComputeFlags(
                i,
                viewer_grid_pos_cache_[i].x,
                viewer_grid_pos_cache_[i].y,
                viewer_list_[i].ViewerRange / MAP_TILE_SIZE
            );
        }
#if UNITY_EDITOR
        Profiler.EndSample();
#endif

        // 步骤3：标记已探索（一次遍历O(n)，仅在需要时执行）
        if (is_save_explored_)
        {
#if UNITY_EDITOR
            Profiler.BeginSample("FogUpdate.MarkExplored");
#endif
            map_.MarkExplored();
#if UNITY_EDITOR
            Profiler.EndSample();
#endif
        }

        // 步骤4：应用颜色 + 模糊
#if UNITY_EDITOR
        Profiler.BeginSample("FogUpdate.ApplyFOW");
#endif
        for (int i = 0; i < viewer_count; ++i)
            map_.ApplyFOW(viewer_grid_pos_cache_[i].x, viewer_grid_pos_cache_[i].y,
                (int)(viewer_list_[i].ViewerRange / MAP_TILE_SIZE) + 20);
        map_.Blur();
#if UNITY_EDITOR
        Profiler.EndSample();
#endif

        // 步骤5：更新Culling组件
#if UNITY_EDITOR
        Profiler.BeginSample("FogUpdate.Culling");
#endif
        for (int i = 0; i < viewer_count; ++i)
        {
            List<FOWCulling> list = viewer_list_[i].CullingCompList;
            for (int j = list.Count - 1; j >= 0; --j)
            {
                var comp = list[j];
                if (comp == null) { list.RemoveAt(j); continue; }
                Vector2Int pos = ScenePos2GridPos(comp.transform.position);
                comp.SetRenderEnabled(map_.CanDisplay(pos.x, pos.y));
            }
        }
#if UNITY_EDITOR
        Profiler.EndSample();
#endif
    }


    // ========================================================
    // 坐标转换
    // ========================================================
    public Vector2Int ScenePos2GridPos(Vector3 pos)
    {
        int x = (int)((pos.x - transform.position.x + HALF_LENGTH_X) / MAP_TILE_SIZE);
        int y = (int)((pos.z - transform.position.z + HALF_LENGTH_Y) / MAP_TILE_SIZE);
        return new Vector2Int(x, y);
    }
    public Vector3 GridPos2ScenePos(Vector2Int pos)
    {
        return
            new Vector3(pos.x * MAP_TILE_SIZE, 0, pos.y * MAP_TILE_SIZE)
            + new Vector3(MAP_TILE_SIZE / 2, 0, MAP_TILE_SIZE / 2)
            + transform.position
            - new Vector3(FOG_SIZE.x / 2, 0, FOG_SIZE.y / 2);
    }
    public Vector3 ScenePos2TileScenePos(Vector3 pos)
    {
        return GridPos2ScenePos(ScenePos2GridPos(pos));
    }


    // ========================================================
    // 内部协程
    // ========================================================
    IEnumerator StartLerp()
    {
        while (true)
        {
            yield return null;
            map_.Lerp();
        }
    }
    IEnumerator SetAllCullingObjState(bool enable)
    {
        List<FOWCulling> all_comp = FindObjectsByType<FOWCulling>().ToList();
        while (all_comp.Count > 0)
        {
            int count = 10;
            while (count > 0 && all_comp.Count > 0)
            {
                all_comp[^1].SetRenderEnabled(enable);
                all_comp.RemoveAt(all_comp.Count - 1);
                count--;
            }
            yield return null;
        }
    }


    // ========================================================
    // 地图初始化
    // ========================================================
    private void InitMap()
    {
        map_?.Release();
        map_ = new FOWMap();
        map_.InitMap(this);
    }


    // ========================================================
    // Editor
    // ========================================================
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(transform.position, new Vector3(FOG_SIZE.x, 0f, FOG_SIZE.y));
        if (map_ == null)
            return;

        foreach (var viewer in viewer_list_)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawCube(ScenePos2TileScenePos(viewer.transform.position), new Vector3(MAP_TILE_SIZE - 0.02f, 1f, MAP_TILE_SIZE - 0.02f));

            int r = (int)(viewer.ViewerRange / MAP_TILE_SIZE);
            int area = r * r;
            Vector2Int viewer_pos = ScenePos2GridPos(viewer.transform.position);

            for (int i = 0; i < GRID_RECT.x; i++)
            {
                for (int j = 0; j < GRID_RECT.y; j++)
                {
                    FOWTile tile = map_.GetTile(i, j);

                    if (tile.Distance(viewer_pos.x, viewer_pos.y) > area)
                        continue;

                    Gizmos.color = map_.BlockData[i, j] == 1 ? Color.red : Color.white;
                    Gizmos.DrawWireCube(GridPos2ScenePos(new Vector2Int(i, j)), new Vector3(MAP_TILE_SIZE - 0.02f, 0f, MAP_TILE_SIZE - 0.02f));
                }
            }
        }
    }
#endif
}