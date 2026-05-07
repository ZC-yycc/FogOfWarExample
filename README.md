# Fog of War Example

一个高性能的 Unity 战争迷雾系统实现，支持多观察者、障碍物遮挡剔除、已探索区域记忆以及平滑过渡效果。

## 功能特性

- **多观察者支持** — 同时支持多个 FOWViewer 观察者，所有观察者的视野范围会合并到一起
- **障碍物遮挡剔除** — 基于角度阈值的射线检测算法，障碍物后方的区域会被正确遮挡
- **已探索区域记忆** — 可选的"走过之后留下足迹"功能，已探索区域以半透明显示
- **平滑过渡效果** — 基于高斯模糊和帧间 Lerp 缓动，避免迷雾边缘突变
- **高性能计算** — 缓存友好的数据结构、memset 级清零、Profiler 标记支持性能分析
- **距离剔除集成** — FOWCulling 组件自动根据可见性切换 Renderer 的显示/隐藏
- **Editor 调试可视化** — 在 Scene 视图中选中 FOWManager 即可看到网格线和障碍物分布

## 项目结构

```
Assets/FogOfWar/
├── Scripts/
│   ├── FOWManager.cs          # 核心管理器：初始化、更新循环、坐标转换、公共API
│   ├── FOWMap.cs              # 地图数据：可见性计算、障碍物剔除、颜色/模糊处理
│   ├── FOWViewer.cs           # 观察者：定义视野范围、碰撞触发区域
│   ├── FOWCulling.cs          # 距离剔除控制器：根据可见性切换 Renderer
│   └── FOWFogRenderer.cs      # 渲染器：将生成的迷雾纹理投射到场景平面
├── Shaders/
│   ├── FOWShader.shader       # 迷雾显示着色器（透明混合，ZTest Off）
│   └── BlurShader.shader      # 均值模糊 + Lerp 缓动着色器
├── Prefabs/
│   └── FOWManager.prefab      # 预置体：包含 FOWManager 和子物体 FogPlane
├── Mats/
│   └── FOWMat.mat             # 迷雾材质（使用 FOWShader）
├── Textures/
│   └── FogTexture.jpg         # 初始迷雾纹理
└── Example/
    └── SampleScene.unity      # 示例场景
```

## 核心架构

```
┌──────────────────────────────────────────────────┐
│                   FOWManager                      │
│          (核心管理器，驱动机整个系统)              │
│  - InvokeRepeating 定时更新                       │
│  - 管理所有 Viewer / 地图 / 渲染器                │
└──────┬──────────┬──────────┬─────────────────────┘
       │          │          │
       ▼          ▼          ▼
┌──────────┐ ┌──────────┐ ┌──────────────────┐
│ FOWMap   │ │FOWViewer │ │ FOWFogRenderer   │
│ 地图数据 │ │ 观察者   │ │ 迷雾纹理渲染     │
└────┬─────┘ └────┬─────┘ └──────────────────┘
     │            │
     ▼            ▼
┌─────────────────────────────────────┐
│          FOWCulling                  │
│    (挂在场景物体上，控制显示/隐藏)   │
└─────────────────────────────────────┘
```

### 每帧更新流程（五步精简化）

```
1. 清零 visible_flags_        →  memset 级别性能
2. 各观察者独立计算可见性      →  OR 写入 visible_flags_（只写1，不覆盖）
3. 标记已探索区域             →  explored_flags_ |= visible_flags_
4. 应用颜色 + 高斯模糊         →  color_buffer → texture → BlurShader
5. 更新 FOWCulling 组件       →  根据网格可见性控制 Renderer.enabled
```

## 快速开始

### 1. 场景设置

将 `FOWManager.prefab` 拖入场景中，该预置体已包含：
- `FOWManager` 组件 — 核心管理器
- `FOWFogRenderer` 组件 — 迷雾渲染器
- 子物体 `FogPlane` — 贴有迷雾材质的平面

### 2. 配置观察者

在你需要拥有视野的 GameObject 上添加 `FOWViewer` 组件：

```
FOWViewer:
  Viewer Range = 7        # 视野半径（单位与 FOWManager.MAP_TILE_SIZE 对应）
  Collider = SphereCollider  # 自动添加，用于触发 FOWCulling 范围检测
```

### 3. 添加距离剔除（可选）

在场景中的物体（如敌人、建筑等）上添加 `FOWCulling` 组件：

```
FOWCulling:
  Awake Hide = true       # 启动时隐藏
  Auto Validate = true    # 自动检测子物体的 Renderer
```

当该物体进入 FOWViewer 的触发范围且处于可见迷雾网格时，会自动显示。

### 4. 生成迷雾

在 Inspector 中设置 FOWManager 上勾选 `Awake Generate`，或在代码中调用：

```csharp
FOWManager.instance_.GenerateMapFOW();
```

---

## 配置参数详解

### FOWManager

| 参数 | 类型 | 说明 |
|------|------|------|
| `FOG_SIZE` | Vector2 | 迷雾覆盖的总区域大小（世界单位） |
| `MAP_TILE_SIZE` | float | 单个网格大小，越小越精细但性能开销越大 |
| `update_time_` | float | 更新频率（秒），默认 0.2 秒/次 |
| `is_save_explored_` | bool | 是否保存已探索区域 |
| `awake_generate_` | bool | 启动时自动生成迷雾 |
| `fog_color_` | Color32 | 迷雾颜色，默认黑色 |
| `invisible_alpha_` | byte | 不可见区域透明度（0-255），默认 255 |
| `visible_alpha_` | byte | 可见区域透明度，默认 0 |
| `explored_alpha_` | byte | 已探索区域透明度，默认 128 |
| `fog_lerp_rate_` | float | 迷雾平滑过渡速度，默认 0.8 |
| `block_layer_` | LayerMask | 障碍物所在的 Layer |
| `fog_shader_` | Shader | 迷雾着色器（BlurShader） |

### FOWViewer

| 参数 | 类型 | 说明 |
|------|------|------|
| `Viewer Range` | int | 视野半径（网格单位），范围 0-50 |

### FOWCulling

| 参数 | 类型 | 说明 |
|------|------|------|
| `awake_hide_` | bool | 启动时是否隐藏渲染器 |
| `auto_validate_` | bool | 是否在 Inspector 修改时自动更新 Renderer 数组 |
| `renderer_arr_` | Renderer[] | 需要控制的渲染器列表 |
| `renderer_obj_list_` | List\<GameObject\> | 需要控制的 GameObject 列表 |

---

## 公共 API

### FOWManager

```csharp
// 生成/清除迷雾
FOWManager.instance_.GenerateMapFOW();   // 初始化并开始迷雾计算
FOWManager.instance_.ClearMapFOW();      // 清除迷雾并恢复所有物体显示

// 动态管理观察者
FOWManager.instance_.AddViewer(viewer);
FOWManager.instance_.RemoveViewer(viewer);

// 坐标转换
Vector2Int gridPos = FOWManager.instance_.ScenePos2GridPos(worldPosition);
Vector3 worldPos  = FOWManager.instance_.GridPos2ScenePos(gridPos);
```

### FOWViewer

```csharp
// 动态修改视野范围
viewer.ViewerRange = 10;

// 管理 Culling 组件
viewer.AddCullingComp(comp);
viewer.RemoveCullingComp(comp);
```

### FOWCulling

```csharp
// 手动控制渲染器显示
culling.SetRenderEnabled(true);
culling.SetRenderEnabled(false);

// 动态管理剔除对象
culling.AddCullingObject(gameObject);
culling.RemoveCullingObject(gameObject);
```

---

## 技术细节

### 障碍物遮挡算法

障碍物遮挡采用基于角度阈值的扇形剔除方法：

1. 以观察者为中心，按距离排序收集视野范围内的所有网格
2. 对每个障碍物网格，计算其相对于观察者的位置向量
3. 对于障碍物"后面"的网格，通过叉积和点积判断是否落在遮挡扇形内
4. 使用预计算的 tan 阈值缓存表（`tan_threshold_cache_`）避免运行时三角函数计算

角度阈值公式：`tan_threshold = tan(π / (6 + sqrt(distance²)))`  
即距离越远的障碍物，遮挡角度越小（投影越小）。

### 性能优化策略

- **memset 级清零**：`Array.Clear()` 代替遍历赋值
- **缓存重用**：`ViewerCache` 结构体复用网格缓冲区，避免每帧分配
- **OR 写入策略**：每个观察者只写 1 不写 0，支持多个观察者并行计算
- **分帧处理**：`SetAllCullingObjState` 协程每帧只处理 10 个物体，避免一次性 CPU 尖峰
- **Profiler 标记**：关键步骤均有 `Profiler.BeginSample/EndSample`，方便性能分析

---

## 依赖

- Unity 6000.1.0+
- 使用 `FindObjectsByType`、`OverlapBoxNonAlloc` 等较新的 Unity API

---

## 许可证

MIT License