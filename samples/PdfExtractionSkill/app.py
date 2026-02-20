"""
PDF Extraction Custom Skill — Apache PDFBox via JPype.

Receives base64-encoded file_data from the Azure AI Search pipeline,
extracts text + metadata with PDFBox, optionally chunks the text,
and returns fields ready for index injection.

Usage:
    python app.py                        # Development (Flask built-in server)
    gunicorn -w 1 -b 0.0.0.0:5280 app:app  # Production (single worker — JVM is shared)

Environment variables:
    PDFBOX_VERSION   — PDFBox JAR version to download (default: 3.0.4)
    CHUNK_ENABLED    — Enable chunking (default: true)
    CHUNK_MAX_LENGTH — Max chars per chunk (default: 2000)
    CHUNK_OVERLAP    — Overlap chars between chunks (default: 0)
    PORT             — Server port (default: 5280)
"""

from __future__ import annotations

import base64
import glob
import io
import logging
import os
import platform
import re
import shutil
import sys
import tempfile
import time
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

from flask import Flask, jsonify, request

# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s — %(message)s",
)
logger = logging.getLogger("pdf_extraction_skill")

# ---------------------------------------------------------------------------
# Flask app
# ---------------------------------------------------------------------------
app = Flask(__name__)

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
PDFBOX_VERSION = os.environ.get("PDFBOX_VERSION", "3.0.4")
CHUNK_ENABLED = os.environ.get("CHUNK_ENABLED", "true").lower() in ("1", "true", "yes")
CHUNK_MAX_LENGTH = int(os.environ.get("CHUNK_MAX_LENGTH", "2000"))
CHUNK_OVERLAP = int(os.environ.get("CHUNK_OVERLAP", "0"))
PORT = int(os.environ.get("PORT", "5280"))

# ---------------------------------------------------------------------------
# JVM / PDFBox bootstrap (done once at import time)
# ---------------------------------------------------------------------------

def _find_jvm() -> str | None:
    """Locate the JVM shared library, matching the notebook's discovery logic."""
    import jpype

    # 1. Try JPype's default
    try:
        path = jpype.getDefaultJVMPath()
        if path and os.path.exists(path):
            return path
    except Exception:
        pass

    # 2. Platform-specific glob search
    search_patterns: list[str] = []
    if platform.system() == "Windows":
        search_patterns = [
            os.path.expandvars(r"%LOCALAPPDATA%\jdk-*-jre\bin\server\jvm.dll"),
            os.path.expandvars(r"%LOCALAPPDATA%\jdk-*\bin\server\jvm.dll"),
            r"C:\Program Files\Java\*\bin\server\jvm.dll",
            r"C:\Program Files\Microsoft\jdk-*\bin\server\jvm.dll",
            r"C:\Program Files\Eclipse Adoptium\*\bin\server\jvm.dll",
        ]
    elif platform.system() == "Darwin":
        search_patterns = [
            "/Library/Java/JavaVirtualMachines/*/Contents/Home/lib/server/libjvm.dylib",
            "/opt/homebrew/opt/openjdk*/libexec/openjdk.jdk/Contents/Home/lib/server/libjvm.dylib",
            "/usr/local/opt/openjdk*/libexec/openjdk.jdk/Contents/Home/lib/server/libjvm.dylib",
        ]
    else:
        search_patterns = [
            "/usr/lib/jvm/*/lib/server/libjvm.so",
            "/usr/lib/jvm/*/lib/amd64/server/libjvm.so",
        ]

    for pattern in search_patterns:
        matches = sorted(glob.glob(pattern), reverse=True)
        if matches:
            return matches[0]

    # 3. Derive from JAVA_HOME or `java` on PATH
    java_home = os.environ.get("JAVA_HOME")
    if not java_home:
        java_exe = shutil.which("java")
        if java_exe:
            java_home = os.path.dirname(os.path.dirname(os.path.realpath(java_exe)))

    if java_home:
        candidates = [
            os.path.join(java_home, "lib", "server", "libjvm.dylib"),
            os.path.join(java_home, "lib", "server", "libjvm.so"),
            os.path.join(java_home, "lib", "amd64", "server", "libjvm.so"),
            os.path.join(java_home, "bin", "server", "jvm.dll"),
        ]
        for p in candidates:
            if os.path.exists(p):
                return p

    return None


def _ensure_pdfbox_jar() -> Path:
    """Download the PDFBox standalone JAR if not already cached."""
    jar_dir = Path(__file__).parent / "lib"
    jar_dir.mkdir(exist_ok=True)
    jar_path = jar_dir / f"pdfbox-app-{PDFBOX_VERSION}.jar"

    if jar_path.exists():
        logger.info("PDFBox JAR already cached: %s (%s bytes)", jar_path, jar_path.stat().st_size)
        return jar_path

    url = (
        f"https://repo1.maven.org/maven2/org/apache/pdfbox/pdfbox-app/"
        f"{PDFBOX_VERSION}/pdfbox-app-{PDFBOX_VERSION}.jar"
    )
    logger.info("Downloading PDFBox %s from Maven Central …", PDFBOX_VERSION)
    urllib.request.urlretrieve(url, str(jar_path))
    logger.info("Saved PDFBox JAR (%s bytes)", jar_path.stat().st_size)
    return jar_path


def _start_jvm() -> None:
    """Start the JVM with PDFBox on the classpath (once per process)."""
    import jpype

    if jpype.isJVMStarted():
        logger.info("JVM already running")
        return

    jvm_path = _find_jvm()
    if not jvm_path:
        raise RuntimeError(
            "No JVM found. Install a JDK (https://adoptium.net/) or set JAVA_HOME."
        )

    jar_path = _ensure_pdfbox_jar()
    logger.info("Starting JVM: %s", jvm_path)
    jpype.startJVM(jvm_path, classpath=[str(jar_path.resolve())])
    logger.info("JVM started with PDFBox %s on classpath", PDFBOX_VERSION)


# Start JVM eagerly so the first request doesn't pay the cost.
_start_jvm()

# Import PDFBox Java classes (must happen after JVM start)
import jpype
import jpype.imports
from java.io import ByteArrayInputStream  # type: ignore
from org.apache.pdfbox import Loader  # type: ignore
from org.apache.pdfbox.text import PDFTextStripper  # type: ignore
from org.apache.pdfbox.io import RandomAccessReadBuffer  # type: ignore

logger.info("PDFBox %s ready", PDFBOX_VERSION)


# ---------------------------------------------------------------------------
# PDF extraction helpers
# ---------------------------------------------------------------------------

def extract_pdf(pdf_bytes: bytes) -> dict:
    """
    Extract text and metadata from raw PDF bytes using PDFBox.

    Returns a dict with fields aligned to the simulator's CrackedDocument model:
      content, page_count, word_count, character_count, language,
      title, author, created_date, modified_date, metadata, pages
    """
    t0 = time.perf_counter()

    # Load PDF from bytes via RandomAccessReadBuffer
    java_stream = ByteArrayInputStream(pdf_bytes)
    rar_buffer = RandomAccessReadBuffer(java_stream)
    doc = Loader.loadPDF(rar_buffer)

    try:
        page_count = doc.getNumberOfPages()

        # --- Per-page text extraction ---
        stripper = PDFTextStripper()
        pages: list[dict] = []
        full_text_parts: list[str] = []

        for page_num in range(1, page_count + 1):
            stripper.setStartPage(page_num)
            stripper.setEndPage(page_num)
            page_text = str(stripper.getText(doc))
            pages.append(
                {
                    "page_num": page_num,
                    "text": page_text,
                    "char_count": len(page_text),
                    "word_count": len(page_text.split()),
                }
            )
            full_text_parts.append(page_text)

        full_text = "\n\n".join(full_text_parts)

        # --- Metadata ---
        info = doc.getDocumentInformation()
        metadata: dict[str, object] = {}
        meta_fields = {
            "subject": "Subject",
            "keywords": "Keywords",
            "creator": "Creator",
            "producer": "Producer",
        }
        for py_key, java_key in meta_fields.items():
            val = info.getCustomMetadataValue(java_key)
            if val:
                metadata[py_key] = str(val)

        title = _java_str(info.getCustomMetadataValue("Title"))
        author = _java_str(info.getCustomMetadataValue("Author"))

        # Dates
        created_date = _java_calendar_to_iso(info.getCreationDate())
        modified_date = _java_calendar_to_iso(info.getModificationDate())

        # PDF version
        pdf_version = str(doc.getVersion()) if doc.getVersion() else None
        if pdf_version:
            metadata["pdfVersion"] = pdf_version

    finally:
        doc.close()

    elapsed_ms = (time.perf_counter() - t0) * 1000

    return {
        "content": full_text,
        "page_count": page_count,
        "word_count": len(full_text.split()),
        "character_count": len(full_text),
        "title": title,
        "author": author,
        "created_date": created_date,
        "modified_date": modified_date,
        "language": None,
        "metadata": metadata,
        "pages": pages,
        "extraction_time_ms": round(elapsed_ms, 1),
    }


def _java_str(val) -> str | None:
    """Convert a Java string (or None) to a Python str."""
    if val is None:
        return None
    s = str(val).strip()
    return s if s else None


def _java_calendar_to_iso(cal) -> str | None:
    """Convert a java.util.Calendar to an ISO-8601 string, or None."""
    if cal is None:
        return None
    try:
        millis = cal.getTimeInMillis()
        dt = datetime.fromtimestamp(millis / 1000.0, tz=timezone.utc)
        return dt.isoformat()
    except Exception:
        return str(cal.getTime())


# ---------------------------------------------------------------------------
# Text chunking (mirrors simulator's TextSplitSkill logic)
# ---------------------------------------------------------------------------

def split_by_pages(text: str, max_length: int = 2000, overlap: int = 0) -> list[str]:
    """
    Split text into chunks of up to *max_length* characters, trying to break
    at natural boundaries (paragraph → sentence → word).  Mirrors the C#
    ``TextSplitSkillExecutor.SplitByPages`` implementation.
    """
    chunks: list[str] = []
    position = 0

    while position < len(text):
        length = min(max_length, len(text) - position)

        # Try to find a natural break point within the last 100 chars
        if position + length < len(text):
            search_start = max(0, length - 100)
            chunk = text[position : position + length]

            # Paragraph break
            bp = chunk.rfind("\n\n")
            if bp < search_start:
                # Sentence break
                bp = chunk.rfind(". ")
            if bp < search_start:
                # Word break
                bp = chunk.rfind(" ")
            if bp > search_start:
                length = bp + 1

        chunk_text = text[position : position + length].strip()
        if chunk_text:
            chunks.append(chunk_text)

        new_position = position + length - overlap
        if new_position <= position:
            new_position = position + length  # Prevent infinite loop
        position = new_position

    return chunks


def split_by_sentences(text: str) -> list[str]:
    """Split text into sentences ending on `.`, `!`, or `?`."""
    sentences: list[str] = []
    current: list[str] = []
    for ch in text:
        current.append(ch)
        if ch in ".!?":
            s = "".join(current).strip()
            if s:
                sentences.append(s)
            current = []
    remainder = "".join(current).strip()
    if remainder:
        sentences.append(remainder)
    return sentences


# ---------------------------------------------------------------------------
# Routes
# ---------------------------------------------------------------------------

@app.route("/api/skills/pdf-extraction", methods=["POST"])
def pdf_extraction():
    """
    Custom skill endpoint: extract text and metadata from a base64-encoded PDF.

    **Inputs** (per record):
      - ``file_data``  — ``{"$type": "file", "data": "<base64>"}``
        (the output of the FileDataSkill or ``/document/file_data``)
      - ``documentId``  — (optional) identifier for logging

    **Outputs** (per record):
      - ``content``            — full extracted text
      - ``page_count``         — number of pages
      - ``word_count``         — total words
      - ``character_count``    — total characters
      - ``metadata_title``     — PDF title
      - ``metadata_author``    — PDF author
      - ``metadata_created_date``  — creation date (ISO-8601)
      - ``metadata_modified_date`` — modification date (ISO-8601)
      - ``metadata``           — additional metadata dict
      - ``pages``              — per-page text array
      - ``chunks``             — text chunks (if chunking enabled)
    """
    body = request.get_json(force=True)
    values = body.get("values", [])
    logger.info("pdf-extraction skill received %d records", len(values))

    results: list[dict] = []

    for record in values:
        record_id = record.get("recordId", "")
        data = record.get("data", {})
        output: dict = {"recordId": record_id, "data": {}, "errors": [], "warnings": []}

        try:
            document_id = data.get("documentId", "")

            # Accept file_data as the structured object from FileDataSkill
            file_data = data.get("file_data")
            if not file_data or not isinstance(file_data, dict):
                output["errors"].append(
                    "Missing required input 'file_data' (expected {\"$type\": \"file\", \"data\": \"<base64>\"})"
                )
                results.append(output)
                continue

            b64_data = file_data.get("data", "")
            if not b64_data:
                output["errors"].append(
                    "file_data.data is empty — no base64 content provided."
                )
                results.append(output)
                continue

            # Decode base64 → raw PDF bytes
            try:
                pdf_bytes = base64.b64decode(b64_data)
            except Exception as exc:
                output["errors"].append(f"Invalid base64 in file_data.data: {exc}")
                results.append(output)
                continue

            logger.info(
                "Extracting PDF for recordId=%s documentId=%s (%d bytes)",
                record_id, document_id, len(pdf_bytes),
            )

            # --- PDFBox extraction ---
            extracted = extract_pdf(pdf_bytes)

            # --- Build output compatible with index field mappings ---
            out_data: dict = {
                "content": extracted["content"],
                "page_count": extracted["page_count"],
                "word_count": extracted["word_count"],
                "character_count": extracted["character_count"],
                "metadata_title": extracted["title"],
                "metadata_author": extracted["author"],
                "metadata_created_date": extracted["created_date"],
                "metadata_modified_date": extracted["modified_date"],
                "metadata": extracted["metadata"],
                "pages": extracted["pages"],
                "extraction_time_ms": extracted["extraction_time_ms"],
            }

            # --- Chunking ---
            if CHUNK_ENABLED:
                chunks = split_by_pages(
                    extracted["content"],
                    max_length=CHUNK_MAX_LENGTH,
                    overlap=CHUNK_OVERLAP,
                )
                out_data["chunks"] = chunks
                out_data["chunk_count"] = len(chunks)

            output["data"] = out_data

            logger.info(
                "recordId=%s: %d pages, %d words, %d chars, %d chunks (%.1f ms)",
                record_id,
                extracted["page_count"],
                extracted["word_count"],
                extracted["character_count"],
                len(out_data.get("chunks", [])),
                extracted["extraction_time_ms"],
            )

        except Exception as exc:
            logger.exception("Error processing recordId=%s", record_id)
            output["errors"].append(f"PDF extraction failed: {exc}")

        results.append(output)

    return jsonify({"values": results})


@app.route("/api/skills/health", methods=["GET"])
def health():
    """Health check."""
    return jsonify(
        {
            "status": "healthy",
            "pdfbox_version": PDFBOX_VERSION,
            "jvm_running": jpype.isJVMStarted(),
            "chunking_enabled": CHUNK_ENABLED,
            "chunk_max_length": CHUNK_MAX_LENGTH,
            "chunk_overlap": CHUNK_OVERLAP,
            "timestamp": datetime.now(timezone.utc).isoformat(),
        }
    )


# ---------------------------------------------------------------------------
# Entrypoint
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    print("=" * 50)
    print("  PDF Extraction Skill (PDFBox + JPype)")
    print("=" * 50)
    print()
    print("Available endpoints:")
    print(f"  POST /api/skills/pdf-extraction  — Extract text & metadata from base64 PDF")
    print(f"  GET  /api/skills/health          — Health check")
    print()
    print("Configuration:")
    print(f"  PDFBox version : {PDFBOX_VERSION}")
    print(f"  Chunking       : {'enabled' if CHUNK_ENABLED else 'disabled'}")
    print(f"  Chunk max len  : {CHUNK_MAX_LENGTH}")
    print(f"  Chunk overlap  : {CHUNK_OVERLAP}")
    print(f"  Port           : {PORT}")
    print()

    app.run(host="0.0.0.0", port=PORT, debug=False)
