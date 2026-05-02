using System;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SocialPlatforms;


/// <summary>
/// 迷雾地图, 用于管理迷雾信息，绘制迷雾。
/// 整体流程为，获取玩家周围一定数量的格子，先整体设置为可视，然后根据玩家位置，将周围一定数量的格子设置为不可见。
/// </summary>
public class FOWMap
{
    /// <summary>
    /// 网格的数据信息，包含网格坐标、是否有障碍物
    /// </summary>
    private FOWTile[]                                           grid_data_arr_;

#if UNITY_EDITOR
    private int[,]                                              block_data_;
    public int[,] BlockData => block_data_;
#endif

    /// <summary>
    /// 网格相对于单个网格的尺寸，宽由有多少单个网格组成
    /// </summary>
    private int                                                 rect_w_;

    /// <summary>
    /// 网格相对于单个网格的尺寸，高由有多少单个网格组成
    /// </summary>
    private int                                                 rect_h_;

    /// <summary>
    /// 迷雾网格的颜色标志区，r == 255 时表示可视区域， b == 255 时表示该区域已探索。
    /// </summary>
    public FOWFlag[]                                            fog_flags_;

    /// <summary>
    /// 高斯模糊颜色缓冲区数组
    /// </summary>
    public Color32[]                                            color_buffer_;

    private Material                                            blur_mat_;

    /// <summary>
    /// 保存颜色缓冲区的贴图
    /// </summary>
    private Texture2D                                           texture_cache_;

    /// <summary>
    /// 高斯模糊的缓冲渲染贴图
    /// </summary>
    private RenderTexture                                       render_buffer_ping_;
    private RenderTexture                                       render_buffer_pong_;

    /// <summary>
    /// 完成模糊后拿到模糊贴图缓存，由该变量缓存贴图渲染
    /// </summary>
    private RenderTexture                                       next_texture_;

    /// <summary>
    /// 当前的渲染贴图
    /// </summary>
    private RenderTexture                                       curr_texture_;

    private FOWManager                                          manager_;

    private float[]                                             tan_threshold_cache_;


    /// <summary>
    /// 迷雾贴图对外接口
    /// </summary>
    public Texture CurrentTexture => curr_texture_;




    /// <summary>
    /// 初始化迷雾信息
    /// </summary>
    public void InitMap(FOWManager manager)
    {
        manager_ = manager;
        rect_w_ = manager_.GRID_RECT.x;
        rect_h_ = manager_.GRID_RECT.y;
        InitBuffer();
        InitGrid();
        InitTanCache(2500);
    }
    private void InitBuffer()
    {
        fog_flags_ = new FOWFlag[rect_w_ * rect_h_];
        color_buffer_ = new Color32[rect_w_ * rect_h_];

        // 初始化全图为黑色（不可见）
        for (int i = 0; i < fog_flags_.Length; i++)
        {
            fog_flags_[i] = new FOWFlag();
            color_buffer_[i] = new Color32(manager_.fog_color_.r, manager_.fog_color_.g, manager_.fog_color_.b, manager_.invisible_alpha_);
        }

        blur_mat_ = new Material(manager_.fog_shader_);
        texture_cache_ = new Texture2D(rect_w_, rect_h_, TextureFormat.ARGB32, false);
        texture_cache_.wrapMode = TextureWrapMode.Clamp;

        // 初始化纹理为全黑
        texture_cache_.SetPixels32(color_buffer_);
        texture_cache_.Apply();

        render_buffer_ping_ = RenderTexture.GetTemporary((int)(rect_w_ * 1.5f), (int)(rect_h_ * 1.5f), 0);
        render_buffer_pong_ = RenderTexture.GetTemporary((int)(rect_w_ * 1.5f), (int)(rect_h_ * 1.5f), 0);
        next_texture_ = RenderTexture.GetTemporary((int)(rect_w_ * 1.5f), (int)(rect_h_ * 1.5f), 0);
        curr_texture_ = RenderTexture.GetTemporary((int)(rect_w_ * 1.5f), (int)(rect_h_ * 1.5f), 0);
    }
    private void InitGrid()
    {
        grid_data_arr_ = new FOWTile[rect_w_ * rect_h_];

#if UNITY_EDITOR
        block_data_ = new int[rect_w_, rect_h_];
#endif

        // 预分配一个容量为1的碰撞器数组，因为我们只关心指定位置有无碰撞体
        Collider[] hit_results = new Collider[1];
        int index = 0;
        for (int j = 0; j < rect_h_; j++)
        {
            for (int i = 0; i < rect_w_; i++)
            {
                Vector3 check_pos = manager_.GridPos2ScenePos(new int[] { i, j });

                // 使用 OverlapBoxNonAlloc，不会分配新数组
                int hit_count = Physics.OverlapBoxNonAlloc(
                    check_pos,
                    manager_.HALF_EXTENTS,
                    hit_results,
                    Quaternion.identity,
                    manager_.block_layer_
                );

                // 如果检测到至少一个碰撞器，则记为1，否则为0
                int type = hit_count > 0 ? 1 : 0;
                grid_data_arr_[index] = new FOWTile(type, i, j);
#if UNITY_EDITOR
                block_data_[i, j] = type;
#endif
                index++;
            }
        }
    }
    private void InitTanCache(int max_distance)
    {
        tan_threshold_cache_ = new float[max_distance + 1];
        for (int i = 0; i <= max_distance; ++i)
        {
            float dist = Mathf.Sqrt(i);
            float z = Mathf.PI / (6 + dist);
            tan_threshold_cache_[i] = (float)Math.Tan(z);
        }
    }




    /// <summary>
    /// 释放缓存资源
    /// </summary>
    public void Release()
    {
        RenderTexture.ReleaseTemporary(render_buffer_ping_);
        RenderTexture.ReleaseTemporary(render_buffer_pong_);
        RenderTexture.ReleaseTemporary(next_texture_);
        RenderTexture.ReleaseTemporary(curr_texture_);
    }
    public FOWTile GetTile(int x, int y)
    {
        return (x >= 0 && y >= 0 && x < rect_w_ && y < rect_h_) ?
            grid_data_arr_[x + y * rect_w_] : null;
    }
    /// <summary>
    /// 判断指定网格坐标是否可见
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public bool CanDisplay(int x, int y)
    {
        int index = Index(x, y);
        return index != -1 && fog_flags_[index].visible;
    }
    /// <summary>
    /// 将网格坐标转换为数组索引
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public int Index(int x, int y)
    {
        return (x >= 0 && y >= 0 && x < rect_w_ && y < rect_h_) ?
           x + y * rect_w_ : -1;
    }
    public bool IsInMap(int x, int y)
    {
        return Index(x, y) != -1;
    }
    /// <summary>
    /// 对计算出的迷雾贴图进行lerp缓动处理
    /// </summary>
    public void Lerp()
    {
        Graphics.Blit(curr_texture_, render_buffer_ping_);
        blur_mat_.SetTexture("_LastTex", render_buffer_ping_);
        Graphics.Blit(next_texture_, curr_texture_, blur_mat_, 1);
    }
    /// <summary>
    /// 应用迷雾信息到指定区域
    /// </summary>
    public void ApplyFOW(int x, int y, int radius)
    {
        if (radius < 0)
        {
            radius = 0;
        }

        // 计算需要更新的区域边界
        int min_x = Mathf.Max(0, x - radius);
        int max_x = Mathf.Min(rect_w_ - 1, x + radius);
        int min_y = Mathf.Max(0, y - radius);
        int max_y = Mathf.Min(rect_h_ - 1, y + radius);

        // 只更新指定范围内的格子
        for (int i = min_y; i <= max_y; ++i)
        {
            for (int j = min_x; j <= max_x; ++j)
            {
                int index = Index(j, i);
                ref var flag = ref fog_flags_[index];

                // 该区域不可见，则设置透明度为不可见
                color_buffer_[index].a = flag.visible ?
                    manager_.visible_alpha_ : (flag.explored ? manager_.explored_alpha_ : manager_.invisible_alpha_);
            }
        }
    }
    /// <summary>
    /// 模糊处理
    /// </summary>
    public void Blur()
    {
        // 应用部分更新到纹理
        texture_cache_.SetPixels32(0, 0, rect_w_, rect_h_, color_buffer_);
        texture_cache_.Apply();

        // 模糊处理
        Graphics.Blit(texture_cache_, render_buffer_ping_, blur_mat_, 0);
        for (int i = 0; i < 2; ++i)
        {
            Graphics.Blit(render_buffer_ping_, render_buffer_pong_, blur_mat_, 0);
            Graphics.Blit(render_buffer_pong_, render_buffer_ping_, blur_mat_, 0);
        }
        Graphics.Blit(render_buffer_ping_, next_texture_);
    }
    /// <summary>
    /// 计算迷雾网格可见性
    /// </summary>
    /// <param name="x">视野起始坐标X</param>
    /// <param name="y">视野起始坐标Y</param>
    /// <param name="range">视野范围</param>
    public void ComputeFlags(int viewer_index, int x, int y, float range)
    {
        if (range < 0)
        {
            range = 0;
        }

        ref ViewerCache cache = ref FOWDataCache.GetCache(viewer_index);

        // 更新计算范围
        if (range != cache.RangeCache)
        {
            // 范围缓存存储
            cache.RangeCache = range;
        }

        /// <summary>
        /// 获取玩家周围一定数量格子，并且标记为可见 ===================================================================
        /// </summary>

        // 拿到区域的对应缓冲区
        FOWTile[] area_tile_buffer = cache.GetAreaTileBuffer();

        // 用于距离判断
        int area = (int)(range * range);
        int index = 0;

        Profiler.BeginSample("FogUpdate.ComputeFlags.FlagGrid");
        // 从 -r 到 r 判断是否在范围内
        for (int i = (int)-range; i <= range; ++i)
        {
            for (int j = (int)-range; j <= range; ++j)
            {
                if (i * i + j * j > area)   // 不在范围内则跳过
                    continue;

                int grid_index = Index(x + i, y + j);                   // 得到该位置的网格索引
                area_tile_buffer[index] = grid_data_arr_[grid_index];   // 缓存网格
                fog_flags_[grid_index].visible = true;                  // 标记为已发现
                index++;                                                // 索引++
            }
        }
        Profiler.EndSample();


        Profiler.BeginSample("FogUpdate.ComputeFlags.Sort");
        /// <summary>
        /// 对得到的所有网格按照距离排序，方便后续检测阻挡网格 ========================================================
        /// </summary>
        Array.Sort(area_tile_buffer, (a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return a.Distance(x, y) - b.Distance(x, y);
        });
        Profiler.EndSample();




        /// <summary>
        /// 缓存所有障碍物 ============================================================================================
        /// </summary>
        FOWTile[] obs_tile_buffer = cache.GetObstacleTileBuffer();

        index = 0;
        for (int i = (int)-range; i <= range; ++i)
        {
            for (int j = (int)-range; j <= range; ++j)
            {
                if (i == 0 && i == j)       // 自身位置跳过
                    continue;

                if (i * i + j * j > area)   // 区域外跳过
                    continue;

                var tile = GetTile(x + i, y + j);
                if (tile == null)           // 为 null 跳过
                    continue;

                if (tile.type_ != 1)         // 不是障碍物跳过
                    continue;

                obs_tile_buffer[index] = tile;
                index++;
            }
        }



        Profiler.BeginSample("FogUpdate.ComputeFlags.Cast");
        /// <summary>
        /// 遍历所有网格障碍物，对所有网格进行检测，标记所有不可视网格 ====================================================
        /// </summary>
        for (int i = 0; i < obs_tile_buffer.Length; ++i)
        {
            FOWTile ob = obs_tile_buffer[i];
            if (ob == null)
            {
                continue;
            }

            Cast(ob, x, y, area_tile_buffer);
        }
        Profiler.EndSample();
    }



    /// <summary>
    /// 障碍物对列表中的网格进行是否遮挡判断，并标记
    /// </summary>
    /// <param name="ob">障碍物</param>
    /// <param name="x">观察者网格坐标 x </param>
    /// <param name="y">观察者网格坐标 y </param>
    /// <param name="test_list">待检测列表</param>
    /// <returns></returns>
    public void Cast(FOWTile ob, int x, int y, FOWTile[] test_list)
    {
        int ob_x = ob.x_ - x;
        int ob_y = ob.y_ - y;
        int ob_dist_sqrt = ob_x * ob_x + ob_y * ob_y;
        float tan_z = tan_threshold_cache_[ob_dist_sqrt];
        int start_index = Array.IndexOf(test_list, ob);


        for (int i = start_index + 1; i < test_list.Length; ++i)
        {
            FOWTile tile = test_list[i];
            if (tile == null) continue;

            int dx = tile.x_ - x;
            int dy = tile.y_ - y;

            long dot = (long)ob_x * dx + (long)ob_y * dy;
            if (dot <= 0)
            {
                continue;  // 目标不在障碍物前方
            }

            // 快速拒绝
            if (tile.type_ == 1)
            {
                fog_flags_[Index(tile.x_, tile.y_)].visible = false;
                continue;
            }

            if (dx * dx + dy * dy <= ob_dist_sqrt)
            {
                continue;
            }

            int index = Index(tile.x_, tile.y_);
            long cross = (long)ob_x * dy - (long)ob_y * dx;
            if (cross == 0)
            {
                fog_flags_[index].visible = false;
                continue;
            }

            if (dot > 0)
            {
                long cross_abs = cross >= 0 ? cross : -cross;
                if (cross_abs < dot * tan_z)
                    fog_flags_[index].visible = false;
            }
        }
    }

    /// <summary>
    /// 刷新迷雾，将玩家周围所有格子重置，根据是否保存进度标记通道
    /// </summary>
    public void FreshFog(int viewer_index, bool is_save_explored)
    {
        FOWTile[] area_tile_buffer = FOWDataCache.GetCache(viewer_index).GetAreaTileBuffer();
        if (area_tile_buffer == null)
        {
            return;
        }

        for (int i = 0; i < area_tile_buffer.Length; ++i)
        {
            if (area_tile_buffer[i] == null)
            {
                continue;
            }

            int index = Index(area_tile_buffer[i].x_, area_tile_buffer[i].y_);

            // 如果该位置不可见则跳过
            if (!fog_flags_[index].visible)
            {
                continue;
            }

            // 将该位置设置为不可见
            fog_flags_[index].visible = false;

            // 标记为已探索
            if (is_save_explored)
            {
                fog_flags_[index].explored = true;
            }
        }
    }
}

/// <summary>
/// 战争迷雾数据缓存类，用于标记网格是否可见、是否已探索
/// </summary>
public struct FOWFlag
{
    /// <summary>
    /// 是否可见
    /// </summary>
    public bool                                             visible;

    /// <summary>
    /// 是否已探索
    /// </summary>
    public bool                                             explored;
}

/// <summary>
/// 战争迷雾网格信息类
/// </summary>
public class FOWTile
{
    /// <summary>
    /// 1表示障碍物 0表示非障碍物
    /// </summary>
    public int                                              type_;
    public int                                              x_;
    public int                                              y_;
    public FOWTile(int type, int x, int y)
    {
        type_ = type;
        x_ = x;
        y_ = y;
    }

    public int Distance(int ox, int oy)
    {
        var tx = ox - x_;
        var ty = oy - y_;
        return tx * tx + ty * ty;
    }
}

/// <summary>
/// 观察者的数据缓存类，缓存观察者周围网格信息、观察者感知范围缓存
/// </summary>
public struct ViewerCache
{
    /// <summary>
    /// 观察者周围的网格缓存，半径 r 则缓存 （4 * r * r）- 边角网格数，个网格
    /// 具体算法看 UpdateComputeRange 函数。
    /// </summary>
    private FOWTile[]                                       area_tile_buffer;

    /// <summary>
    /// 障碍物缓存与 area_tile_buffer_ 大小一致，都是使用 buffer_unification_s_ 初始化大小
    /// </summary>
    private FOWTile[]                                       obstacle_tile_buffer;

    /// <summary>
    /// 对应viewer的视野范围缓存
    /// </summary>
    private float                                           range_cache;

    /// <summary>
    /// 缓存的缓冲大小，即缓存的网格数
    /// </summary>
    private int                                             buffer_unification_size;



    public float RangeCache
    {
        readonly get
        {
            return range_cache;
        }
        set
        {
            range_cache = value;
            int side_length = (int)(2 * range_cache + 1);       // 计算正方形的边长
            int S = side_length * side_length;                  // 计算正方形面积
            int a = (int)(range_cache - 0.7f * range_cache);    // 计算边角多余的区域
            buffer_unification_size = S - a * a * 8;            // 计算除去边缘面积（大概去除，不能完全去除）后的面积，同步缓存大小
        }
    }



    /// <summary>
    /// 重置缓存
    /// </summary>
    public readonly void ResetBuffers()
    {
        if (area_tile_buffer == null || obstacle_tile_buffer == null)
            return;

        Array.Clear(area_tile_buffer, 0, area_tile_buffer.Length);
        Array.Clear(obstacle_tile_buffer, 0, obstacle_tile_buffer.Length);
    }

    public FOWTile[] GetAreaTileBuffer()
    {
        if (area_tile_buffer == null || area_tile_buffer.Length != buffer_unification_size)
        {
            area_tile_buffer = new FOWTile[buffer_unification_size];
        }
        return area_tile_buffer;
    }

    public FOWTile[] GetObstacleTileBuffer()
    {
        if (obstacle_tile_buffer == null || obstacle_tile_buffer.Length != buffer_unification_size)
        {
            obstacle_tile_buffer = new FOWTile[buffer_unification_size];
        }
        else
        {
            Array.Clear(obstacle_tile_buffer, 0, obstacle_tile_buffer.Length);
        }
        return obstacle_tile_buffer;
    }
}

/// <summary>
/// 网格检测缓存，防止变量频繁创建，触发GC
/// </summary>
public static class FOWDataCache
{
    private static ViewerCache[]                            viewer_cache_array_ = new ViewerCache[0];

    public static ref ViewerCache GetCache(int index)
    {
        // 边界检查
        if (index >= viewer_cache_array_.Length)
        {
            Array.Resize(ref viewer_cache_array_, index + 1);
        }

        return ref viewer_cache_array_[index];
    }
}