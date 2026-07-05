"""marmoset-rl: Gymnasium-compatible client for the Marmoset RL framework (Rhino/Grasshopper)."""

from .client import (
    API_VERSION,
    MarmosetClient,
    MarmosetConnectionError,
    MarmosetError,
    MarmosetProtocolError,
    MarmosetTimeoutError,
)
from .env import MarmosetEnv

__version__ = "0.1.0"

__all__ = [
    "API_VERSION",
    "MarmosetClient",
    "MarmosetConnectionError",
    "MarmosetEnv",
    "MarmosetError",
    "MarmosetProtocolError",
    "MarmosetTimeoutError",
    "__version__",
]
