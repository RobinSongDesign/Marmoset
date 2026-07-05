"""Gymnasium environment backed by a Marmoset training server running in Rhino."""

from __future__ import annotations

from typing import Any, Dict, Optional, Tuple

import gymnasium as gym
import numpy as np
from gymnasium import spaces

from .client import MarmosetClient, MarmosetProtocolError


def _observation_space_from_desc(desc: Dict[str, Any]) -> spaces.Box:
    """PROTOCOL.md: v1 observations are fixed-length vectors with +-inf bounds."""
    if desc.get("type") != "box":
        raise MarmosetProtocolError(
            f"Unsupported observation space type {desc.get('type')!r} "
            f"(protocol v1 only defines 'box')."
        )
    shape = tuple(int(d) for d in desc["shape"])
    return spaces.Box(low=-np.inf, high=np.inf, shape=shape, dtype=np.float32)


def _action_space_from_desc(desc: Dict[str, Any]) -> gym.Space:
    space_type = desc.get("type")
    if space_type == "discrete":
        return spaces.Discrete(int(desc["n"]))
    if space_type == "multi_discrete":
        return spaces.MultiDiscrete([int(n) for n in desc["nvec"]])
    if space_type == "box":
        shape = tuple(int(d) for d in desc["shape"])
        return spaces.Box(
            low=np.float32(desc["low"]),
            high=np.float32(desc["high"]),
            shape=shape,
            dtype=np.float32,
        )
    raise MarmosetProtocolError(f"Unsupported action space type {space_type!r}.")


class MarmosetEnv(gym.Env):
    """A remote Marmoset environment exposed through the Gymnasium API.

    Connecting and the protocol handshake happen in the constructor, so
    ``observation_space`` / ``action_space`` are available immediately.

    Parameters
    ----------
    host, port:
        Address of the Training Server component running inside Rhino
        (default ``127.0.0.1:5555``).
    timeout:
        Socket timeout in seconds for connecting and for every reply.
    env_id:
        Environment index within the server; must be ``0`` in protocol v1.
    """

    metadata = {"render_modes": []}

    def __init__(
        self,
        host: str = "127.0.0.1",
        port: int = 5555,
        timeout: float = 60.0,
        env_id: int = 0,
    ):
        super().__init__()
        self._env_id = int(env_id)
        self._client = MarmosetClient(host, port, timeout=timeout)
        self._client.connect()
        ack = self._client.handshake()
        self.observation_space = _observation_space_from_desc(ack["observation_space"])
        self.action_space = _action_space_from_desc(ack["action_space"])

    # ------------------------------------------------------------------ #

    def _to_observation(self, values: Any) -> np.ndarray:
        obs = np.asarray(values, dtype=np.float32)
        return obs.reshape(self.observation_space.shape)

    def reset(
        self,
        *,
        seed: Optional[int] = None,
        options: Optional[Dict[str, Any]] = None,
    ) -> Tuple[np.ndarray, Dict[str, Any]]:
        super().reset(seed=seed)
        raw_obs = self._client.reset(env_id=self._env_id, seed=seed)
        return self._to_observation(raw_obs), {}

    def step(self, action: Any) -> Tuple[np.ndarray, float, bool, bool, Dict[str, Any]]:
        if isinstance(self.action_space, spaces.Discrete):
            raw = self._client.step(env_id=self._env_id, discrete=[int(action)])
        elif isinstance(self.action_space, spaces.MultiDiscrete):
            branches = [int(v) for v in np.asarray(action).reshape(-1)]
            raw = self._client.step(env_id=self._env_id, discrete=branches)
        elif isinstance(self.action_space, spaces.Box):
            values = [float(v) for v in np.asarray(action, dtype=np.float32).reshape(-1)]
            raw = self._client.step(env_id=self._env_id, continuous=values)
        else:  # pragma: no cover - constructor only builds the three types above
            raise TypeError(f"Unsupported action space: {self.action_space!r}")
        raw_obs, reward, terminated, truncated = raw
        return self._to_observation(raw_obs), float(reward), terminated, truncated, {}

    def close(self) -> None:
        self._client.close()
        super().close()
