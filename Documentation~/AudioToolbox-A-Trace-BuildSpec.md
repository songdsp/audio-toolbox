# AudioDoctor · 模块 A：运行时事件追踪（Trace）

> 版本 0.1 · 单人开发 · 依赖模块 C 的 Core 数据模型

## 1. 目标与边界

**目标**：把"没声音"这个模糊症状拆解成七种可区分的失败模式，并为每一次事件调用留下足以复现的现场记录。

失败链路：

```
代码未调用 → 句柄无效（bank 未加载）→ 实例被拒（max instance）
→ 虚拟化（距离 / 音量 / 发声数上限）→ 被 bus 静音或 ducking 压死
→ 被逻辑提前 stop → 空间化定位错误
```

**明确不做**：
- 不做混音电平监视与性能 HUD——那是 D 模块
- 不做音频输出的采集与比对——那是 E 模块
- 不追踪未经统一门面的直接中间件 API 调用（见 §7 盲区）

---

## 2. 技术栈

| 层 | 选型 | 理由 |
|---|---|---|
| 引擎 | Unity 2022.3 LTS | 与模块 C 保持一致 |
| 采集层 | Runtime 程序集，C# struct + 环形缓冲 | 必须进包；必须零 GC |
| 条件编译 | `AUDIODOCTOR_TRACE` | 关闭时采集层编译为空，Release 包无残留 |
| 落盘 | 二进制格式，写入 `Application.persistentDataPath` | 体积可控；QA 可直接取走 |
| UI | UI Toolkit `ListView`（虚拟化） | 一次会话动辄数万条记录，IMGUI 扛不住 |
| 调用点采集 | `[CallerFilePath]` / `[CallerLineNumber]` | 编译期常量，运行时零开销，优于抓调用栈 |
| 测试 | Unity Test Framework，PlayMode + EditMode | 性能与正确性测试必须在 PlayMode |

### 2.1 程序集划分

```
AudioDoctor.Core              Runtime  与模块 C 共用
AudioDoctor.Trace             Runtime  门面、环形缓冲、序列化
AudioDoctor.Trace.Fmod        Runtime  Define Constraint: AUDIODOCTOR_FMOD
AudioDoctor.Trace.Wwise       Runtime  Define Constraint: AUDIODOCTOR_WWISE
AudioDoctor.Trace.Native      Runtime  兜底后端
AudioDoctor.Trace.Editor      Editor   时间轴窗口、日志导入
AudioDoctor.Trace.Tests       Runtime  PlayMode 测试
```

### 2.2 核心数据模型

```csharp
public enum PlaybackOutcome {
    NotCalled,        // 保留：由 C 模块的静态分析补充，追踪器本身不产生
    HandleInvalid,    // 句柄无效：bank 未加载 / 事件不存在
    Rejected,         // 实例创建被拒：max instance 上限
    Started,          // 正常起播
    Virtualized,      // 起播但被虚拟化
    Stolen,           // 起播后被抢占
    StoppedEarly,     // 起播后被逻辑提前停止
}

public struct AudioTraceRecord {          // struct，避免堆分配
    public long   Frame;
    public double TimeSeconds;
    public int    EventKeyId;             // 字符串驻留表索引，不存字符串
    public int    EmitterPathId;
    public int    CallSiteId;
    public Vector3 EmitterPos;
    public Vector3 ListenerPos;
    public float  DistanceToListener;
    public PlaybackOutcome Outcome;
    public int    BackendResultCode;      // 原始错误码，保留供深挖
    public int    ParamSnapshotId;        // 指向快照池
}
```

字符串一律走驻留表存 `int`：这是环形缓冲能做到零 GC 的前提，也是日志体积可控的关键。

### 2.3 后端结果映射

这是整个模块最核心的设计工作。两个中间件的词汇完全不同，语义高度重合：

| 归一化 Outcome | FMOD 侧信号 | Wwise 侧信号 |
|---|---|---|
| HandleInvalid | `RESULT` 非 OK（事件描述查找失败） | `AKRESULT` 非 Success（PostEvent 返回无效 ID） |
| Rejected | 实例创建成功但 start 后立即 destroy 且未 started | PostEvent 返回无效 playing ID |
| Started | `EVENT_CALLBACK_TYPE.STARTED` | PostEvent 返回有效 playing ID |
| Virtualized | 回调中的虚拟化通知 | 需结合 profiler 侧信息，覆盖度较低 |
| Stolen | 未 started 即 destroy | `AkCallbackType` 结束回调 + 时长比对 |
| StoppedEarly | destroy 早于事件时长 | 结束回调早于事件时长 |

Wwise 侧的 Virtualized 覆盖度弱于 FMOD，这一点必须如实写进支持矩阵，不得为了对称而伪造判定。

---

## 3. Build phases

估算按单人全职计。

### Phase 0 · 门面与记录模型（3 人日）

- `AudioTrace.Post(eventKey, emitter)` 统一门面
- `IAudioRuntimeProbe` 接口、`AudioTraceRecord`、`PlaybackOutcome`
- Native 后端最简实现，先跑通「调用 → 记录 → 打印」

**出口条件**：无中间件环境下调用门面，Console 能打出结构化记录。

### Phase 1 · 环形缓冲与落盘（3 人日）

- 定容环形缓冲，溢出策略：丢最旧并计数（丢弃计数本身也要记录并在 UI 上告警）
- 字符串驻留表
- 后台线程批量写盘，主线程零阻塞
- 会话头：Unity 版本、平台、中间件版本、构建时间

**出口条件**：连续 10 分钟高频调用，主线程零 GC 分配（Profiler 验证）。

### Phase 2 · FMOD 结果归一化（5 人日）

- `RESULT` → `Outcome` 映射表
- `EventInstance.setCallback` 订阅起播、虚拟化、销毁回调
- 区分 Rejected / Stolen / StoppedEarly 三种「起播失败或早停」
- 保留原始 `BackendResultCode`

**出口条件**：§5.3 的七种构造场景全部产出正确 Outcome。

### Phase 3 · 上下文采集（3 人日）

- GameObject 层级路径（缓存，不每次拼接）
- 调用点：`[CallerFilePath]` + `[CallerLineNumber]`
- Listener 位置与距离
- 参数快照：事件发生时全量采一次，另按固定间隔采样全局参数；快照做差分存储

**出口条件**：单条记录点开能看到完整现场，无需回看代码即可判断触发条件是否合理。

### Phase 4 · 时间轴 UI（5 人日）

- `ListView` 虚拟化，行 = 事件或发声体，横轴 = 时间，色块 = Outcome
- 筛选：Outcome、事件名、发声体、时间区间
- 详情面板：参数快照、调用点（点击跳转到代码行）、距离
- 日志导入：拖拽 `.adtrace` 文件到窗口

**出口条件**：筛选 `Outcome != Started`，一屏看完全部静默失败。

### Phase 5 · Wwise 后端（4 人日）

- `AKRESULT` / `AkCallbackType` 映射
- 覆盖 HandleInvalid / Started / StoppedEarly；Rejected 与 Virtualized 尽力而为
- 更新支持矩阵

**出口条件**：支持矩阵与实测行为一致。

### Phase 6 · 盲区检测与文档（2 人日）

- 与模块 C 联动：静态扫出绕过门面直接调用中间件 API 的代码位置，报为 Info 级并在追踪窗口顶部提示「本次会话存在 N 处追踪盲区」
- 《音频问题排查手册》

**合计约 25 人日。**

---

## 4. 性能预算

采集层进包，预算是硬约束，不是目标值。

| 指标 | 预算 |
|---|---|
| 每帧 GC 分配（采集层） | 0 B |
| 单条记录采集耗时 | < 5 μs |
| 200 并发事件下每帧总开销 | < 0.5 ms |
| 环形缓冲常驻内存 | < 8 MB（默认 5 万条） |
| 10 分钟会话日志体积 | < 20 MB |
| `AUDIODOCTOR_TRACE` 关闭时的运行时开销 | 0（编译期消除） |

超出预算即视为 Phase 未完成，不得进入下一阶段。

---

## 5. 测试策略

### 5.1 单元测试（EditMode）

- 结果映射表：每个后端错误码 / 回调组合 → 期望 Outcome，表驱动
- 环形缓冲：填满、回绕、溢出计数、并发写入
- 序列化往返：写入后读回，逐字段相等
- 字符串驻留表：重复键复用、容量上限行为

### 5.2 性能测试（PlayMode）

用 `Unity.PerformanceTesting` 或自建计时，跑 §4 的每一项预算，失败即测试失败。GC 分配用 `Recorder.Get("GC.Alloc")` 断言为 0。

### 5.3 正确性测试：七种 Outcome 的确定性构造

这是本模块最难也最关键的测试。每种 Outcome 必须有一个**可重复触发**的测试场景：

| Outcome | 构造方法 |
|---|---|
| HandleInvalid | 不加载 bank 直接 post；或 post 一个拼错的事件名 |
| Rejected | 事件设 max instance = 1 且拒绝新实例，连续 post 两次 |
| Started | 正常 post 一个已加载的事件 |
| Virtualized | 把发声体放到衰减范围外；或超出全局发声数上限 |
| Stolen | 发声数上限 + 抢占最旧策略，post 超量事件 |
| StoppedEarly | post 后在事件时长内主动 stop |
| 空间化异常 | 发声体与 Listener 距离超阈值——记录 `DistanceToListener` 并断言 |

每个场景写成一个 PlayMode 测试，断言追踪器记录的 Outcome 与预期一致。**这张表同时就是排查手册的骨架**——它把"没声音"翻译成了七个可验证的判断。

### 5.4 回归

每修复一个真实 bug，补一个对应的 PlayMode 场景。

---

## 6. 验收标准

| # | 标准 | 阈值 |
|---|---|---|
| 1 | 七种 Outcome 在 FMOD 后端全部被正确区分 | 100%，对照 §5.3 |
| 2 | 采集层每帧 GC 分配 | 0 B |
| 3 | 200 并发事件下每帧开销 | < 0.5 ms |
| 4 | `AUDIODOCTOR_TRACE` 关闭时的构建产物中无采集层代码 | IL 检查确认 |
| 5 | 10 分钟会话日志体积 | < 20 MB |
| 6 | 从打包构建导出的日志可在编辑器完整还原 | 通过 |
| 7 | 环形缓冲溢出时有明确告警，不静默丢数据 | 通过 |
| 8 | 支持矩阵与实测行为一致 | 无出入 |
| 9 | 全部单元 / 性能 / PlayMode 测试通过 | 100% |

---

## 7. 已知边界

必须在 README 首屏写明，不得含糊：

- **追踪盲区**：绕过统一门面、直接调用中间件 API 的代码不会被记录。模块 C 会扫出这些位置并在追踪窗口提示，但无法追踪其行为。
- **Wwise 虚拟化判定覆盖度低于 FMOD**，原因见 §2.3。
- **`NotCalled` 不由本模块产生**：追踪器只能记录发生过的调用，"代码根本没跑到"需要静态分析或断点配合。这是设计上的边界，不是缺陷。

主动写清工具的盲区，比宣称全覆盖更能说明你做过真实项目。

---

## 8. 交付物清单

- UPM 包源码（含 asmdef 与三类测试）
- 埋雷演示场景：每种 Outcome 至少一个可复现案例
- README：安装、门面接入方式、支持矩阵、已知边界
- 《音频问题排查手册》：症状 → 候选原因 → 用哪个视图查 → 如何确认
- 演示视频：3–5 分钟，展示从「玩家反馈某处没声音」到定位根因的完整路径

---

## 9. 风险与应对

| 风险 | 影响 | 应对 |
|---|---|---|
| 中间件回调时序与文档不符 | Outcome 判定错误 | §5.3 的构造场景就是防线；先写测试再写映射 |
| 门面接入需要改动现有调用点 | 落地阻力 | 提供 Roslyn Analyzer 或 C 模块的静态提示，逐步迁移而非一次性重构 |
| 高频事件下环形缓冲溢出 | 丢失关键记录 | 容量可配；溢出计数上报；提供按事件名的采样过滤 |
| 时间轴 UI 在数万条记录下卡顿 | 不可用 | `ListView` 虚拟化 + 预聚合分桶；性能测试列为 Phase 4 出口条件 |
