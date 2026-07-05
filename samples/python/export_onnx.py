#!/usr/bin/env python
"""Export a trained stable-baselines3 model to ONNX for Python-free inference in Rhino.

The exported graph follows the "ONNX 模型约定" section of docs/PROTOCOL.md, which
is what the C# ``OnnxPolicy`` loader expects:

- Input : name ``observation``, ``float32 [batch, obs_dim]``.
- Output: name ``action``:
    * Discrete / MultiDiscrete -> ``int64 [batch, num_branches]`` (argmax per
      branch; a single-branch Discrete space still yields ``[batch, 1]``).
    * Box (continuous)         -> ``float32 [batch, act_dim]`` deterministic
      mean action, clipped to ``[-1, 1]``.

Usage
-----
    python export_onnx.py snake_ppo.zip --output snake_ppo.onnx

Requires the training extras:

    pip install marmoset-rl[train]
"""

from __future__ import annotations

import argparse
import sys


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("model", help="Path to the SB3 model .zip (e.g. from train_snake.py)")
    parser.add_argument("--output", default=None,
                        help="Output .onnx path (default: model path with .onnx suffix)")
    parser.add_argument("--opset", type=int, default=17,
                        help="ONNX opset version (default: %(default)s)")
    return parser.parse_args()


def main() -> None:
    args = parse_args()

    try:
        import torch as th
    except ImportError:
        sys.exit(
            "torch is required to export ONNX but is not installed.\n"
            "Install the training extras first:\n\n    pip install marmoset-rl[train]\n"
        )
    try:
        from stable_baselines3 import PPO
    except ImportError:
        sys.exit(
            "stable-baselines3 is required to load the model but is not installed.\n"
            "Install the training extras first:\n\n    pip install marmoset-rl[train]\n"
        )
    try:
        import onnx
    except ImportError:
        sys.exit(
            "onnx is required to validate the exported model but is not installed.\n"
            "Install the training extras first:\n\n    pip install marmoset-rl[train]\n"
        )

    import numpy as np
    from gymnasium import spaces

    class DeterministicPolicy(th.nn.Module):
        """Wraps an SB3 ActorCriticPolicy into the deterministic form PROTOCOL.md requires."""

        def __init__(self, policy: th.nn.Module, action_space):
            super().__init__()
            self.policy = policy
            if isinstance(action_space, spaces.Discrete):
                self.branch_sizes = [int(action_space.n)]
            elif isinstance(action_space, spaces.MultiDiscrete):
                self.branch_sizes = [int(n) for n in action_space.nvec]
            elif isinstance(action_space, spaces.Box):
                self.branch_sizes = None  # continuous
            else:
                raise TypeError(f"Unsupported action space: {action_space!r}")

        def forward(self, observation: "th.Tensor") -> "th.Tensor":
            features = self.policy.extract_features(observation)
            if isinstance(features, tuple):  # non-shared features extractor (SB3 >= 2.0)
                features = features[0]
            latent_pi = self.policy.mlp_extractor.forward_actor(features)
            raw = self.policy.action_net(latent_pi)
            if self.branch_sizes is None:
                # Box: deterministic mean action, clipped per the [-1, 1] convention.
                return th.clamp(raw, -1.0, 1.0).to(th.float32)
            # Discrete / MultiDiscrete: argmax the logits of each branch.
            branch_actions = []
            offset = 0
            for size in self.branch_sizes:
                branch_actions.append(th.argmax(raw[:, offset:offset + size], dim=1))
                offset += size
            return th.stack(branch_actions, dim=1)  # int64 [batch, num_branches]

    print(f"Loading SB3 model from {args.model} ...")
    model = PPO.load(args.model, device="cpu")
    policy = model.policy
    if not hasattr(policy, "mlp_extractor") or not hasattr(policy, "action_net"):
        sys.exit(
            f"Unsupported policy type {type(policy).__name__}: this exporter handles "
            f"SB3 ActorCriticPolicy models (PPO/A2C MlpPolicy)."
        )
    policy.to("cpu").eval()

    obs_shape = model.observation_space.shape
    if obs_shape is None or len(obs_shape) != 1:
        sys.exit(
            f"Unsupported observation space shape {obs_shape!r}: Marmoset v1 uses "
            f"flat float vectors [obs_dim]."
        )
    obs_dim = int(np.prod(obs_shape))

    wrapper = DeterministicPolicy(policy, model.action_space)
    dummy = th.zeros(1, obs_dim, dtype=th.float32)

    output_path = args.output or (args.model.rsplit(".", 1)[0] + ".onnx")
    print(f"Exporting to {output_path} (opset {args.opset}) ...")
    with th.no_grad():
        th.onnx.export(
            wrapper,
            dummy,
            output_path,
            input_names=["observation"],
            output_names=["action"],
            dynamic_axes={"observation": {0: "batch"}, "action": {0: "batch"}},
            opset_version=args.opset,
        )

    onnx_model = onnx.load(output_path)
    onnx.checker.check_model(onnx_model)

    with th.no_grad():
        sample_action = wrapper(dummy)
    kind = ("continuous float32" if wrapper.branch_sizes is None
            else f"discrete int64, {len(wrapper.branch_sizes)} branch(es)")
    print(f"OK: 'observation' float32 [batch, {obs_dim}] -> "
          f"'action' {kind}, shape [batch, {sample_action.shape[1]}]")
    print(f"Model validated and saved to {output_path}")


if __name__ == "__main__":
    main()
