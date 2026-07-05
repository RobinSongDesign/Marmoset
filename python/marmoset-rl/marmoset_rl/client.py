"""Low-level TCP client for the Marmoset wire protocol v1.

Implements ``docs/PROTOCOL.md``: TCP transport, message frames made of a
4-byte little-endian uint32 length prefix followed by a MessagePack payload,
and the strict request/response message set (``handshake`` / ``reset`` /
``step`` / ``close``).
"""

from __future__ import annotations

import socket
import struct
from typing import Any, Dict, List, Optional, Sequence, Tuple

import msgpack

API_VERSION = 1
"""Protocol version implemented by this client (PROTOCOL.md v1)."""

_LEN_PREFIX = struct.Struct("<I")


class MarmosetError(Exception):
    """Base class for all marmoset-rl errors."""


class MarmosetConnectionError(MarmosetError):
    """The TCP connection could not be established or was lost."""


class MarmosetTimeoutError(MarmosetError):
    """The server did not respond within the configured timeout."""


class MarmosetProtocolError(MarmosetError):
    """The server reported an ``error`` message, or a reply violated the protocol."""


class MarmosetClient:
    """Blocking, single-connection client for a Marmoset training server.

    Typical use::

        client = MarmosetClient("127.0.0.1", 5555)
        client.connect()
        ack = client.handshake()
        obs = client.reset(seed=42)
        obs, reward, terminated, truncated = client.step(discrete=[1])
        client.close()
    """

    def __init__(self, host: str = "127.0.0.1", port: int = 5555, timeout: float = 60.0):
        self.host = host
        self.port = port
        self.timeout = timeout
        self._sock: Optional[socket.socket] = None

    # ------------------------------------------------------------------ #
    # Connection management
    # ------------------------------------------------------------------ #

    @property
    def connected(self) -> bool:
        return self._sock is not None

    def connect(self) -> None:
        """Open the TCP connection. No-op if already connected."""
        if self._sock is not None:
            return
        try:
            sock = socket.create_connection((self.host, self.port), timeout=self.timeout)
        except OSError as exc:
            # 连接阶段的 timeout（TimeoutError 是 OSError 子类，Windows 上对关闭端口
            # 常表现为超时而非 refused）与拒绝连接同义：服务器不可达。
            raise MarmosetConnectionError(
                f"Could not connect to the Marmoset training server at "
                f"{self.host}:{self.port} ({exc}). Is the Training Server "
                f"component running in Rhino/Grasshopper?"
            ) from exc
        sock.settimeout(self.timeout)
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self._sock = sock

    def _drop(self) -> None:
        if self._sock is not None:
            try:
                self._sock.close()
            except OSError:
                pass
            self._sock = None

    # ------------------------------------------------------------------ #
    # Framing: 4-byte little-endian uint32 length prefix + msgpack payload
    # ------------------------------------------------------------------ #

    def _send_msg(self, msg: Dict[str, Any]) -> None:
        payload = msgpack.packb(msg, use_bin_type=True)
        self._sock.sendall(_LEN_PREFIX.pack(len(payload)) + payload)

    def _recv_exact(self, n: int) -> bytes:
        chunks = bytearray()
        while len(chunks) < n:
            chunk = self._sock.recv(n - len(chunks))
            if not chunk:
                self._drop()
                raise MarmosetConnectionError(
                    "Connection closed by the Marmoset training server "
                    "(the training session on the Rhino side may have ended)."
                )
            chunks.extend(chunk)
        return bytes(chunks)

    def _recv_msg(self) -> Dict[str, Any]:
        (length,) = _LEN_PREFIX.unpack(self._recv_exact(4))
        payload = self._recv_exact(length)
        msg = msgpack.unpackb(payload, raw=False)
        if not isinstance(msg, dict):
            raise MarmosetProtocolError(
                f"Malformed server message: expected a map, got {type(msg).__name__}."
            )
        return msg

    def _request(self, msg: Dict[str, Any], expect: str) -> Dict[str, Any]:
        if self._sock is None:
            raise MarmosetConnectionError("Not connected. Call connect() first.")
        try:
            self._send_msg(msg)
            reply = self._recv_msg()
        except socket.timeout as exc:
            self._drop()
            raise MarmosetTimeoutError(
                f"No reply from the Marmoset training server within {self.timeout}s "
                f"while waiting for '{expect}'. The Rhino side may be busy or hung."
            ) from exc
        except MarmosetError:
            raise
        except OSError as exc:
            self._drop()
            raise MarmosetConnectionError(
                f"Connection to {self.host}:{self.port} failed while sending "
                f"'{msg.get('type')}': {exc}"
            ) from exc
        reply_type = reply.get("type")
        if reply_type == "error":
            raise MarmosetProtocolError(
                f"Server error: {reply.get('message', '<no message>')}"
            )
        if reply_type != expect:
            raise MarmosetProtocolError(
                f"Expected a '{expect}' reply, got {reply_type!r}."
            )
        return reply

    # ------------------------------------------------------------------ #
    # Protocol messages
    # ------------------------------------------------------------------ #

    def handshake(self, api_version: int = API_VERSION) -> Dict[str, Any]:
        """Perform the mandatory first exchange.

        Returns the full ``handshake_ack`` map, containing ``api_version``,
        ``observation_space`` and ``action_space`` descriptions.
        """
        ack = self._request({"type": "handshake", "api_version": api_version}, "handshake_ack")
        server_version = ack.get("api_version")
        if server_version != api_version:
            raise MarmosetProtocolError(
                f"Server negotiated api_version={server_version!r}, "
                f"client requested {api_version}."
            )
        return ack

    def reset(self, env_id: int = 0, seed: Optional[int] = None) -> List[float]:
        """Reset the episode; returns the initial observation vector."""
        msg: Dict[str, Any] = {
            "type": "reset",
            "env_id": int(env_id),
            "seed": None if seed is None else int(seed),
        }
        reply = self._request(msg, "reset_result")
        return reply["observation"]

    def step(
        self,
        env_id: int = 0,
        discrete: Optional[Sequence[int]] = None,
        continuous: Optional[Sequence[float]] = None,
    ) -> Tuple[List[float], float, bool, bool]:
        """Advance one step. Exactly one of ``discrete``/``continuous`` must be given.

        Returns ``(observation, reward, terminated, truncated)``.
        """
        if (discrete is None) == (continuous is None):
            raise ValueError(
                "step() requires exactly one of 'discrete' or 'continuous', "
                "matching the environment's action space."
            )
        msg: Dict[str, Any] = {
            "type": "step",
            "env_id": int(env_id),
            "discrete": None if discrete is None else [int(v) for v in discrete],
            "continuous": None if continuous is None else [float(v) for v in continuous],
        }
        reply = self._request(msg, "step_result")
        return (
            reply["observation"],
            float(reply["reward"]),
            bool(reply["terminated"]),
            bool(reply["truncated"]),
        )

    def close(self) -> None:
        """Send ``close``, wait for ``close_ack`` (best effort), and disconnect."""
        if self._sock is None:
            return
        try:
            self._request({"type": "close"}, "close_ack")
        except MarmosetError:
            pass  # best effort: we are tearing the session down anyway
        finally:
            self._drop()

    # ------------------------------------------------------------------ #

    def __enter__(self) -> "MarmosetClient":
        self.connect()
        return self

    def __exit__(self, *exc_info: Any) -> None:
        self.close()
