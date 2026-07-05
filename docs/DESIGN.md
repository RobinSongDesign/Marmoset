# Marmoset 项目定位与架构决策

> 2026-07-04 定稿。本文档记录项目定位讨论的全部结论，后续开发以此为准。

## 一句话定位

**Marmoset 是 Rhino/Grasshopper 生态里的 ML-Agents**：一个面向设计与建筑科研的仿真运行时 + 强化学习训练框架。内置小游戏作为框架的测试场景与趣味演示（可持续扩充，供用户游玩），但项目本体不是游戏引擎。

## 核心决策清单

### 1. 定位收敛
- 砍掉"通用轻量游戏引擎"这个中间目标，只保留 RL 框架所需的最小仿真运行时。
- 游戏（Snake/Tetris 及后续新增）降级为示例环境 + 演示素材 + 用户趣味内容。

### 2. 第一版能力边界
- 回合制、单智能体、离散/连续动作、向量观测（float 数组）。
- 多智能体、图像观测、外部性能分析器（日照/结构等）接入：留架构扩展点，第一版不实现。

### 3. 分层架构（最重要的约束）
- **Marmoset.Core**（纯 .NET 类库）：Agent 基类、环境运行时、step 循环、TCP 服务、ONNX 推理。**不引用 Grasshopper**，最多引用 RhinoCommon 几何类型。
- **Marmoset.GH**（.gha 插件）：训练服务器组件、可视化 conduit、游戏组件、DirectionPad。GH 只是"装配界面 + 观察窗"。
- **训练循环不经过 GH 求解器**：GH 求解只发生在装配时刻（改脚本、重新连线）；训练开始后由后台线程直接驱动 Agent 实例。

### 4. 训练可视化
- Rhino **DisplayConduit** 直接向视口绘制，不创建文档对象、不触发 GH 重算。
- 训练线程写状态快照到共享缓冲，conduit 按渲染帧率读取最新快照。
- 三种观看方式：默认快进（全速训练）、可切换减速逐步观看（调试用）、事后回放录制的 episode 轨迹。

### 5. Python 侧接口
- 交付 pip 包 **`marmoset-rl`**：Gymnasium 兼容环境类（`reset`/`step`/`observation_space`/`action_space`）+ 通信客户端。
- 通信协议：TCP + 长度前缀 + MessagePack（不用 gRPC/protobuf，单人项目不背那套工具链）。
- 成熟算法（SB3 等）开箱即用；因为是标准 gym 环境，后期自研/魔改算法零额外框架工作。

### 6. 进程拓扑
- Rhino 插件内起 TCP 服务器；Python 是客户端，**由 Python 驱动 step 节奏**（gym 语义的自然实现）。
- 第一版：单 Rhino 实例 + 单环境；但协议从一开始带 `env_id` 字段。
- 第二阶段：Rhino.Inside headless 并行环境，Python 侧换 VecEnv，训练脚本不变。

### 7. 环境定义方式
- C# 继承 `Agent` 基类，API 照 ML-Agents 形状：`OnEpisodeBegin` / `CollectObservations(sensor)` / `OnActionReceived(actions)` / `AddReward` / `EndEpisode`。
- **免重编译**：利用 Rhino 8 GH C# 脚本组件的 Roslyn 即时编译——用户在脚本组件里写 Agent 子类，组件输出 Agent 实例，沿连线流入"训练服务器"组件，交给核心运行时后台线程。改代码→重编译→重启训练会话，秒级循环，不碰 Visual Studio。
- 内置游戏走编译好的 dll，与脚本环境继承同一基类，运行时不区分来源。
- 纯连线无代码搭环境：无限期推迟（有训练需求的人倾向写代码）。

### 8. 推理部署（第一阶段就做）
- 训练完从 PyTorch 导出 ONNX，C# 侧用 `Microsoft.ML.OnnxRuntime` 在 Rhino 进程内推理，无需 Python。
- 战略意义：训练出的策略可封装成普通 GH 组件分发给不懂 RL 的用户；游戏获得"AI 演示模式"。

### 9. 平台范围
- **仅支持 Rhino 8**，砍掉 net48/Rhino 7。目标框架：`net7.0-windows` + `net7.0`（Mac 支持重要）。
- 硬约束：核心运行时与训练链路**不得依赖 WinForms**；UI 相关（如 DirectionPad 键盘钩子）用 Eto.Forms 重做或仅限 Windows。

### 10. 仓库结构（单仓库）

```text
Marmoset/
├── src/
│   ├── Marmoset.Core/      # 纯 .NET：Agent 基类、运行时、TCP、ONNX 推理
│   ├── Marmoset.GH/        # .gha：训练服务器、conduit、游戏组件
│   └── Marmoset.Games/     # 内置游戏纯逻辑（现有 Games/ 迁入，改造为 Agent 子类）
├── python/
│   └── marmoset-rl/        # pip 包：Gymnasium 环境 + 通信客户端
└── samples/                # GH 示例文件、训练脚本示例
```

### 11. 第一阶段验收标准（Definition of Done）

> 在 Rhino 里打开 Snake 环境 → 命令行跑 `python train.py`（SB3 PPO）→ 视口实时看到快进训练 → 收敛后导出 ONNX → Rhino 内加载模型，AI 自己玩贪吃蛇，无需 Python。

这条线跑通即验证全部架构决策。GH 脚本组件写 Agent、减速观察、回放系统、Tetris 改造均排在其后。

### 12. 开源
- GitHub 公开 + Food4Rhino/Yak 发布。文档按"给外人看"的标准写。
