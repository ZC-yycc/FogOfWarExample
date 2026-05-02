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
    /// <summary>
    /// 单个网格的半长方形
    /// </summary>
    public Vector3                          HALF_EXTENTS => new Vector3(MAP_TILE_SIZE - 0.02f, 0f, MAP_TILE_SIZE - 0.02f) * 0.5f;
    /// <summary>
    /// 战争迷雾的网格大小的一半
    /// </summary>
    public float                            HALF_LENGTH_X => FOG_SIZE.x * 0.5f;
    public float                            HALF_LENGTH_Y => FOG_SIZE.y * 0.5f;
    /// <summary>
    /// 观察者列表，网格地图上的观察者对象，观察到的网格被标记为已探索，FOWViewer为观察者挂载在玩家身上
    /// </summary>
    [SerializeField] 
    private List<FOWViewer>                 viewer_list_ = new List<FOWViewer>();
    /// <summary>
    /// 网格地图
    /// </summary>
    public FOWMap                           map_;
    /// <summary>
    /// 迷雾的算法渲染器
    /// </summary>
    public Shader                           fog_shader_;
    /// <summary>
    /// 是否保存已探索的区域
    /// </summary>
    [Header("是否保存已探索的区域")]
    public bool                             is_save_explored_ = false;
    /// <summary>
    /// 探索过的位置迷雾的透明度
    /// </summary>
    [Header("探索过的位置迷雾的透明度")]
    public byte                             explored_alpha_ = 200;
    /// <summary>
    /// 可视区域的迷雾透明度
    /// </summary>
    [Header("可视区域的迷雾透明度")]
    public byte                             visible_alpha_  = 0;
    /// <summary>
    /// 不可视区域的迷雾透明度
    /// </summary>
    [Header("不可视区域的迷雾透明度")]
    public byte                             invisible_alpha_ = 255;
    /// <summary>
    /// 迷雾颜色
    /// </summary>
    [Header("迷雾颜色(透明度无效)")]
    public Color32                          fog_color_ = new Color32(0, 0, 0, 0);
    /// <summary>
    /// 障碍物的碰撞检测图层，初始化时检测障碍物
    /// </summary>
    [Header("障碍物的碰撞检测图层，初始化时检测障碍物")]
    public LayerMask                        block_layer_ = 1 << 8 | 1 << 10;
    /// <summary>
    /// 迷雾的更新时间间隔，数值越小响应越快，性能越差
    /// </summary>
    [Header("迷雾的更新时间间隔，数值越小响应越快，性能越差")]
    public float                            update_time_ = 0.05f;

    [Header("是否在场景开始时生成")]
    public bool                             awake_generate_ = false;

    private bool                            generated_fow_ = false;
    /// <summary>
    /// 观察者位置缓存，避免每次更新都进行位置计算
    /// </summary>
    private int[,]                          viewer_pos_cache_;



    private int[,] ViewerPosCache
    {
        get
        {
            if (viewer_pos_cache_ == null || viewer_pos_cache_.GetLength(0) != viewer_list_.Count)
            {
                viewer_pos_cache_ = new int[viewer_list_.Count, 2];
            }
            for (int i = 0; i < viewer_list_.Count; i++)
            {
                var pos = ScenePos2GridPos(viewer_list_[i].transform.position);
                viewer_pos_cache_[i, 0] = pos[0];
                viewer_pos_cache_[i, 1] = pos[1];
            }
            return viewer_pos_cache_;
        }
    }
    public bool GeneratedFow => generated_fow_;





    private void Reset()
    {
        FOG_SIZE = new Vector2(200, 200);
        MAP_TILE_SIZE = 1.0f;
        block_layer_ = 1 << 8 | 1 << 10;
        update_time_ = 0.05f;
        is_save_explored_ = false;
        explored_alpha_ = 200;
    }
    private void Awake()
    {
        if(instance_ != null)
        {
            Destroy(gameObject);
            return;
        }
        instance_ = this;

        /// 查找场景中所有观察者组件
        viewer_list_ = FindObjectsByType<FOWViewer>().ToList();
    }
    private void Start()
    {
        if(awake_generate_) GenerateMapFOW();
    }
    protected void OnDestroy()
    {
        map_?.Release();
    }
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
    IEnumerator StartLerp()
    {
        while (true)
        {
            yield return null;
            map_.Lerp();
        }
    }
    public void ClearMapFOW()
    {
        CancelInvoke();
        map_?.Release();

        for(int i = 0; i < viewer_list_.Count; ++i)
        {
            viewer_list_[i].ResetViewer();
        }

        StartCoroutine(SetAllCullingObjState(true));
        generated_fow_ = false;
    }
    IEnumerator SetAllCullingObjState(bool enable)
    { 
        List<FOWCulling> all_comp = FindObjectsByType<FOWCulling>().ToList();
        while(all_comp.Count > 0)
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




    /// <summary>
    /// 数据初始化，包括 map_data_ 的初始化，对 map_data_ 的所有网格进行碰撞遍历，与障碍物碰撞的网格被标记为 1 否则为 0
    /// 使用 block_layer_ 进行碰撞检测
    /// </summary>
    private void InitMap()
    {
        map_?.Release();
        map_ = new FOWMap();
        map_.InitMap(this);
    }

    /// <summary>
    /// 网格的更新函数，更新频率受到 update_time_ 的影响
    /// </summary>
    public void FogUpdate()
    {
        if (map_ == null) return;

        // 获取所有观察者的网格位置
        int[,] viewer_grid_pos = ViewerPosCache;



        // 刷新迷雾
        for (int i = 0; i < viewer_list_.Count; ++i)
        {
            map_.FreshFog(i, is_save_explored_);
        }



        // 计算可见性
        Profiler.BeginSample("FogUpdate.ComputeFlags");
        for (int i = 0; i < viewer_list_.Count; ++i)
        {
            map_.ComputeFlags(
                i,
                viewer_grid_pos[i, 0],
                viewer_grid_pos[i, 1],
                viewer_list_[i].ViewerRange / MAP_TILE_SIZE
            );
        }
        Profiler.EndSample();




        // 应用迷雾到贴图
        for (int i = 0; i < viewer_list_.Count; ++i)
        {
            map_.ApplyFOW(
                viewer_grid_pos[i, 0],
                viewer_grid_pos[i, 1],
                (int)(viewer_list_[i].ViewerRange / MAP_TILE_SIZE) + 20
            );
        }




        // 模糊贴图
        map_.Blur();





        // 更新组件显示状态
        for (int i = 0; i < viewer_list_.Count; ++i)
        {
            List<FOWCulling> list = viewer_list_[i].CullingCompList;
            for (int j = list.Count - 1; j >= 0; --j)
            {
                var comp = list[j];
                if (comp == null)
                {
                    list.RemoveAt(j);
                    continue;
                }

                int[] pos = ScenePos2GridPos(comp.transform.position);
                comp.SetRenderEnabled(map_.CanDisplay(pos[0], pos[1]));
            }
        }
    }





    /// <summary>
    /// 添加观察者
    /// </summary>
    /// <param name="viewer"></param>
    public void AddViewer(FOWViewer viewer)
    {
        if (!viewer_list_.Contains(viewer))
        {
            viewer_list_.Add(viewer);
        }
    }
    /// <summary>
    /// 移除观察者
    /// </summary>
    /// <param name="viewer"></param>
    public void RemoveViewer(FOWViewer viewer)
    {
        if (viewer_list_.Contains(viewer))
        {
            viewer_list_.Remove(viewer);
        }
    }






    /// <summary>
    /// 将世界坐标转换为网格坐标
    ///  计算出相对于网格左下角的位置，除以网格大小得到网格数量，转成int截断不满一格的数据，得到网格位置
    /// </summary>
    /// <param name="viewer"></param>
    /// <returns></returns>
    public int[] ScenePos2GridPos(Vector3 pos)
    {
        var x = (int)((pos.x - transform.position.x + HALF_LENGTH_X) / MAP_TILE_SIZE);
        var y = (int)((pos.z - transform.position.z + HALF_LENGTH_Y) / MAP_TILE_SIZE);
        return new int[] { x, y };
    }
    /// <summary>
    /// 将网格位置转化为世界坐标位置，左下角网格 (0,0) 为起始点，第一个点 (0,0) 先计算出位于局部空间的坐标(0,0,0)
    /// 再减去整个网格大小的二分之一，得到左下角的网格位置，其余点同理得到位置
    /// </summary>
    /// <param name="pos"></param>
    /// <returns></returns>
    public Vector3 GridPos2ScenePos(int[] pos)
    {
        return 
            new Vector3(pos[0] * MAP_TILE_SIZE, 0, pos[1] * MAP_TILE_SIZE)      // 网格位置 x * 单个网格的大小 得到网格世界坐标的 x，同理得到 y，注意得到的是网格边缘的 x 和 y
            + new Vector3(MAP_TILE_SIZE / 2, 0, MAP_TILE_SIZE / 2)              // 网格中心点位置偏移，得到网格的中心点的局部坐标
            + transform.position                                                // 将网格的局部坐标加上父对象的坐标，得到世界位置的坐标
            - new Vector3(FOG_SIZE.x / 2, 0, FOG_SIZE.y / 2);                   // 减去网格的二分之一大小，得到网格的世界坐标
    }
    /// <summary>
    /// 将场景位置定位到对应的网格单元，返回网格单元的世界坐标
    /// </summary>
    /// <param name="pos">世界坐标</param>
    /// <returns>返回网格单元的世界坐标</returns>
    public Vector3 ScenePos2TileScenePos(Vector3 pos)
    {
        return GridPos2ScenePos(ScenePos2GridPos(pos));
    }





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
            int area = r * r; // 用于距离判断
            int[] viewer_pos = ScenePos2GridPos(viewer.transform.position);

            for (int i = 0; i < GRID_RECT.x; i++)
            {
                for (int j = 0; j < GRID_RECT.y; j++)
                {
                    FOWTile tile = map_.GetTile(i, j);

                    if (tile.Distance(viewer_pos[0], viewer_pos[1]) > area)   // 不在范围内则跳过
                        continue;

                    Gizmos.color = map_.BlockData[i, j] == 1 ? Color.red : Color.white;
                    Gizmos.DrawWireCube(GridPos2ScenePos(new int[] { i, j }), new Vector3(MAP_TILE_SIZE - 0.02f, 0f, MAP_TILE_SIZE - 0.02f));
                }
            }
        }
    }
#endif

}
