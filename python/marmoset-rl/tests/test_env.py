"""Gymnasium-level tests for MarmosetEnv: space mapping, semantics, API compliance."""

import gymnasium as gym
import numpy as np
import pytest
from gymnasium import spaces
from gymnasium.utils.env_checker import check_env

from marmoset_rl import MarmosetEnv, MarmosetProtocolError
from mock_server import BOX_SPACE, DISCRETE_SPACE, MULTI_DISCRETE_SPACE, MockMarmosetServer


def make_env(server, **kwargs):
    return MarmosetEnv(host="127.0.0.1", port=server.port, timeout=5.0, **kwargs)


# --------------------------------------------------------------------- #
# Space mapping (PROTOCOL.md "Space 描述")
# --------------------------------------------------------------------- #

def test_observation_space_mapping():
    with MockMarmosetServer(obs_shape=(11,)) as server:
        env = make_env(server)
        assert env.observation_space == spaces.Box(-np.inf, np.inf, (11,), np.float32)
        env.close()


def test_discrete_action_space_mapping():
    with MockMarmosetServer(action_space=DISCRETE_SPACE) as server:
        env = make_env(server)
        assert env.action_space == spaces.Discrete(3)
        env.close()


def test_multi_discrete_action_space_mapping():
    with MockMarmosetServer(action_space=MULTI_DISCRETE_SPACE) as server:
        env = make_env(server)
        assert isinstance(env.action_space, spaces.MultiDiscrete)
        assert list(env.action_space.nvec) == [3, 2]
        env.close()


def test_box_action_space_mapping():
    with MockMarmosetServer(action_space=BOX_SPACE) as server:
        env = make_env(server)
        assert env.action_space == spaces.Box(-1.0, 1.0, (2,), np.float32)
        env.close()


# --------------------------------------------------------------------- #
# reset / step semantics
# --------------------------------------------------------------------- #

def test_reset_and_step_return_gymnasium_tuples():
    with MockMarmosetServer(obs_shape=(4,), episode_length=2) as server:
        env = make_env(server)
        obs, info = env.reset(seed=42)
        assert isinstance(obs, np.ndarray)
        assert obs.dtype == np.float32 and obs.shape == (4,)
        assert info == {}

        obs, reward, terminated, truncated, info = env.step(env.action_space.sample())
        assert obs.dtype == np.float32 and obs.shape == (4,)
        assert isinstance(reward, float)
        assert (terminated, truncated) == (False, False)
        assert info == {}

        _, _, terminated, truncated, _ = env.step(env.action_space.sample())
        assert terminated and not truncated
        env.close()


def test_reset_seed_determinism_through_env():
    with MockMarmosetServer() as server:
        env = make_env(server)
        obs_a, _ = env.reset(seed=99)
        obs_b, _ = env.reset(seed=99)
        np.testing.assert_array_equal(obs_a, obs_b)
        env.close()


def test_step_after_terminated_without_reset_raises():
    with MockMarmosetServer(episode_length=1) as server:
        env = make_env(server)
        env.reset()
        _, _, terminated, _, _ = env.step(0)
        assert terminated
        with pytest.raises(MarmosetProtocolError, match="reset"):
            env.step(0)
        env.reset()
        env.step(0)
        env.close()


# --------------------------------------------------------------------- #
# Action encoding on the wire (numpy -> protocol fields)
# --------------------------------------------------------------------- #

def _last_step_msg(server):
    return [m for m in server.received if m.get("type") == "step"][-1]


def test_discrete_action_encoding():
    with MockMarmosetServer(action_space=DISCRETE_SPACE) as server:
        env = make_env(server)
        env.reset()
        env.step(np.int64(2))  # numpy scalar, as sampled from Discrete
        msg = _last_step_msg(server)
        assert msg["env_id"] == 0
        assert msg["discrete"] == [2]
        assert all(type(v) is int for v in msg["discrete"])
        assert msg["continuous"] is None
        env.close()


def test_multi_discrete_action_encoding():
    with MockMarmosetServer(action_space=MULTI_DISCRETE_SPACE) as server:
        env = make_env(server)
        env.reset()
        env.step(np.array([2, 1], dtype=np.int64))
        msg = _last_step_msg(server)
        assert msg["discrete"] == [2, 1]
        assert msg["continuous"] is None
        env.close()


def test_box_action_encoding():
    with MockMarmosetServer(action_space=BOX_SPACE) as server:
        env = make_env(server)
        env.reset()
        env.step(np.array([0.25, -1.0], dtype=np.float32))
        msg = _last_step_msg(server)
        assert msg["discrete"] is None
        assert msg["continuous"] == pytest.approx([0.25, -1.0])
        assert all(type(v) is float for v in msg["continuous"])
        env.close()


def test_reset_seed_encoding():
    with MockMarmosetServer() as server:
        env = make_env(server)
        env.reset(seed=1234)
        env.reset()
        resets = [m for m in server.received if m.get("type") == "reset"]
        assert resets[0]["seed"] == 1234 and type(resets[0]["seed"]) is int
        assert resets[1]["seed"] is None
        assert all(m["env_id"] == 0 for m in resets)
        env.close()


# --------------------------------------------------------------------- #
# Gymnasium API compliance
# --------------------------------------------------------------------- #

@pytest.mark.parametrize(
    "action_space",
    [DISCRETE_SPACE, MULTI_DISCRETE_SPACE, BOX_SPACE],
    ids=["discrete", "multi_discrete", "box"],
)
def test_gymnasium_check_env(action_space):
    with MockMarmosetServer(obs_shape=(6,), action_space=action_space, episode_length=4) as server:
        env = make_env(server)
        assert isinstance(env, gym.Env)
        check_env(env, skip_render_check=True)
        env.close()
