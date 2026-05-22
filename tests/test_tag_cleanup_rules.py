from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCANNER = ROOT / "PhotoAIApp.Core" / "PhotoAiScanner.cs"


def scanner_source() -> str:
    return SCANNER.read_text(encoding="utf-8")


def test_tag_cleanup_normalizes_known_variants_and_typos():
    scanner = scanner_source()

    assert "TagAliases()" in scanner
    assert '"outdoors", "outdoor"' in scanner
    assert '"horses", "horse"' in scanner
    assert '"automobiles", "automotive"' in scanner
    assert '"christma", "christmas"' in scanner


def test_tag_cleanup_filters_inferred_mood_and_weak_tags():
    scanner = scanner_source()

    assert "NoisyInferredTags()" in scanner
    for tag in ["relaxation", "affection", "affectionate", "celebration", "lifestyle", "wall"]:
        assert f'"{tag}"' in scanner


def test_tag_cleanup_collapses_redundant_human_tags_after_enrichment():
    scanner = scanner_source()

    assert "CollapseRedundantHumanTags" in scanner
    assert "SpecificHumanTags()" in scanner
    assert 'tag != "person" && tag != "people"' in scanner
    assert 'tag != "people"' in scanner
    assert 'tagList.Contains("person"' in scanner


def test_tag_cleanup_runs_after_description_and_caption_enrichment():
    scanner = scanner_source()

    assert "return PostProcessTags(orderedTags).Take(10).ToArray();" in scanner
    assert "private static IEnumerable<string> PostProcessTags" in scanner
