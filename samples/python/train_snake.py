#!/usr/bin/env python
"""Train a PPO agent (stable-baselines3) on a Marmoset environment such as Snake.

Prerequisites
-------------
1. In Rhino/Grasshopper: load the Marmoset plugin, wire the Snake environment
   into the **Training Server** component and start it. The server listens on
   127.0.0.1:5555 by default, and accepts a single client.
2. Install the Python side with training extras:

       pip install marmoset-rl[train]

Usage
-----
    python train_snake.py --port 5555 --timesteps 200000 --save-path snake_ppo.zip

While training runs you can watch the fast-forwarded episodes directly in the
Rhino viewport. Afterwards, convert the saved model for Python-free inference
inside Rhino with:

    python export_onnx.py snake_ppo.zip --output snake_ppo.onnx
"""

from __future__ import annotations

import argparse
import sys


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--host", default="127.0.0.1",
                        help="Training server host (default: %(default)s)")
    parser.add_argument("--port", type=int, default=5555,
                        help="Training server port (default: %(default)s)")
    parser.add_argument("--timesteps", type=int, default=200_000,
                        help="Total environment steps to train for (default: %(default)s)")
    parser.add_argument("--save-path", default="snake_ppo.zip",
                        help="Where to save the trained SB3 model (default: %(default)s)")
    parser.add_argument("--seed", type=int, default=None,
                        help="Optional training seed")
    return parser.parse_args()


def main() -> None:
    args = parse_args()

    try:
        from stable_baselines3 import PPO
        from stable_baselines3.common.monitor import Monitor
    except ImportError:
        sys.exit(
            "stable-baselines3 (and torch) are required for training but are not "
            "installed.\nInstall the training extras first:\n\n"
            "    pip install marmoset-rl[train]\n"
        )

    from marmoset_rl import MarmosetConnectionError, MarmosetEnv

    print(f"Connecting to Marmoset training server at {args.host}:{args.port} ...")
    try:
        env = MarmosetEnv(host=args.host, port=args.port)
    except MarmosetConnectionError as exc:
        sys.exit(
            f"{exc}\n\nStart the Training Server component in Rhino/Grasshopper "
            f"before running this script."
        )

    print(f"Connected. observation_space={env.observation_space}, "
          f"action_space={env.action_space}")

    monitored = Monitor(env)
    model = PPO("MlpPolicy", monitored, verbose=1, seed=args.seed)
    try:
        model.learn(total_timesteps=args.timesteps, progress_bar=False)
        model.save(args.save_path)
        print(f"Training finished; model saved to {args.save_path}")
    finally:
        monitored.close()


if __name__ == "__main__":
    main()
