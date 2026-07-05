"""Protocol-level tests for MarmosetClient against the in-process mock server."""

import socket

import pytest

from marmoset_rl import (
    MarmosetClient,
    MarmosetConnectionError,
    MarmosetProtocolError,
    MarmosetTimeoutError,
)
from mock_server import BOX_SPACE, MockMarmosetServer


def make_client(server, timeout=5.0):
    client = MarmosetClient("127.0.0.1", server.port, timeout=timeout)
    client.connect()
    return client


# --------------------------------------------------------------------- #
# Handshake / version negotiation
# --------------------------------------------------------------------- #

def test_handshake_returns_space_descriptions():
    with MockMarmosetServer(obs_shape=(11,)) as server:
        client = make_client(server)
        ack = client.handshake()
        assert ack["api_version"] == 1
        assert ack["observation_space"] == {"type": "box", "shape": [11]}
        assert ack["action_space"] == {"type": "discrete", "n": 3}
        client.close()


def test_handshake_version_mismatch_raises_and_server_disconnects():
    with MockMarmosetServer() as server:
        client = make_client(server)
        with pytest.raises(MarmosetProtocolError, match="api_version"):
            client.handshake(api_version=99)
        # PROTOCOL.md: on version mismatch the server replies error and disconnects.
        with pytest.raises((MarmosetConnectionError, MarmosetTimeoutError)):
            client.reset()


def test_message_before_handshake_is_rejected():
    with MockMarmosetServer() as server:
        client = make_client(server)
        with pytest.raises(MarmosetProtocolError, match="handshake"):
            client.reset()
        # The connection stays open: handshaking afterwards works.
        assert client.handshake()["api_version"] == 1
        client.close()


# --------------------------------------------------------------------- #
# reset / step flow and the episode state machine
# --------------------------------------------------------------------- #

def test_full_episode_flow_discrete():
    with MockMarmosetServer(obs_shape=(4,), episode_length=3) as server:
        client = make_client(server)
        client.handshake()
        obs = client.reset(seed=7)
        assert isinstance(obs, list) and len(obs) == 4

        results = []
        for _ in range(3):
            results.append(client.step(discrete=[1]))
        for obs, reward, terminated, truncated in results:
            assert len(obs) == 4
            assert isinstance(reward, float)
            assert isinstance(terminated, bool)
            assert isinstance(truncated, bool)
        assert [r[2] for r in results] == [False, False, True]
        client.close()


def test_reset_seed_is_deterministic():
    with MockMarmosetServer() as server:
        client = make_client(server)
        client.handshake()
        assert client.reset(seed=123) == client.reset(seed=123)
        client.close()


def test_step_before_reset_is_rejected_then_recoverable():
    with MockMarmosetServer() as server:
        client = make_client(server)
        client.handshake()
        with pytest.raises(MarmosetProtocolError, match="reset"):
            client.step(discrete=[0])
        # error keeps the connection: a reset fixes the state machine
        client.reset()
        client.step(discrete=[0])
        client.close()


def test_step_after_terminated_requires_reset():
    with MockMarmosetServer(episode_length=1) as server:
        client = make_client(server)
        client.handshake()
        client.reset()
        _, _, terminated, _ = client.step(discrete=[0])
        assert terminated
        with pytest.raises(MarmosetProtocolError, match="reset"):
            client.step(discrete=[0])
        client.reset()
        client.step(discrete=[0])  # fine again
        client.close()


def test_continuous_step_and_mismatched_action_error():
    with MockMarmosetServer(action_space=BOX_SPACE) as server:
        client = make_client(server)
        client.handshake()
        client.reset()
        obs, reward, terminated, truncated = client.step(continuous=[0.5, -0.5])
        assert not terminated and not truncated
        with pytest.raises(MarmosetProtocolError, match="discrete"):
            client.step(discrete=[1])
        client.close()


def test_step_requires_exactly_one_action_kind():
    client = MarmosetClient()
    with pytest.raises(ValueError):
        client.step()
    with pytest.raises(ValueError):
        client.step(discrete=[0], continuous=[0.0])


# --------------------------------------------------------------------- #
# close / connection failures / timeout
# --------------------------------------------------------------------- #

def test_close_sends_close_and_server_returns_to_listening():
    with MockMarmosetServer() as server:
        client = make_client(server)
        client.handshake()
        client.close()
        assert not client.connected
        assert server.received[-1] == {"type": "close"}
        # v1 accepts a single client at a time, but a new session may follow.
        second = make_client(server)
        assert second.handshake()["api_version"] == 1
        second.close()


def test_connect_refused_gives_clear_error():
    # Grab a port that is guaranteed to be closed.
    probe = socket.socket()
    probe.bind(("127.0.0.1", 0))
    dead_port = probe.getsockname()[1]
    probe.close()

    client = MarmosetClient("127.0.0.1", dead_port, timeout=2.0)
    with pytest.raises(MarmosetConnectionError, match="Training Server"):
        client.connect()


def test_reply_timeout_raises_timeout_error():
    with MockMarmosetServer(respond=False) as server:
        client = make_client(server, timeout=0.5)
        with pytest.raises(MarmosetTimeoutError, match="0.5"):
            client.handshake()


def test_request_without_connect_raises():
    client = MarmosetClient()
    with pytest.raises(MarmosetConnectionError, match="connect"):
        client.handshake()
