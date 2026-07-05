# marmoset-rl

Gymnasium-compatible Python client for **Marmoset**, the reinforcement learning
framework for Rhino/Grasshopper. The C# side (inside Rhino) hosts the environment
and a TCP training server; this package is the Python client that speaks the
Marmoset wire protocol (TCP + 4-byte little-endian length prefix + MessagePack,
see `docs/PROTOCOL.md` in the repository).

## Install

```bash
pip install marmoset-rl            # client only
pip install marmoset-rl[train]     # + stable-baselines3 / torch / onnx
```

## Usage

1. In Rhino/Grasshopper, wire an environment into the **Training Server**
   component and start it (default port `5555`).
2. In Python:

```python
from marmoset_rl import MarmosetEnv

env = MarmosetEnv(host="127.0.0.1", port=5555)
obs, info = env.reset(seed=42)
for _ in range(100):
    obs, reward, terminated, truncated, info = env.step(env.action_space.sample())
    if terminated or truncated:
        obs, info = env.reset()
env.close()
```

Because `MarmosetEnv` is a standard `gymnasium.Env`, off-the-shelf algorithms
(e.g. stable-baselines3) work out of the box — see `samples/python/train_snake.py`
and `samples/python/export_onnx.py` in the repository.

For low-level access (custom loops, debugging the protocol) use
`marmoset_rl.MarmosetClient` directly.
