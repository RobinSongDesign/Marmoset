# Marmoset 通信协议 v1

C#（Rhino 内 TrainingServer）与 Python（marmoset-rl 客户端）之间的线上协议。
**本文件是两侧实现的唯一契约，任何改动必须同时更新两侧代码与本文件。**

## 传输层

- TCP，服务端在 Rhino 进程内监听，默认端口 **5555**，仅接受**单个客户端**（v1）。
- 消息帧：`4 字节小端 uint32 长度前缀` + `MessagePack 载荷`。长度不含前缀本身。
- 严格请求-响应：客户端发一条，服务端必回一条，无流水线、无服务端主动推送。
- 服务端遇到协议错误回 `error` 消息（连接保持）；连接断开视作训练会话结束，服务端回到监听状态。

## 消息格式

所有消息是 string-key 的 MessagePack map，`type` 字段标识消息类型。
数值编码：观测/奖励用 float（msgpack float32/float64 均可，C# 侧统一按 float 读），动作离散值用 int。

### 客户端 → 服务端

| type | 字段 | 说明 |
|------|------|------|
| `handshake` | `api_version: int` | 必须是第一条消息。版本当前为 `1`，不匹配时服务端回 error 并断开 |
| `reset` | `env_id: int`, `seed: int \| nil` | 重置回合。`env_id` v1 恒为 `0`（多环境预留） |
| `step` | `env_id: int`, `discrete: [int] \| nil`, `continuous: [float] \| nil` | 二选一，与动作空间匹配 |
| `close` | — | 客户端主动结束会话，服务端回 `close_ack` 后断开 |

### 服务端 → 客户端

| type | 字段 |
|------|------|
| `handshake_ack` | `api_version: int`, `observation_space: Space`, `action_space: Space` |
| `reset_result` | `observation: [float]` |
| `step_result` | `observation: [float]`, `reward: float`, `terminated: bool`, `truncated: bool` |
| `close_ack` | — |
| `error` | `message: string` |

### Space 描述

```jsonc
// 观测空间（v1 只有定长向量，边界视作 ±inf）
{ "type": "box", "shape": [11] }

// 动作空间三选一：
{ "type": "discrete", "n": 3 }                    // ActionSpec.Discrete(3)
{ "type": "multi_discrete", "nvec": [3, 2] }      // ActionSpec.Discrete(3, 2)
{ "type": "box", "shape": [2], "low": -1.0, "high": 1.0 }  // ActionSpec.Continuous(2)，约定 [-1,1]
```

Python 侧映射：`box` 观测 → `gym.spaces.Box(-inf, inf, shape, float32)`；
`discrete`→`Discrete(n)`；`multi_discrete`→`MultiDiscrete(nvec)`；`box` 动作→`Box(low, high, shape, float32)`。

### 状态机规则

1. `handshake` 之前的任何消息 → `error`。
2. 首次 `step` 之前必须有过 `reset`。
3. `step_result` 返回 `terminated=true` 或 `truncated=true` 后，下一条必须是 `reset`（否则 `error`）。

### 边界行为澄清（与两侧实现一致）

- **error 后是否断开**：仅 `handshake` 版本不匹配的 error 会随后断开连接；其余所有 error（协议违规、动作非法、环境异常等）连接保持，客户端可继续发送合法消息。
- **重复 `handshake`**：握手完成后再次收到 `handshake` → `error`（连接保持）。
- **`env_id` 缺失**：`reset`/`step` 中缺失 `env_id` 字段按 `0` 宽容处理；显式非 0 值 → `error`。
- **`step` 动作字段**：服务端按自身动作空间读取对应字段（离散空间读 `discrete`，连续读 `continuous`），另一字段被忽略；对应字段为 nil 或缺失 → `error`。客户端应把与动作空间不符的字段置 nil。
- **ONNX 输出类型**：输出张量元素类型既非 int64 也非 float32 时，`OnnxPolicy` 在加载期直接失败（快速失败），而非推理期出错。

## ONNX 模型约定

Python 侧导出、C# `OnnxPolicy` 加载的模型必须满足：

- 输入：名为 `observation`，`float32 [batch, obs_dim]`。
- 输出：名为 `action`：
  - 离散空间 → `int64 [batch, num_branches]`（已 argmax 的确定性动作；单分支时 num_branches=1）
  - 连续空间 → `float32 [batch, act_dim]`（确定性均值动作，已 clip 到 [-1,1]）
- 导出器负责把 SB3 策略包装成上述确定性形式（`samples/python/export_onnx.py`）。
