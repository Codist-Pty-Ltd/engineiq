"""Tests for the standalone rule validator."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR = REPO_ROOT / "python" / "rule-validator" / "validate.py"
CONFIG = REPO_ROOT / "config" / "standards-templates" / "clean-architecture.yaml"
FIXTURES = REPO_ROOT / "python" / "rule-validator" / "fixtures"


def run_validator() -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [
            sys.executable,
            str(VALIDATOR),
            "--config",
            str(CONFIG),
            "--fixtures",
            str(FIXTURES),
        ],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        check=False,
    )


def test_validator_exits_zero_on_fixtures() -> None:
    result = run_validator()
    assert result.returncode == 0, result.stdout + result.stderr


def test_overall_fp_rate_below_threshold() -> None:
    import importlib.util

    spec = importlib.util.spec_from_file_location("rule_validator_validate", VALIDATOR)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)

    outcome = module.run_validator(CONFIG, FIXTURES)
    assert outcome["overall_fp_rate"] < 0.15


def test_labels_cover_all_fixtures() -> None:
    labels = json.loads((FIXTURES / "labels.json").read_text(encoding="utf-8"))
    patches = {p.name for p in FIXTURES.glob("*.patch")}
    assert patches == set(labels.keys())


def test_each_rule_has_positive_and_negative_fixture() -> None:
    labels = json.loads((FIXTURES / "labels.json").read_text(encoding="utf-8"))
    by_rule: dict[str, list[bool]] = {}
    for label in labels.values():
        rule_id = label["rule_id"]
        by_rule.setdefault(rule_id, []).append(bool(label["should_find"]))

    for rule_id, flags in by_rule.items():
        assert True in flags, f"missing positive fixture for {rule_id}"
        assert False in flags, f"missing negative fixture for {rule_id}"
