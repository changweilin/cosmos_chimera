import importlib.util
import json
from pathlib import Path

import pytest


spec = importlib.util.spec_from_file_location("demo_merge", Path(__file__).parents[1] / "demo-data" / "merge.py")
merge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(merge)


@pytest.fixture
def inputs(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(merge, "COPY_OUT", tmp_path / "Copy")
    monkeypatch.setattr(merge, "GEO_OUT", tmp_path / "Geo")
    monkeypatch.setattr(merge, "NAME_SOURCES", ["names"])
    monkeypatch.setattr(merge, "BATCHES", ["batch"])
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
    batch = root / "_incoming" / "batch" / "moves"
    batch.mkdir(parents=True)
    (batch / "one.json").write_text('{"id":"one"}', encoding="utf-8")
    geo = root / "geo-templates" / "active"
    geo.mkdir(parents=True)
    (geo / "meta.json").write_text('{}', encoding="utf-8")
    (geo / "height.r16").write_bytes(b'height')
    (geo / "water.u8").write_bytes(b'water')
    return root


def test_geo_preserves_unity_meta_and_removes_retired(inputs):
    merge.GEO_OUT.mkdir()
    for suffix, data in (("meta.json", b'{}'), ("height.bytes", b'height'), ("water.bytes", b'water')):
        (merge.GEO_OUT / f"active.{suffix}").write_bytes(data)
        (merge.GEO_OUT / f"active.{suffix}.meta").write_text('guid: fixed', encoding="utf-8")
        (merge.GEO_OUT / f"retired.{suffix}").write_bytes(b'old')
        (merge.GEO_OUT / f"retired.{suffix}.meta").write_text('guid: old', encoding="utf-8")
    (merge.GEO_OUT / "handmade.txt").write_text('KEEP', encoding="utf-8")
    unchanged = merge.GEO_OUT / "active.height.bytes"
    unchanged.touch()
    before = unchanged.stat().st_mtime_ns
    assert merge.stage_geo(inputs) == 1
    assert unchanged.stat().st_mtime_ns == before
    assert all(p.read_text(encoding="utf-8") == 'guid: fixed' for p in merge.GEO_OUT.glob('*.meta'))
    assert len(list(merge.GEO_OUT.glob('*.meta'))) == 3
    assert not list(merge.GEO_OUT.glob('retired.*'))
    assert (merge.GEO_OUT / "handmade.txt").read_text() == 'KEEP'


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
    assert not merge.GEO_OUT.exists()


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
        'entries': [{'id': 'a', 'zh': '甲', 'en': 'A'}]}
    assert (merge.COPY_OUT / 'balance.json').read_bytes() == b'{"x":1}'
    assert (inputs / '_demo' / 'moves' / 'batch--one.json').read_bytes() == b'{"id":"one"}'
    assert (merge.GEO_OUT / 'active.height.bytes').read_bytes() == b'height'


def test_ja_collision_fails_before_writes(inputs):
    path = inputs / "_incoming" / "d0-copy-terra" / "strings.json"
    path.write_text('{"base":{"en":"base","ja":"existing"}}', encoding="utf-8")
    with pytest.raises(ValueError, match="duplicate translation key"):
        merge.main()
    assert not merge.COPY_OUT.exists()
    assert not merge.GEO_OUT.exists()
    assert not (inputs / "_demo").exists()


def test_ja_cannot_repeat_an_existing_language(inputs):
    path = inputs / "_incoming" / "d0-copy-flash-ja" / "strings.json"
    path.write_text('{"base":{"en":"base","ja":"translation"}}', encoding="utf-8")
    with pytest.raises(ValueError, match="duplicate translation key: en"):
        merge.main()
    assert not merge.COPY_OUT.exists()
    assert not merge.GEO_OUT.exists()


def test_ja_duplicate_json_key_fails_before_writes(inputs):
    path = inputs / "_incoming" / "d0-copy-flash-ja" / "strings.json"
    path.write_text('{"base":{"ja":"a","ja":"b"}}', encoding="utf-8")
    with pytest.raises(ValueError, match="duplicate key: ja"):
        merge.main()
    assert not merge.COPY_OUT.exists()
    assert not merge.GEO_OUT.exists()


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
