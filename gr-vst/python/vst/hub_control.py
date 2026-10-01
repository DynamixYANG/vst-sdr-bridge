"""Minimal VST Hub control-port client (TCP 127.0.0.1:19788)."""
from __future__ import annotations

import json
import socket
from typing import Any


class HubControl:
    def __init__(self, host: str = "127.0.0.1", port: int = 19788, timeout_s: float = 30.0):
        self.host = host
        self.port = int(port)
        self.timeout_s = float(timeout_s)

    def command(self, line: str) -> str:
        with socket.create_connection((self.host, self.port), timeout=self.timeout_s) as sock:
            sock.settimeout(self.timeout_s)
            sock.sendall((line + "\n").encode("utf-8"))
            with sock.makefile("r", encoding="utf-8", newline="\n") as reader:
                reply = reader.readline()
        if not reply:
            raise RuntimeError("Hub control connection closed without reply")
        return reply.strip()

    def ok(self, line: str) -> str:
        reply = self.command(line)
        if not reply.startswith("OK"):
            raise RuntimeError(reply)
        return reply

    def status(self) -> dict[str, Any]:
        return json.loads(self.command("STATUS"))

    def tx_stop(self) -> str:
        return self.ok("TXSTOP")

    def tx_start(self, config: dict[str, Any]) -> str:
        return self.ok("TXSTART " + json.dumps(config, separators=(",", ":")))
