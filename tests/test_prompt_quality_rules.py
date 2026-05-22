from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCANNER = ROOT / "PhotoAIApp.Core" / "PhotoAiScanner.cs"


def test_prompt_discourages_unverified_mechanical_state_and_inference():
    scanner = SCANNER.read_text(encoding="utf-8")

    assert "Describe only what is visibly supported by the image" in scanner
    assert "Do not infer mechanical state" in scanner
    assert "canopy is up/down" in scanner
    assert "unless it is visually unambiguous" in scanner


def test_prompt_limits_tags_to_specific_searchable_subjects():
    scanner = SCANNER.read_text(encoding="utf-8")

    assert "Prefer 3 to 7 tags" in scanner
    assert "concrete, visible subjects" in scanner
    assert "Avoid mood/activity tags" in scanner
    assert "relaxation" in scanner
    assert "Do not include a tag just because it is true" in scanner


def test_prompt_requests_conservative_uncertainty_language():
    scanner = SCANNER.read_text(encoding="utf-8")

    assert "Use conservative wording" in scanner
    assert "Avoid exact counts unless clear" in scanner
    assert "Do not identify relationships" in scanner
