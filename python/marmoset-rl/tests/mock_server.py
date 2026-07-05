"""In-process mock Marmoset training server for tests.

Implements docs/PROTOCOL.md v1 independently of ``marmoset_rl.client``:
TCP, 4-byte little-endian uint32 length prefix + MessagePack frames, the
handshake/reset/step/close message set, and the state-machine rules
(handshake first; reset before step; forced reset after terminated/truncated).

Episodes are deterministic functions of the reset seed so that Gymnasium's
``check_env`` seed-determinism checks pass.
"""

from __future__ import annotations

import socket
import struct
import threading
from typing import Any, Dict, List, Optional

import msgpack
import numpy as np

_LEN_PREFIX = struct.Struct("<I")

DISCRETE_SPACE = {"type": "discrete", "n": 3}
MULTI_DISCRETE_SPACE = {"type": "multi_discrete", "nvec": [3, 2]}
BOX_SPACE = {"type": "box", "shape": [2], "low": -1.0, "high": 1.0}


class MockMarmosetServer:
    """Single-client, sequential mock server. Use as a context manager."""

    def __init__(
        self,
        obs_shape=(4,),
        action_space: Optional[Dict[str, Any]] = None,
        episode_length: int = 5,
        api_version: int = 1,
        respond: bool = True,
    ):
        self.obs_shape = tuple(int(d) for d in obs_shape)
        self.action_desc = dict(action_space if action_space is not None else DISCRETE_SPACE)
        self.episode_length = episode_length
        self.api_version = api_version
        self.respond = respond  # False: read messages but never reply (timeout tests)
        self.received: List[Dict[str, Any]] = []
        self.port: Optional[int] = None
        self._listener: Optional[socket.socket] = None
        self._thread: Optional[threading.Thread] = None
        self._stopping = threading.Event()
        self._episode_counter = 0

    # ------------------------------------------------------------------ #

    def start(self) -> "MockMarmosetServer":
        self._listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._listener.bind(("127.0.0.1", 0))
        self._listener.listen(1)
        self.port = self._listener.getsockname()[1]
        self._thread = threading.Thread(target=self._serve, daemon=True)
        self._thread.start()
        return self

    def stop(self) -> None:
        self._stopping.set()
        if self._listener is not None:
            try:
                self._listener.close()
            except OSError:
                pass
        if self._thread is not None:
            self._thread.join(timeout=5)

    def __enter__(self) -> "MockMarmosetServer":
        return self.start()

    def __exit__(self, *exc_info: Any) -> None:
        self.stop()

    # ------------------------------------------------------------------ #
    # Framing
    # ------------------------------------------------------------------ #

    @staticmethod
    def _recv_exact(conn: socket.socket, n: int) -> Optional[bytes]:
        data = bytearray()
        while len(data) < n:
            chunk = conn.recv(n - len(data))
            if not chunk:
                return None
            data.extend(chunk)
        return bytes(data)

    def _recv_msg(self, conn: socket.socket) -> Optional[Dict[str, Any]]:
        header = self._recv_exact(conn, 4)
        if header is None:
            return None
        (length,) = _LEN_PREFIX.unpack(header)
        payload = self._recv_exact(conn, length)
        if payload is None:
            return None
        return msgpack.unpackb(payload, raw=False)

    @staticmethod
    def _send_msg(conn: socket.socket, msg: Dict[str, Any]) -> None:
        payload = msgpack.packb(msg, use_bin_type=True)
        conn.sendall(_LEN_PREFIX.pack(len(payload)) + payload)

    # ------------------------------------------------------------------ #
    # Server loop: accept clients sequentially until stopped
    # ------------------------------------------------------------------ #

    def _serve(self) -> None:
        while not self._stopping.is_set():
            try:
                conn, _addr = self._listener.accept()
            except OSError:
                return  # listener closed by stop()
            with conn:
                try:
                    self._handle_client(conn)
                except OSError:
                    pass  # client vanished; back to listening

    def _handle_client(self, conn: socket.socket) -> None:
        handshaken = False
        ready = False  # a reset happened and the episode is not over
        episode_step = 0
        rng = np.random.default_rng(0)

        while not self._stopping.is_set():
            msg = self._recv_msg(conn)
            if msg is None:
                return  # disconnect ends the session; go back to listening
            self.received.append(msg)
            if not self.respond:
                continue
            msg_type = msg.get("type")

            if msg_type == "handshake":
                if msg.get("api_version") != self.api_version:
                    self._send_msg(conn, {
                        "type": "error",
                        "message": f"unsupported api_version {msg.get('api_version')!r}, "
                                   f"server speaks {self.api_version}",
                    })
                    return  # protocol: version mismatch -> error, then disconnect
                handshaken = True
                self._send_msg(conn, {
                    "type": "handshake_ack",
                    "api_version": self.api_version,
                    "observation_space": {"type": "box", "shape": list(self.obs_shape)},
                    "action_space": dict(self.action_desc),
                })

            elif not handshaken:
                self._send_msg(conn, {
                    "type": "error",
                    "message": "handshake must be the first message",
                })

            elif msg_type == "reset":
                seed = msg.get("seed")
                self._episode_counter += 1
                rng_seed = seed if seed is not None else 1_000_000 + self._episode_counter
                rng = np.random.default_rng(rng_seed)
                episode_step = 0
                ready = True
                self._send_msg(conn, {
                    "type": "reset_result",
                    "observation": self._observation(rng),
                })

            elif msg_type == "step":
                if not ready:
                    self._send_msg(conn, {
                        "type": "error",
                        "message": "reset is required before step "
                                   "(no episode in progress or the last one ended)",
                    })
                    continue
                problem = self._validate_action(msg)
                if problem is not None:
                    self._send_msg(conn, {"type": "error", "message": problem})
                    continue
                episode_step += 1
                terminated = episode_step >= self.episode_length
                if terminated:
                    ready = False
                self._send_msg(conn, {
                    "type": "step_result",
                    "observation": self._observation(rng),
                    "reward": float(rng.uniform(-1.0, 1.0)),
                    "terminated": terminated,
                    "truncated": False,
                })

            elif msg_type == "close":
                self._send_msg(conn, {"type": "close_ack"})
                return

            else:
                self._send_msg(conn, {
                    "type": "error",
                    "message": f"unknown message type {msg_type!r}",
                })

    # ------------------------------------------------------------------ #

    def _observation(self, rng: np.random.Generator) -> List[float]:
        values = rng.random(self.obs_shape).astype(np.float32)
        return [float(v) for v in values.reshape(-1)]

    def _validate_action(self, msg: Dict[str, Any]) -> Optional[str]:
        """Return an error string if the step payload does not match the action space."""
        discrete = msg.get("discrete")
        continuous = msg.get("continuous")
        space_type = self.action_desc["type"]

        if space_type in ("discrete", "multi_discrete"):
            if continuous is not None:
                return "continuous action sent to a discrete action space"
            if not isinstance(discrete, list):
                return "step requires a 'discrete' int list for this action space"
            nvec = [self.action_desc["n"]] if space_type == "discrete" else list(self.action_desc["nvec"])
            if len(discrete) != len(nvec):
                return f"expected {len(nvec)} discrete branch(es), got {len(discrete)}"
            for value, n in zip(discrete, nvec):
                if not isinstance(value, int) or isinstance(value, bool):
                    return f"discrete action values must be ints, got {value!r}"
                if not 0 <= value < n:
                    return f"discrete action {value} out of range [0, {n})"
            return None

        # box
        if discrete is not None:
            return "discrete action sent to a continuous action space"
        if not isinstance(continuous, list):
            return "step requires a 'continuous' float list for this action space"
        expected = int(np.prod(self.action_desc["shape"]))
        if len(continuous) != expected:
            return f"expected {expected} continuous value(s), got {len(continuous)}"
        for value in continuous:
            if not isinstance(value, float):
                return f"continuous action values must be floats, got {value!r}"
        return None
