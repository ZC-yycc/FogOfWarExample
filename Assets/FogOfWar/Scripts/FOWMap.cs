using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;


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

    private int                                                 rect_w_;
    private int                                                 rect_h_;
    private int                                                 total_tiles_;

    /// <summary> 可见性标志（0=不可见, 1=可见） </summary>
    public byte[]                                               visible_flags_;

    /// <summary> 已探索标志（0=未探索, 1=已探索） </summary>
    public byte[]                                               explored_flags_;

    /// <summary> 高斯模糊颜色缓冲区数组 </summary>
    public Color32[]                                            color_buffer_;

    private Material                                            blur_mat_;
    private Texture2D                                           texture_cache_;
    private RenderTexture                                       render_buffer_ping_;
    private RenderTexture                                       render_buffer_pong_;
    private RenderTexture                                       next_texture_;
    private RenderTexture                                       curr_texture_;

    private FOWManager                                          manager_;
    private float[]                                             tan_threshold_cache_;

    public Texture CurrentTexture => curr_texture_;

    private byte invisible_alpha_;
    private byte visible_alpha_;
    private byte explored_alpha_;
    private Color32 fog_color_;


    /// <summary>
    /// 初始化迷雾信息
    /// </summary>
    public void InitMap(FOWManager manager)
    {
        manager_ = manager;
        rect_w_ = manager_.GRID_RECT.x;
        rect_h_ = manager_.GRID_RECT.y;
        total_tiles_ = rect_w_ * rect_h_;

        // 缓存常用值，减少间接访问
        invisible_alpha_ = manager_.invisible_alpha_;
        visible_alpha_ = manager_.visible_alpha_;
        explored_alpha_ = manager_.explored_alpha_;
        fog_color_ = new Color32(manager_.fog_color_.r, manager_.fog_color_.g, manager_.fog_color_.b, 255);

        InitBuffer();
        InitGrid();
        InitTanCache(2500);
    }

    private void InitBuffer()
    {
        visible_flags_ = new byte[total_tiles_];
        explored_flags_ = new byte[total_tiles_];
        color_buffer_ = new Color32[total_tiles_];

        // 初始化全图为黑色（不可见）
        for (int i = 0; i < total_tiles_; i++)
        {
            color_buffer_[i] = new Color32(fog_color_.r, fog_color_.g, fog_color_.b, invisible_alpha_);
        }

        blur_mat_ = new Material(manager_.fog_shader_);
        texture_cache_ = new Texture2D(rect_w_, rect_h_, TextureFormat.ARGB32, false);
        texture_cache_.wrapMode = TextureWrapMode.Clamp;

        texture_cache_.SetPixels32(color_buffer_);
        texture_cache_.Apply();

        render_buffer_ping_ = RenderTexture.GetTemporary((int)(rect_w_ * 1.5f), (int)(rect_h_ * 1.5f), 0);
        render_buffer_pong_ = RenderTexture.GetTemporary((int)(rect_w_ * 1.5f), (int)(rect_h_ * 1.5f), 0);
        next_texture_ = RenderTexture.GetTemporary((int)(rect_w_ * 1.5f), (int)(rect_h_ * 1.5f), 0);
        curr_texture_ = RenderTexture.GetTemporary((int)(rect_w_ * 1.5f), (int)(rect_h_ * 1.5f), 0);
    }

    private void InitGrid()
    {
        grid_data_arr_ = new FOWTile[total_tiles_];

#if UNITY_EDITOR
        block_data_ = new int[rect_w_, rect_h_];
#endif

        Collider[] hit_results = new Collider[1];
        int index = 0;
        for (int j = 0; j < rect_h_; j++)
        {
            for (int i = 0; i < rect_w_; i++)
            {
                Vector3 check_pos = manager_.GridPos2ScenePos(new Vector2Int(i, j));

                int hit_count = Physics.OverlapBoxNonAlloc(
                    check_pos,
                    manager_.HALF_EXTENTS,
                    hit_results,
                    Quaternion.identity,
                    manager_.block_layer_
                );

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


    /// <summary> 释放缓存资源 </summary>
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

    /// <summary> 判断指定网格坐标是否可见 </summary>
    public bool CanDisplay(int x, int y)
    {
        int index = Index(x, y);
        return index != -1 && visible_flags_[index] != 0;
    }

    /// <summary> 将网格坐标转换为数组索引 </summary>
    public int Index(int x, int y)
    {
        return (x >= 0 && y >= 0 && x < rect_w_ && y < rect_h_) ?
           x + y * rect_w_ : -1;
    }

    public bool IsInMap(int x, int y)
    {
        return Index(x, y) != -1;
    }

    /// <summary> 对计算出的迷雾贴图进行lerp缓动处理 </summary>
    public void Lerp()
    {
        Graphics.Blit(curr_texture_, render_buffer_ping_);
        blur_mat_.SetTexture("_LastTex", render_buffer_ping_);
        Graphics.Blit(next_texture_, curr_texture_, blur_mat_, 1);
    }

    /// <summary> 应用迷雾颜色到指定区域 </summary>
    public void ApplyFOW(int x, int y, int radius)
    {
        if (radius < 0) radius = 0;

        int min_x = Mathf.Max(0, x - radius);
        int max_x = Mathf.Min(rect_w_ - 1, x + radius);
        int min_y = Mathf.Max(0, y - radius);
        int max_y = Mathf.Min(rect_h_ - 1, y + radius);

        for (int i = min_y; i <= max_y; ++i)
        {
            for (int j = min_x; j <= max_x; ++j)
            {
                int index = Index(j, i);

                color_buffer_[index].a = visible_flags_[index] != 0
                    ? visible_alpha_
                    : (explored_flags_[index] != 0 ? explored_alpha_ : invisible_alpha_);
            }
        }
    }

    /// <summary> 模糊处理 </summary>
    public void Blur()
    {
        texture_cache_.SetPixels32(0, 0, rect_w_, rect_h_, color_buffer_);
        texture_cache_.Apply();

        Graphics.Blit(texture_cache_, render_buffer_ping_, blur_mat_, 0);
        for (int i = 0; i < 2; ++i)
        {
            Graphics.Blit(render_buffer_ping_, render_buffer_pong_, blur_mat_, 0);
            Graphics.Blit(render_buffer_pong_, render_buffer_ping_, blur_mat_, 0);
        }
        Graphics.Blit(render_buffer_ping_, next_texture_);
    }

    /// <summary> 重置所有可见性为0（memset级性能） </summary>
    public void ResetAllVisible()
    {
        Array.Clear(visible_flags_, 0, total_tiles_);
    }

    /// <summary> 将当前可见的格子标记为已探索（OR写入） </summary>
    public void MarkExplored()
    {
        for (int i = 0; i < total_tiles_; ++i)
        {
            explored_flags_[i] |= visible_flags_[i];
        }
    }

    /// <summary>
    /// 计算单个观察者的迷雾网格可见性，直接将可见结果OR写入visible_flags_
    /// 核心原则：只写1，永不写0——后处理的观察者不能覆盖前者的可见区域
    /// </summary>
    public void ComputeFlags(int viewer_index, int x, int y, float range)
    {
        if (range < 0) range = 0;

        int irange = (int)range;
        int area = irange * irange;

        ref ViewerCache cache = ref FOWDataCache.GetCache(viewer_index);
        if (range != cache.RangeCache)
        {
            cache.RangeCache = range;
        }

        // 第一步：收集范围内所有网格
        FOWTile[] area_tile_buffer = cache.GetAreaTileBuffer();
        int tile_count = 0;

#if UNITY_EDITOR
        Profiler.BeginSample("FogUpdate.ComputeFlags.FlagGrid");
#endif
        for (int i = -irange; i <= irange; ++i)
        {
            for (int j = -irange; j <= irange; ++j)
            {
                if (i * i + j * j > area) continue;

                int grid_x = x + i;
                int grid_y = y + j;
                int grid_index = Index(grid_x, grid_y);
                if (grid_index < 0) continue;

                area_tile_buffer[tile_count++] = grid_data_arr_[grid_index];
            }
        }
#if UNITY_EDITOR
        Profiler.EndSample();
#endif

        if (tile_count == 0) return;

        // 第二步：按距离排序
#if UNITY_EDITOR
        Profiler.BeginSample("FogUpdate.ComputeFlags.Sort");
#endif
        Array.Sort(area_tile_buffer, 0, tile_count, Comparer<FOWTile>.Create((a, b) => a.Distance(x, y) - b.Distance(x, y)));
#if UNITY_EDITOR
        Profiler.EndSample();
#endif

        // 第三步：收集障碍物并预计算索引
        FOWTile[] obs_tile_buffer = cache.GetObstacleTileBuffer();
        int[] obs_start_indices = cache.GetObstacleIndicesBuffer();
        int obs_count = 0;

        for (int i = 0; i < tile_count; ++i)
        {
            FOWTile tile = area_tile_buffer[i];
            if (tile.type_ != 1) continue;
            if (tile.x_ == x && tile.y_ == y) continue;

            obs_tile_buffer[obs_count] = tile;
            obs_start_indices[obs_count] = i;
            obs_count++;
        }

        // 第四步：障碍物遮挡剔除（只置null，不写visible_flags_）
#if UNITY_EDITOR
        Profiler.BeginSample("FogUpdate.ComputeFlags.Cast");
#endif
        for (int i = 0; i < obs_count; ++i)
        {
            Cast(obs_tile_buffer[i], x, y, area_tile_buffer, obs_start_indices[i]);
        }
#if UNITY_EDITOR
        Profiler.EndSample();
#endif

        // 第五步：将本观察者的可见结果OR写入visible_flags_（只写1）
        for (int i = 0; i < tile_count; ++i)
        {
            FOWTile tile = area_tile_buffer[i];
            if (tile == null) continue;

            int idx = Index(tile.x_, tile.y_);
            if (idx >= 0)
            {
                visible_flags_[idx] = 1;
            }
        }
    }


    /// <summary>
    /// 障碍物遮挡判断，被遮挡的格子置null
    /// 不碰visible_flags_——由ComputeFlags统一OR写入
    /// </summary>
    private void Cast(FOWTile ob, int x, int y, FOWTile[] test_list, int start_index)
    {
        int ob_x = ob.x_ - x;
        int ob_y = ob.y_ - y;
        int ob_dist_sqrt = ob_x * ob_x + ob_y * ob_y;
        float tan_z = tan_threshold_cache_[ob_dist_sqrt];

        for (int i = start_index + 1; i < test_list.Length; ++i)
        {
            FOWTile tile = test_list[i];
            if (tile == null) continue;

            int dx = tile.x_ - x;
            int dy = tile.y_ - y;

            long dot = (long)ob_x * dx + (long)ob_y * dy;
            if (dot <= 0) continue;

            // 障碍物直接遮挡
            if (tile.type_ == 1)
            {
                test_list[i] = null;
                continue;
            }

            if (dx * dx + dy * dy <= ob_dist_sqrt) continue;

            long cross = (long)ob_x * dy - (long)ob_y * dx;
            if (cross == 0)
            {
                test_list[i] = null;
                continue;
            }

            long cross_abs = cross >= 0 ? cross : -cross;
            if (cross_abs < dot * tan_z)
            {
                test_list[i] = null;
            }
        }
    }
}


/// <summary>
/// 战争迷雾网格信息类
/// </summary>
public class FOWTile
{
    /// <summary> 1=障碍物, 0=非障碍物 </summary>
    public int type_;
    public int x_;
    public int y_;

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
    private FOWTile[] area_tile_buffer;
    private FOWTile[] obstacle_tile_buffer;
    private float range_cache;
    private int buffer_unification_size;
    private int[] obstacle_indices_buffer;

    public float RangeCache
    {
        readonly get => range_cache;
        set
        {
            range_cache = value;
            int side_length = (int)(2 * range_cache + 1);
            int S = side_length * side_length;
            int a = (int)(range_cache - 0.7f * range_cache);
            buffer_unification_size = S - a * a * 8;
        }
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

    public int[] GetObstacleIndicesBuffer()
    {
        if (obstacle_indices_buffer == null || obstacle_indices_buffer.Length != buffer_unification_size)
        {
            obstacle_indices_buffer = new int[buffer_unification_size];
        }
        else
        {
            Array.Clear(obstacle_indices_buffer, 0, obstacle_indices_buffer.Length);
        }
        return obstacle_indices_buffer;
    }
}


/// <summary>
/// 观察者缓存管理（静态类，仅管理ViewerCache数组）
/// </summary>
public static class FOWDataCache
{
    private static ViewerCache[] viewer_cache_array_ = new ViewerCache[0];

    public static ref ViewerCache GetCache(int index)
    {
        if (index >= viewer_cache_array_.Length)
        {
            Array.Resize(ref viewer_cache_array_, index + 1);
        }
        return ref viewer_cache_array_[index];
    }
}