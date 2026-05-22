from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCANNER = ROOT / "PhotoAIApp.Core" / "PhotoAiScanner.cs"
OPTIONS = ROOT / "PhotoAIApp.Core" / "PhotoAiScanOptions.cs"
MAIN_FORM = ROOT / "PhotoAIApp.Gui" / "MainForm.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_add_tags_never_replaces_sentence_description():
    scanner = read(SCANNER)

    assert "public static string BuildImmichXmp(PhotoAnalysis analysis, bool addTags = false)" in scanner
    assert "string description = analysis.Description;" in scanner
    assert "string.Join(\", \", analysis.Tags)" not in scanner
    assert "tagsInDescription" not in scanner


def test_add_tags_controls_keyword_metadata_fields_only():
    scanner = read(SCANNER)

    assert "if (addTags && tags.Length > 0)" in scanner
    assert "new XElement(dc + \"subject\"" in scanner
    assert "new XElement(digiKam + \"TagsList\"" in scanner
    assert "new XElement(lr + \"HierarchicalSubject\"" in scanner


def test_gui_option_is_add_tags_not_tags_in_description():
    options = read(OPTIONS)
    main_form = read(MAIN_FORM)

    program = read(ROOT / "PhotoAIApp" / "Program.cs")

    assert "public bool AddTags { get; init; }" in options
    assert "TagsInDescription" not in options
    assert "AddTags = _addTagsCheckBox.Checked" in main_form
    assert "TagsInDescription" not in main_form
    assert "--add-tags" in program
    assert "--tags-in-description" not in program
