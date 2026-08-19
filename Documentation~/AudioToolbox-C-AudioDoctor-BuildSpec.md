# AudioDoctor · 模块 C：静态体检（Validate）

> 版本 0.1 · 单人开发 · 目标：作品集交付物，同时可直接投入真实项目

## 1. 目标与边界

**目标**：在不运行游戏的前提下，把"中间件工程声明了什么"、"构建产物打包了什么"、"Unity 工程引用了什么"三方对账，输出可定位、可修复、可接入 CI 的问题清单。

**明确不做**：
- 不做音频内容质量判断（响度、频谱、削波）——那是 E 模块的范畴
- 不做运行时行为分析——那是 A 模块
- 不修改任何资源，只报告（自动修复留作 v0.2）

---

## 2. 技术栈

| 层 | 选型 | 理由 |
|---|---|---|
| 引擎 | Unity 2022.3 LTS | Wwise / FMOD 集成包对 LTS 支持最完整；非 LTS 版本集成包常滞后 |
| 语言 | C# / .NET Standard 2.1 | Unity 编辑器扩展的唯一选项 |
| UI | UI Toolkit（UXML + USS） | 跨 Win/Mac 一套代码；`ListView` 自带虚拟化；样式与逻辑分离便于测试 |
| 测试 | Unity Test Framework（NUnit），EditMode | 规则引擎不依赖运行时，全部可在 EditMode 覆盖 |
| CI | GitHub Actions + `-batchmode -executeMethod` | 证明工具能进构建流水线，对应 JD 第 5、7 条 |
| 分发 | UPM 包（Git URL 安装） | 引擎工具的标准交付形态 |
| 报告 | JSON（机器）+ Markdown（人） | JSON 供 CI 消费，Markdown 供音频设计师阅读 |

### 2.1 程序集划分（asmdef）

```
AudioDoctor.Core            Runtime  无外部依赖，只含归一化数据模型
AudioDoctor.Editor          Editor   规则引擎、扫描调度、UI、报告
AudioDoctor.Backend.Fmod    Editor   Define Constraint: AUDIODOCTOR_FMOD
AudioDoctor.Backend.Wwise   Editor   Define Constraint: AUDIODOCTOR_WWISE
AudioDoctor.Backend.Native  Editor   无约束，兜底后端
AudioDoctor.Tests.Editor    Editor   仅依赖 Core + Editor，不依赖任何后端
```

`AudioDoctor.Tests.Editor` 不依赖后端是硬约束：规则引擎的单元测试必须在没装任何中间件的机器上跑通。

### 2.2 中间件探测

一个 `[InitializeOnLoad]` 编辑器脚本探测集成程序集是否存在（`AppDomain` 中查找 `FMODUnity` / `AkSoundEngine` 类型），据此写入 / 清除 scripting define symbol。目的：任何人 clone 仓库后无论装了几个中间件都能直接编译通过。

### 2.3 核心数据模型（Core）

```csharp
public sealed class EventDef {
    public string Key;                       // 归一化标识（路径或名称）
    public string BackendId;                 // GUID / ShortID，后端各自的原生 ID
    public bool?  Is3D;                      // 可空 = 该后端无法提供
    public bool?  IsStreaming;
    public float? LengthSeconds;
    public IReadOnlyList<string> Parameters; // RTPC / Switch / State / parameter 统一收口
    public IReadOnlyList<string> BankNames;
    public IReadOnlyDictionary<string,string> Extras; // 后端特有信息，原样透传到 UI
}

public sealed class BankDef {
    public string Name;
    public string Platform;                  // 跨平台一致性检查用
    public long   SizeBytes;
    public IReadOnlyList<string> EventKeys;
}

public sealed class EventRefUsage {
    public string EventKey;
    public string AssetPath;                 // Prefab / Scene / .cs 文件
    public string ObjectPath;                // GameObject 层级路径，可空
    public RefSource Source;                 // SerializedField / CodeLiteral / Timeline / AnimationEvent
    public int Line;                         // 代码引用时有效
}

public sealed class ValidationIssue {
    public string RuleId;                    // R001…
    public Severity Severity;                // Error / Warning / Info
    public string Message;
    public string PrimaryAssetPath;          // 双击定位目标
    public string Detail;
}
```

可空字段是刻意设计：Wwise 与 FMOD 的能力集不一致，取并集而非最小公共子集。后端映射不了的概念放进 `Extras` 原样显示，绝不丢弃。

---

## 3. 规则清单

| ID | 名称 | 级别 | 依赖数据 |
|---|---|---|---|
| R001 | 悬空引用（引用了不存在的事件） | Error | authored + refs |
| R002 | 未打包（事件不属于任何 bank） | Error | authored + banks |
| R003 | 加载缺口（bank 在该场景无加载逻辑） | Error | banks + 场景扫描 |
| R004 | 孤儿资源（bank 内事件无人引用） | Warning | banks + refs |
| R005 | 加载策略错配（长音频常驻 / 短音效流式） | Warning | authored（需时长） |
| R006 | 2D/3D 错配 | Warning | authored + refs |
| R007 | 参数不匹配（代码设置了事件没有的参数） | Error | authored + refs |
| R008 | 命名规范违规 | Info | authored |
| R009 | 跨平台 bank 不一致（缺平台 / 大小写差异） | Error | banks |

R007 优先级最高：它在运行时是完全静默的失败，没有任何日志，是最难查的一类问题，也最能体现工具价值。

R009 对应 JD 第 3 条"平台兼容性"——macOS 与 Windows 文件系统大小写敏感度不同导致的事件名大小写不一致，是真实项目里的高频坑。

---

## 4. Build phases

估算按单人全职计，实际按业余时间投入应乘以 2–3。

### Phase 0 · 骨架与后端接缝（3 人日）

- UPM 包目录结构、六个 asmdef、中间件探测脚本
- `IAudioProjectSource` 接口定义、Core 数据模型
- Native 后端最简实现（用 `AudioClip` / `AudioSource` 冒充 event / ref）
- 一条 dummy 规则跑通「扫描 → 规则 → 报告」全链路

**出口条件**：在只装 Unity、不装任何中间件的机器上，菜单点一下能出一份含至少一条 issue 的报告。

### Phase 1 · FMOD 后端 + 引用扫描（5 人日）

- `GetAuthoredEvents` / `GetBanks`：读取 FMOD 集成的编辑器侧事件缓存
- `FindReferences` 三条采集路径：
  - 序列化字段——`AssetDatabase.FindAssets` 遍历 Prefab / Scene，`SerializedObject` 递归找事件引用字段
  - 代码字面量——正则或 Roslyn 扫 `.cs`，抓传入门面/中间件 API 的字符串
  - Timeline / AnimationEvent
- 本阶段只输出原始清单，不做规则判断

**出口条件**：对示例工程输出的事件清单、bank 清单、引用清单，与 FMOD Studio 里手动核对一致。

### Phase 2 · 规则引擎 + 基础四规则（4 人日）

- `IValidationRule` 接口、`RuleId`、`Severity`
- 规则集配置用 ScriptableObject（可开关、可调阈值、可设级别），对应 JD 第 7 条「资源规范可维护」
- 实现 R001 / R002 / R004 / R008——这四条只需要 authored + banks + refs，不依赖深度元数据

**出口条件**：单元测试覆盖这四条规则，全部通过。

### Phase 3 · 深度规则（4 人日）

- R003、R005、R006、R007、R009
- R003 需要额外扫描场景中的 bank 加载组件与代码调用
- R007 需要把代码里的参数设置调用与 `EventDef.Parameters` 对账

**出口条件**：九条规则全部在 fixture 工程上跑出预期结果。

### Phase 4 · UI 与报告（4 人日）

- UI Toolkit 窗口：问题列表，支持按级别 / 规则 / 资源三种分组，双击跳转到对应资产并高亮
- 报告导出：JSON + Markdown
- 扫描进度条与可取消

**出口条件**：非程序员能独立看懂报告并定位到问题资产。

### Phase 5 · Wwise 后端（4 人日）

- 解析 `SoundBanksInfo.xml` 与 `WwiseProjectData`
- 引用扫描适配 `AkEvent` 组件与 `WwiseObjectReference`
- 目标覆盖 R001 / R002 / R004 / R008 / R009；R005、R006、R007 视元数据可得性决定

**出口条件**：README 中有一张明确的「后端 × 规则」支持矩阵，未覆盖的格子写清原因。诚实标注边界比宣称完整支持更可信。

### Phase 6 · CI 与文档（3 人日）

- `-batchmode -executeMethod` 入口，支持 `--backend`、`--rules`、`--output`、`--fail-on` 参数
- 退出码：存在 Error 级问题返回非零
- GitHub Actions workflow 示例
- 《规则手册》：每条规则写清「症状 → 成因 → 修复步骤 → 如何验证」

**合计约 27 人日。**

---

## 5. 测试策略

三层，成本从低到高。

### 5.1 单元测试（规则引擎）

不依赖任何中间件、不依赖 Unity 资源。手写 `EventDef` / `BankDef` / `EventRefUsage` 数组作为输入，断言规则输出。

每条规则至少三个用例：

- 正例——应当报出
- 反例——不应报出（防误报）
- 边界——阈值临界、空集合、重名、大小写差异

**这是整套测试的地基**：因为规则只吃归一化模型，所以规则的正确性可以完全脱离中间件验证。这一点本身就是三层架构的直接收益，值得在文档里点明。

### 5.2 集成测试（fixture 工程）

`Tests/Fixtures/BrokenProject~` 下维护一个**故意做坏**的小型 Unity 工程，埋入已知缺陷，每个缺陷对应一个规则。

测试断言的是**集合相等**，不是包含关系：

```
Assert.That(actualIssues, Is.EquivalentTo(expectedIssues));
```

漏报和误报必须同时被捕获。只断言"检出了 R001"而不断言"没有多报"，会让工具在真实工程上噪音爆炸——这是校验类工具最常见的死法。

### 5.3 性能测试

生成一个合成的大工程（1000+ Prefab、500+ 事件、20+ bank），测全量扫描耗时与内存峰值。

---

## 6. 验收标准

全部可量化，逐条勾选。

| # | 标准 | 阈值 |
|---|---|---|
| 1 | fixture 工程中埋入的全部缺陷被检出 | 100% |
| 2 | fixture 工程中的误报 | 0 |
| 3 | 中型工程全量扫描耗时 | < 30 s |
| 4 | 扫描过程内存峰值增量 | < 500 MB |
| 5 | 三种环境下均能编译：只装 FMOD / 只装 Wwise / 都不装 | 全部通过 |
| 6 | CI 模式退出码正确（有 Error → 非零，仅 Warning → 零） | 通过 |
| 7 | 报告可读性：找一位非程序员试读，能独立定位到问题资产 | 通过 |
| 8 | 后端 × 规则支持矩阵与实际行为一致 | 无出入 |
| 9 | 全部单元测试与集成测试通过 | 100% |

### 回归约定

每修复一个真实 bug，必须在 fixture 工程里补一个对应的缺陷用例。fixture 工程只增不减——它是这个工具的记忆。

---

## 7. 交付物清单

- UPM 包源码（含 asmdef 与测试）
- `Tests/Fixtures/BrokenProject~` 演示工程
- README：安装、快速上手、后端 × 规则支持矩阵、已知限制
- 《音频资源校验规则手册》：九条规则的症状 / 成因 / 修复 / 验证
- CI 接入说明与 workflow 示例
- 演示视频：3–5 分钟，展示在埋雷工程上一次扫描定位全部问题

---

## 8. 风险与应对

| 风险 | 影响 | 应对 |
|---|---|---|
| 中间件集成包的编辑器 API 跨版本变动 | 后端适配器编译失败 | 后端适配器隔离在独立 asmdef；README 标注已验证的集成包版本 |
| 代码字面量扫描漏掉动态拼接的事件名 | 漏报 | 明确文档化为已知限制；同时把「事件名由变量拼接」本身报为 Info 级提示 |
| 引用扫描在大工程上过慢 | 体验差 | 增量扫描（只扫 dirty 资产）留作 v0.2；v0.1 先保证全量正确 |
| Wwise 元数据不足以支撑部分规则 | 支持矩阵不完整 | 接受并如实标注，不为了填满矩阵而降级规则语义 |
