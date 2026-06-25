#!/usr/bin/env python3
"""Standalone rule validator for EngineIQ standards YAML against unified diff fixtures."""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

import yaml
from rich.console import Console
from rich.table import Table

console = Console()

LAYER_FOLDERS: dict[str, tuple[str, ...]] = {
    "Domain": ("Domain", "Core"),
    "Application": ("Application", "UseCases"),
    "Infrastructure": ("Infrastructure", "Persistence"),
    "API": ("API", "Controllers", "WebAPI"),
}

SECRET_PATTERNS = [
    re.compile(r"(?i)connection\s*string\s*=\s*[\"'][^\"']+[\"']"),
    re.compile(r"(?i)(password|api[_-]?key|secret|token)\s*=\s*[\"'][^\"']{8,}[\"']"),
    re.compile(r"(?i)Password=[^\"';\\s]{8,}"),
    re.compile(r"(?i)Host=[^\"';]+;Password=[^\"';]+"),
    re.compile(r"sk-ant-[A-Za-z0-9_-]{10,}"),
    re.compile(r"-----BEGIN (RSA |EC )?PRIVATE KEY-----"),
]

PERF_PATTERNS = [
    re.compile(r"\.Result\b"),
    re.compile(r"\.Wait\(\)"),
    re.compile(r"GetAwaiter\(\)\.GetResult\(\)"),
]

CALCULATION_PATTERN = re.compile(
    r"(?<![\w.])(?:var|let|const)?\s*\w+\s*=\s*[^;]*[+\-*/%][^;]*;|"
    r"\b(?:total|sum|amount|price|cost)\s*=\s*[^;]+[+\-*/%][^;]+",
    re.IGNORECASE,
)


@dataclass
class DiffHunk:
    path: str
    added_lines: list[tuple[int, str]] = field(default_factory=list)


@dataclass
class Finding:
    rule_id: str
    file: str
    line: int
    message: str


def path_layer(path: str, layer_name: str) -> bool:
    normalized = path.replace("\\", "/")
    for folder in LAYER_FOLDERS.get(layer_name, ()):
        if (
            f"/{folder}/" in normalized
            or f".{folder}/" in normalized
            or normalized.startswith(f"{folder}/")
        ):
            return True
    return False


def parse_unified_diff(text: str) -> list[DiffHunk]:
    hunks: list[DiffHunk] = []
    current: DiffHunk | None = None
    line_no = 0

    for raw in text.splitlines():
        if raw.startswith("+++ "):
            path = raw[4:].strip()
            if path.startswith("b/"):
                path = path[2:]
            current = DiffHunk(path=path)
            hunks.append(current)
            continue

        if current is None:
            continue

        if raw.startswith("@@"):
            match = re.search(r"\+(\d+)", raw)
            line_no = int(match.group(1)) if match else 0
            continue

        if raw.startswith("+") and not raw.startswith("+++"):
            current.added_lines.append((line_no, raw[1:]))
            line_no += 1
        elif raw.startswith(" ") or raw.startswith("-"):
            if not raw.startswith("-"):
                line_no += 1

    return hunks


def load_rules(config_path: Path) -> list[dict]:
    data = yaml.safe_load(config_path.read_text(encoding="utf-8"))
    return list(data.get("rules") or [])


def check_arch_001(hunks: list[DiffHunk]) -> list[Finding]:
    findings: list[Finding] = []
    disallowed = ("Infrastructure", "API", "Persistence", "Controllers", "WebAPI")
    for hunk in hunks:
        if not path_layer(hunk.path, "Domain"):
            continue
        for line_no, line in hunk.added_lines:
            if "using " in line:
                for token in disallowed:
                    if token in line:
                        findings.append(
                            Finding(
                                "ARCH-001",
                                hunk.path,
                                line_no,
                                f"Domain layer must not reference {token}",
                            )
                        )
                        break
            for token in ("Infrastructure.", ".API.", "Persistence."):
                if token in line:
                    findings.append(
                        Finding(
                            "ARCH-001",
                            hunk.path,
                            line_no,
                            f"Domain layer must not reference {token}",
                        )
                    )
                    break
    return findings


def check_arch_002(hunks: list[DiffHunk]) -> list[Finding]:
    findings: list[Finding] = []
    for hunk in hunks:
        if not path_layer(hunk.path, "API"):
            continue
        for line_no, line in hunk.added_lines:
            stripped = line.strip()
            if stripped.startswith("//") or stripped.startswith("*"):
                continue
            if re.search(r"\bif\s*\(", line):
                findings.append(Finding("ARCH-002", hunk.path, line_no, "Business logic (if) in controller"))
            elif re.search(r"\bswitch\s*\(", line):
                findings.append(Finding("ARCH-002", hunk.path, line_no, "Business logic (switch) in controller"))
            elif CALCULATION_PATTERN.search(line):
                findings.append(Finding("ARCH-002", hunk.path, line_no, "Calculation logic in controller"))
    return findings


def check_sec_001(hunks: list[DiffHunk]) -> list[Finding]:
    findings: list[Finding] = []
    for hunk in hunks:
        for line_no, line in hunk.added_lines:
            for pattern in SECRET_PATTERNS:
                if pattern.search(line):
                    findings.append(Finding("SEC-001", hunk.path, line_no, "Possible hardcoded secret"))
                    break
    return findings


def check_perf_001(hunks: list[DiffHunk]) -> list[Finding]:
    findings: list[Finding] = []
    for hunk in hunks:
        for line_no, line in hunk.added_lines:
            for pattern in PERF_PATTERNS:
                if pattern.search(line):
                    findings.append(Finding("PERF-001", hunk.path, line_no, "Blocking async call pattern"))
                    break
    return findings


RULE_CHECKERS = {
    "ARCH-001": check_arch_001,
    "ARCH-002": check_arch_002,
    "SEC-001": check_sec_001,
    "PERF-001": check_perf_001,
}


def run_validator(config_path: Path, fixtures_dir: Path) -> dict:
    rules = load_rules(config_path)
    rule_ids = [r["id"] for r in rules if r.get("id") in RULE_CHECKERS]

    labels_path = fixtures_dir / "labels.json"
    labels = json.loads(labels_path.read_text(encoding="utf-8"))

    per_rule = {
        rid: {"tp": 0, "fn": 0, "fp": 0, "tn": 0} for rid in rule_ids
    }
    all_findings: list[tuple[str, list[Finding]]] = []

    for fixture_name, label in labels.items():
        patch_path = fixtures_dir / fixture_name
        hunks = parse_unified_diff(patch_path.read_text(encoding="utf-8"))
        detected: dict[str, list[Finding]] = {rid: checker(hunks) for rid, checker in RULE_CHECKERS.items()}
        all_findings.append((fixture_name, [f for findings in detected.values() for f in findings]))

        should_find = bool(label.get("should_find"))
        rule_id = label.get("rule_id")
        if not rule_id:
            continue

        fired = len(detected.get(rule_id, [])) > 0
        if should_find:
            if fired:
                per_rule[rule_id]["tp"] += 1
            else:
                per_rule[rule_id]["fn"] += 1
        else:
            if fired:
                per_rule[rule_id]["fp"] += 1
            else:
                per_rule[rule_id]["tn"] += 1

    total_fp = sum(stats["fp"] for stats in per_rule.values())
    total_tn = sum(stats["tn"] for stats in per_rule.values())
    total_negatives = total_fp + total_tn
    overall_fp_rate = (total_fp / total_negatives) if total_negatives else 0.0

    return {
        "per_rule": per_rule,
        "overall_fp_rate": overall_fp_rate,
        "all_findings": all_findings,
        "rule_ids": rule_ids,
    }


def rate(numerator: int, denominator: int) -> float:
    return (numerator / denominator) if denominator else 1.0


def print_report(result: dict) -> None:
    table = Table(title="Rule validator report", show_header=True, header_style="bold")
    table.add_column("Rule")
    table.add_column("TP rate", justify="right")
    table.add_column("FP rate", justify="right")
    table.add_column("TP", justify="right")
    table.add_column("FN", justify="right")
    table.add_column("FP", justify="right")
    table.add_column("TN", justify="right")

    for rule_id in result["rule_ids"]:
        stats = result["per_rule"][rule_id]
        tp_denom = stats["tp"] + stats["fn"]
        fp_denom = stats["fp"] + stats["tn"]
        table.add_row(
            rule_id,
            f"{rate(stats['tp'], tp_denom):.0%}",
            f"{rate(stats['fp'], fp_denom):.0%}",
            str(stats["tp"]),
            str(stats["fn"]),
            str(stats["fp"]),
            str(stats["tn"]),
        )

    console.print(table)
    console.print(
        f"\n[bold]Overall false-positive rate:[/bold] {result['overall_fp_rate']:.1%} "
        f"(threshold < 15%)"
    )

    for fixture_name, findings in result["all_findings"]:
        if findings:
            console.print(f"\n[cyan]{fixture_name}[/cyan]")
            for f in findings:
                console.print(f"  {f.rule_id} {f.file}:{f.line} — {f.message}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate standards rules against diff fixtures.")
    parser.add_argument(
        "--config",
        type=Path,
        default=Path("config/standards-templates/clean-architecture.yaml"),
    )
    parser.add_argument(
        "--fixtures",
        type=Path,
        default=Path(__file__).resolve().parent / "fixtures",
    )
    parser.add_argument("--max-fp-rate", type=float, default=0.15)
    args = parser.parse_args(argv)

    result = run_validator(args.config.resolve(), args.fixtures.resolve())
    print_report(result)

    if result["overall_fp_rate"] >= args.max_fp_rate:
        console.print("[red]FAIL[/red]: overall FP rate at or above threshold.")
        return 1

    console.print("[green]PASS[/green]: overall FP rate below threshold.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
