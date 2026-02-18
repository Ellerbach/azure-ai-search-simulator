"""
Python wrapper for the DocumentCrackingTool CLI.

Provides a simple interface to call the simulator's C# document crackers
(PdfCracker, PlainTextCracker, HtmlCracker, JsonCracker, CsvCracker,
ExcelCracker, WordDocCracker) from Python and get structured results.

Usage:
    from document_cracking import DocumentCracker

    cracker = DocumentCracker()          # auto-builds if needed
    result = cracker.crack("file.pdf")   # runs all crackers
    result = cracker.crack("file.pdf", crackers=["PdfCracker"])
    crackers = cracker.list_crackers()   # list available crackers
"""

import json
import subprocess
import os
from pathlib import Path
from typing import Optional


class DocumentCracker:
    """Wrapper around the .NET DocumentCrackingTool CLI."""

    TOOL_PROJECT = "tools/DocumentCrackingTool/DocumentCrackingTool.csproj"

    def __init__(self, repo_root: Optional[str] = None, auto_build: bool = True):
        """
        Initialize the DocumentCracker wrapper.

        Args:
            repo_root: Path to the AzureAISimulator repository root.
                       Auto-detected if not provided.
            auto_build: If True, build the tool on first use if binary not found.
        """
        if repo_root:
            self.repo_root = Path(repo_root)
        else:
            # Try to find repo root relative to this file
            self.repo_root = self._find_repo_root()

        self.project_path = self.repo_root / self.TOOL_PROJECT
        self.auto_build = auto_build
        self._built = False

        if not self.project_path.exists():
            raise FileNotFoundError(
                f"DocumentCrackingTool project not found at {self.project_path}"
            )

    def _find_repo_root(self) -> Path:
        """Find the repository root by looking for the solution file."""
        # Start from this file's location
        current = Path(__file__).resolve().parent
        for _ in range(10):  # max 10 levels up
            if (current / "AzureAISearchSimulator.sln").exists():
                return current
            current = current.parent
        raise FileNotFoundError(
            "Could not find AzureAISearchSimulator.sln. "
            "Please specify repo_root explicitly."
        )

    def _ensure_built(self):
        """Build the tool if needed."""
        if self._built:
            return

        if self.auto_build:
            print("🔨 Building DocumentCrackingTool...")
            result = subprocess.run(
                [
                    "dotnet", "build",
                    str(self.project_path),
                    "-c", "Release",
                    "--nologo",
                    "-v", "q",
                ],
                capture_output=True,
                text=True,
                cwd=str(self.repo_root),
            )
            if result.returncode != 0:
                raise RuntimeError(
                    f"Failed to build DocumentCrackingTool:\n{result.stderr}"
                )
            print("✅ Build successful")

        self._built = True

    def _run_tool(self, args: list[str]) -> dict:
        """Run the DocumentCrackingTool with given arguments and parse JSON output."""
        self._ensure_built()

        cmd = [
            "dotnet", "run",
            "--project", str(self.project_path),
            "-c", "Release",
            "--no-build",
            "--",
        ] + args

        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            cwd=str(self.repo_root),
        )

        if result.returncode != 0:
            raise RuntimeError(
                f"DocumentCrackingTool failed (exit {result.returncode}):\n"
                f"stderr: {result.stderr}\n"
                f"stdout: {result.stdout}"
            )

        try:
            return json.loads(result.stdout)
        except json.JSONDecodeError as e:
            raise RuntimeError(
                f"Failed to parse tool output as JSON: {e}\n"
                f"Output: {result.stdout[:500]}"
            )

    def crack(
        self,
        file_path: str,
        crackers: Optional[list[str]] = None,
        content_preview: Optional[int] = None,
    ) -> dict:
        """
        Run document crackers on a file.

        Args:
            file_path: Path to the document file.
            crackers: Optional list of cracker names to run (default: all).
                      e.g. ["PdfCracker", "PlainTextCracker"]
            content_preview: Max chars of content to include.
                             None = full content, 0 = no content.

        Returns:
            dict with file info and cracker results. Structure:
            {
                "file": "name.pdf",
                "fileSize": 12345,
                "extension": ".pdf",
                "detectedContentType": "application/pdf",
                "crackers": [
                    {
                        "crackerName": "PdfCracker",
                        "canHandle": true,
                        "success": true,
                        "content": "...",
                        "characterCount": 1234,
                        "wordCount": 200,
                        "pageCount": 5,
                        "title": "...",
                        "author": "...",
                        "metadata": {...},
                        ...
                    },
                    ...
                ]
            }
        """
        file_path = str(Path(file_path).resolve())
        args = [file_path]

        if crackers:
            args.extend(["--crackers", ",".join(crackers)])

        if content_preview is not None:
            args.extend(["--content-preview", str(content_preview)])

        return self._run_tool(args)

    def crack_multiple(
        self,
        file_paths: list[str],
        crackers: Optional[list[str]] = None,
        content_preview: Optional[int] = None,
    ) -> list[dict]:
        """
        Run document crackers on multiple files.

        Args:
            file_paths: List of file paths.
            crackers: Optional cracker filter.
            content_preview: Max content chars.

        Returns:
            List of result dicts (one per file).
        """
        results = []
        for fp in file_paths:
            try:
                result = self.crack(fp, crackers=crackers, content_preview=content_preview)
                results.append(result)
            except Exception as e:
                results.append({
                    "file": Path(fp).name,
                    "filePath": str(Path(fp).resolve()),
                    "error": str(e),
                })
        return results

    def list_crackers(self) -> list[dict]:
        """
        List all available document crackers.

        Returns:
            List of dicts with cracker info:
            [
                {
                    "name": "PdfCracker",
                    "supportedContentTypes": ["application/pdf"],
                    "supportedExtensions": [".pdf"]
                },
                ...
            ]
        """
        data = self._run_tool(["--list"])
        return data.get("crackers", [])

    def get_handling_crackers(self, result: dict) -> list[dict]:
        """
        From a crack() result, return only the crackers that can handle the file.
        """
        return [c for c in result.get("crackers", []) if c.get("canHandle")]

    def get_successful_crackers(self, result: dict) -> list[dict]:
        """
        From a crack() result, return only crackers that successfully extracted content.
        """
        return [
            c for c in result.get("crackers", [])
            if c.get("canHandle") and c.get("success")
        ]
