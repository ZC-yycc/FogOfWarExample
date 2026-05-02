using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 距离剔除组件，用于控制渲染器在特定条件下的显示和隐藏
/// </summary>
public class FOWCulling : MonoBehaviour
{
    /// <summary>
    /// 启动时是否隐藏渲染器
    /// </summary>
    public bool                                         awake_hide_ = false;
    /// <summary>
    /// 是否自动验证并更新渲染器数组
    /// </summary>
    public bool                                         auto_validate_ = true;
    /// <summary>
    /// 是否尝试获取粒子系统的父对象
    /// </summary>
    public bool                                         try_get_particle_parent_ = false;
    /// <summary>
    /// 渲染器数组，存储所有需要控制的渲染器组件
    /// </summary>
    [SerializeField] private Renderer[]                 renderer_arr_;
    /// <summary>
    /// 渲染对象列表，存储需要控制的GameObject对象
    /// </summary>
    [SerializeField] private List<GameObject>           renderer_obj_list_;





    /// <summary>
    /// 验证时回调函数，用于自动更新渲染器数组
    /// </summary>
    private void OnValidate()
    {
        if (!auto_validate_)
        {
            return;
        }

        renderer_arr_ = GetComponentsInChildren<Renderer>();
    }
    /// <summary>
    /// 重置时回调函数，用于初始化渲染器数组
    /// </summary>
    private void Reset()
    {
        renderer_arr_ = GetComponentsInChildren<Renderer>();
    }
    /// <summary>
    /// 启动时初始化组件
    /// </summary>
    private void Start()
    {
        if (renderer_arr_ == null || renderer_arr_.Length == 0)
        {
            renderer_arr_ = GetComponentsInChildren<Renderer>();
        }

        // 根据FOW管理器状态和启动隐藏设置来决定渲染器启用状态
        SetRenderEnabled(FOWManager.instance_ == null || !awake_hide_ && FOWManager.instance_.GeneratedFow);
    }
    /// <summary>
    /// 设置渲染器的启用状态
    /// </summary>
    /// <param name="enabled">是否启用渲染器</param>
    public void SetRenderEnabled(bool enabled)
    {
        if (renderer_arr_ == null)
        {
            return;
        }

        for (int i = 0; i < renderer_arr_.Length; ++i)
        {
            if (renderer_arr_[i] == null)
            {
                continue;
            }

            renderer_arr_[i].enabled = enabled;
        }

        for (int i = renderer_obj_list_.Count - 1; i >= 0; --i)
        {
            GameObject temp = renderer_obj_list_[i];
            if (temp == null)
            {
                renderer_obj_list_.RemoveAt(i);
                continue;
            }
            temp.SetActive(enabled);
        }
    }
    /// <summary>
    /// 重新查找并更新渲染器数组
    /// </summary>
    public void Refind()
    {
        renderer_arr_ = GetComponentsInChildren<Renderer>();
    }
    /// <summary>
    /// 添加需要进行剔除控制的游戏对象
    /// </summary>
    /// <param name="obj">要添加的游戏对象</param>
    public void AddCullingObject(GameObject obj)
    {
        renderer_obj_list_.Add(obj);
    }
    /// <summary>
    /// 移除需要进行剔除控制的游戏对象
    /// </summary>
    /// <param name="obj">要移除的游戏对象</param>
    public void RemoveCullingObject(GameObject obj)
    {
        renderer_obj_list_.Remove(obj);
    }
}