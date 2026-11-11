import importlib.util
import json
import os
import tempfile
from pathlib import Path

import pytest
import _pytest.tmpdir

def _safe_getbasetemp(self):
    if self._basetemp is not None:
        return self._basetemp
    temp_dir = (Path(tempfile.gettempdir()) / "pytest-demo-merge").resolve()
    temp_dir.mkdir(parents=True, exist_ok=True)
    self._basetemp = temp_dir
    return temp_dir

_pytest.tmpdir.TempPathFactory.getbasetemp = _safe_getbasetemp


spec = importlib.util.spec_from_file_location("demo_merge", Path(__file__).parents[1] / "demo-data" / "merge.py")
merge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(merge)


@pytest.fixture
def inputs(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(merge, "COPY_OUT", tmp_path / "Copy")
    monkeypatch.setattr(merge, "NAME_SOURCES", ["names"])
    monkeypatch.setattr(merge, "BATCHES", ["batch"])
    monkeypatch.setattr(merge, "ENV_MATERIALS_BATCH", "env")
    root = tmp_path / "Data"
    incoming = root / "_incoming" / "d0-copy-terra"
    incoming.mkdir(parents=True)
    for name in merge.D0_COPY:
        (incoming / f"{name}.json").write_text('{}', encoding="utf-8")
    (incoming / "strings.json").write_text('{"base":{"en":"base"}}', encoding="utf-8")
    (incoming / "demo-ui-supplement.json").write_text('{"new":{"en":"new"}}', encoding="utf-8")
    ja = root / "_incoming" / "d0-copy-flash-ja"
    ja.mkdir()
    for name in merge.D0_COPY:
        (ja / f"{name}.json").write_text('{}', encoding="utf-8")
    (ja / "strings.json").write_text('{"base":{"ja":"base-ja"}}', encoding="utf-8")
    (ja / "demo-ui-supplement.json").write_text('{"new":{"ja":"new-ja"}}', encoding="utf-8")
    (root / "balance.json").write_text('{"x":1}', encoding="utf-8")
    names = root / "_incoming" / "names"
    names.mkdir()
    (names / "copy-terra.json").write_text('{"creatures":[{"id":"a","name_zh":"甲","name_en":"A"}]}', encoding="utf-8")
    translated_names = root / "_incoming" / "d0-names-flash-ja"
    translated_names.mkdir()
    (translated_names / "names-ja.json").write_text(
        json.dumps({"entries": [{"id": "a", "zh": "\u7532", "en": "A", "ja": "\u30a2"}]}, ensure_ascii=False), encoding="utf-8")
    batch = root / "_incoming" / "batch" / "moves"
    batch.mkdir(parents=True)
    (batch / "one.json").write_text('{"id":"one"}', encoding="utf-8")
    env = root / "_incoming" / "env" / "env-materials"
    env.mkdir(parents=True)
    (env / "env-materials.json").write_text('{"substances":[]}', encoding="utf-8")
    return root


def test_conflict_preserves_all_outputs(inputs):
    merge.COPY_OUT.mkdir()
    old = merge.COPY_OUT / "strings.json"
    old.write_text('{"old_reviewed_key":{}}', encoding="utf-8")
    demo = inputs / "_demo"
    demo.mkdir()
    (demo / "old").write_text('old')
    supplement = inputs / "_incoming" / "d0-copy-terra" / "demo-ui-supplement.json"
    supplement.write_text('{"base":{"en":"override"}}', encoding="utf-8")
    with pytest.raises(SystemExit, match="overrides"):
        merge.main()
    assert old.read_text() == '{"old_reviewed_key":{}}'
    assert (demo / "old").read_text() == 'old'


def test_nested_duplicate_rejected_before_writes(inputs):
    supplement = inputs / "_incoming" / "d0-copy-terra" / "demo-ui-supplement.json"
    supplement.write_text('{"new":{"en":"first","en":"second"}}', encoding="utf-8")
    with pytest.raises(ValueError, match=r'demo-ui-supplement.json.*duplicate key: en'):
        merge.stage_copy(inputs)
    assert not merge.COPY_OUT.exists()


def test_normal_output(inputs):
    assert merge.main() == 0
    assert json.loads((merge.COPY_OUT / 'strings.json').read_text(encoding='utf-8')) == {
        'base': {'en': 'base', 'ja': 'base-ja'}, 'new': {'en': 'new', 'ja': 'new-ja'}}
    assert json.loads((merge.COPY_OUT / 'names.json').read_text(encoding='utf-8')) == {
        'entries': [{'id': 'a', 'zh': '甲', 'en': 'A', 'ja': '\u30a2'}]}
    assert (merge.COPY_OUT / 'balance.json').read_bytes() == b'{"x":1}'
    assert (inputs / '_demo' / 'moves' / 'batch--one.json').read_bytes() == b'{"id":"one"}'


def test_ja_collision_fails_before_writes(inputs):
    path = inputs / "_incoming" / "d0-copy-terra" / "strings.json"
    path.write_text('{"base":{"en":"base","ja":"existing"}}', encoding="utf-8")
    with pytest.raises(ValueError, match="duplicate translation key"):
        merge.main()
    assert not merge.COPY_OUT.exists()
    assert not (inputs / "_demo").exists()


def test_ja_cannot_repeat_an_existing_language(inputs):
    path = inputs / "_incoming" / "d0-copy-flash-ja" / "strings.json"
    path.write_text('{"base":{"en":"base","ja":"translation"}}', encoding="utf-8")
    with pytest.raises(ValueError, match="duplicate translation key: en"):
        merge.main()
    assert not merge.COPY_OUT.exists()


def test_ja_duplicate_json_key_fails_before_writes(inputs):
    path = inputs / "_incoming" / "d0-copy-flash-ja" / "strings.json"
    path.write_text('{"base":{"ja":"a","ja":"b"}}', encoding="utf-8")
    with pytest.raises(ValueError, match="duplicate key: ja"):
        merge.main()
    assert not merge.COPY_OUT.exists()


def test_ja_nested_entries_merge_and_identity_mismatch_preserves_outputs(inputs):
    base = inputs / "_incoming" / "d0-copy-terra" / "affixes.json"
    ja = inputs / "_incoming" / "d0-copy-flash-ja" / "affixes.json"
    base.write_text('[{"id":"a","name":{"en":"Name"}}]', encoding="utf-8")
    ja.write_text('[{"id":"a","name":{"ja":"Japanese"}}]', encoding="utf-8")
    merge.stage_copy(inputs)
    assert json.loads((merge.COPY_OUT / "affixes.json").read_text(encoding="utf-8")) == [
        {"id": "a", "name": {"en": "Name", "ja": "Japanese"}}]
    old = (merge.COPY_OUT / "affixes.json").read_bytes()
    ja.write_text('[{"id":"different","name":{"ja":"Japanese"}}]', encoding="utf-8")
    with pytest.raises(ValueError, match="identity mismatch"):
        merge.stage_copy(inputs)
    assert (merge.COPY_OUT / "affixes.json").read_bytes() == old


@pytest.mark.parametrize("entries", [
    [],
    [{"id": "a", "zh": "\u7532", "en": "A", "ja": "\u30a2"}, {"id": "extra", "zh": "\u4e59", "en": "B", "ja": "\u30a4"}],
    [{"id": "a", "zh": "\u7532", "en": "A", "ja": "\u30a2"}, {"id": "a", "zh": "\u7532", "en": "A", "ja": "\u30a2"}],
    [{"id": "a", "zh": "wrong", "en": "A", "ja": "\u30a2"}],
    [{"id": "a", "zh": "\u7532", "en": "wrong", "ja": "\u30a2"}],
    [{"id": "a", "zh": "\u7532", "en": "A"}],
    [{"id": "a", "zh": "\u7532", "en": "A", "ja": ""}],
    [{"id": "a", "zh": "\u7532", "en": "A", "ja": "  "}],
    [{"id": "a", "zh": "\u7532", "en": "A", "ja": None}],
])
def test_invalid_ja_names_preserve_all_outputs(inputs, entries):
    path = inputs / "_incoming" / "d0-names-flash-ja" / "names-ja.json"
    path.write_text(json.dumps({"entries": entries}, ensure_ascii=False), encoding="utf-8")
    merge.COPY_OUT.mkdir()
    old = merge.COPY_OUT / "names.json"
    old.write_bytes(b"old")
    demo = inputs / "_demo"
    demo.mkdir()
    (demo / "old").write_bytes(b"old")
    with pytest.raises(ValueError):
        merge.main()
    assert old.read_bytes() == b"old"
    assert (demo / "old").read_bytes() == b"old"


def test_stage_copy_env_materials_byte_identical(inputs):
    src = inputs / "_incoming" / "env" / "env-materials" / "env-materials.json"
    raw = b'{\n  "substances": [\n    {"id": "metal"}\n  ]\n}\n'
    src.write_bytes(raw)
    merge.stage_copy(inputs)
    assert (merge.COPY_OUT / "env-materials.json").read_bytes() == raw


@pytest.mark.parametrize("file_count", [0, 2])
def test_env_materials_file_count_validation(inputs, file_count):
    env_dir = inputs / "_incoming" / "env" / "env-materials"
    for f in env_dir.glob("*.json"):
        f.unlink()
    for i in range(file_count):
        (env_dir / f"env_{i}.json").write_text('{"substances":[]}', encoding="utf-8")
    with pytest.raises(ValueError, match="exactly one"):
        merge.stage_copy(inputs)


def test_main_stages_env_materials_kind(inputs, monkeypatch):
    batch_dir = inputs / "_incoming" / "env-batch" / "env-materials"
    batch_dir.mkdir(parents=True)
    raw = b'{"substances":[{"id":"stone"}]}'
    (batch_dir / "env-materials.json").write_bytes(raw)
    monkeypatch.setattr(merge, "BATCHES", ["batch", "env-batch"])
    assert merge.main() == 0
    staged = inputs / "_demo" / "env-materials" / "env-batch--env-materials.json"
    assert staged.read_bytes() == raw
