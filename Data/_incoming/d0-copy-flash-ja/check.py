import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent
source_root = root.parent / "d0-copy-terra"
names = [
    "affixes.json",
    "region-themes.json",
    "quest-templates.json",
    "boss-epithets.json",
    "strings.json",
]


def extract_placeholders(text: str) -> set[str]:
    return set(re.findall(r"\{([^{}]+)\}", text))


def extract_numbers(text: str) -> list[str]:
    return re.findall(r"\d+", text)


def compare_structures(src, tgt, path=""):
    if isinstance(src, dict):
        if set(src.keys()) == {"zh-TW", "en"}:
            assert isinstance(tgt, dict), f"Expected dict at {path}, got {type(tgt)}"
            assert set(tgt.keys()) == {"ja"}, (
                f"Expected only 'ja' key at {path}, got {set(tgt.keys())}"
            )
            assert isinstance(tgt["ja"], str) and tgt["ja"].strip(), (
                f"Empty ja string at {path}"
            )

            # Check placeholders match
            src_zh_placeholders = extract_placeholders(src["zh-TW"])
            src_en_placeholders = extract_placeholders(src["en"])
            tgt_placeholders = extract_placeholders(tgt["ja"])
            assert (
                tgt_placeholders == src_zh_placeholders == src_en_placeholders
            ), (
                f"Placeholder mismatch at {path}: src_zh={src_zh_placeholders}, "
                f"src_en={src_en_placeholders}, tgt={tgt_placeholders}"
            )

            # Check arabic numbers in zh-TW appear in ja
            src_nums = extract_numbers(src["zh-TW"])
            for num in src_nums:
                assert re.search(rf"(?<!\d){num}(?!\d)", tgt["ja"]), (
                    f"Number {num} from zh-TW missing in ja at {path}: '{tgt['ja']}'"
                )
            return 1
        else:
            assert isinstance(tgt, dict), f"Expected dict at {path}, got {type(tgt)}"
            assert list(src.keys()) == list(tgt.keys()), (
                f"Key mismatch or order difference at {path}: "
                f"{list(src.keys())} vs {list(tgt.keys())}"
            )
            count = 0
            for k in src:
                count += compare_structures(
                    src[k], tgt[k], f"{path}.{k}" if path else k
                )
            return count
    elif isinstance(src, list):
        assert isinstance(tgt, list), f"Expected list at {path}, got {type(tgt)}"
        assert len(src) == len(tgt), (
            f"Length mismatch at {path}: {len(src)} vs {len(tgt)}"
        )
        count = 0
        for idx, (s_item, t_item) in enumerate(zip(src, tgt)):
            count += compare_structures(s_item, t_item, f"{path}[{idx}]")
        return count
    else:
        assert src == tgt, f"Value mismatch at {path}: {src} != {tgt}"
        return 0


def main():
    total_fields = 0
    for name in names:
        src_path = source_root / name
        tgt_path = root / name
        assert src_path.is_file(), f"Missing source file: {src_path}"
        assert tgt_path.is_file(), f"Missing target file: {tgt_path}"

        src_data = json.loads(src_path.read_text(encoding="utf-8"))
        tgt_data = json.loads(tgt_path.read_text(encoding="utf-8"))
        field_count = compare_structures(src_data, tgt_data, name)
        print(f"PASS: {name} ({field_count} localized fields verified)")
        total_fields += field_count

    print(f"ALL PASS: 5 files, {total_fields} localized fields match perfectly.")


if __name__ == "__main__":
    main()
