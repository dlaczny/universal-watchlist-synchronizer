from __future__ import annotations

import io
import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.utils.logging import ensure_utf8_stream


def test_ensure_utf8_stream_reconfigures_cp1250_stream_for_unicode_output():
    buffer = io.BytesIO()
    stream = io.TextIOWrapper(buffer, encoding="cp1250")

    configured = ensure_utf8_stream(stream)
    configured.write("SirÄt")
    configured.flush()

    assert buffer.getvalue() == "SirÄt".encode("utf-8")

