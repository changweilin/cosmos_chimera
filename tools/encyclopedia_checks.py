"""Shared semantic checks for the game's and Forge's encyclopedia records."""


def check_encyclopedia(records, findings, finding_type):
    subjects = set()
    game_ids = set()
    for record in records:
        entry = record.data

        def error(code, pointer, message):
            findings.append(finding_type("error", code, record.file,
                                         f"/{record.index}/{pointer}", message))

        subject = (entry.get("kind"), entry.get("subject_id"),
                   entry.get("stage"), entry.get("skill_type"))
        if all(value is None or isinstance(value, str) for value in subject):
            if subject in subjects:
                error("duplicate_subject", "subject_id", "duplicate subject, stage and skill type")
            subjects.add(subject)
        game_id = entry.get("game_id")
        kind = entry.get("kind")
        if isinstance(game_id, str) and isinstance(kind, str):
            key = (kind, game_id)
            if key in game_ids:
                error("duplicate_game_mapping", "game_id", "gameplay subject already has an encyclopedia entry")
            game_ids.add(key)

        sources = entry.get("sources")
        source_ids = set()
        if isinstance(sources, list):
            for index, source in enumerate(sources):
                if not isinstance(source, dict) or not isinstance(source.get("id"), str):
                    continue
                source_id = source["id"]
                if source_id in source_ids:
                    error("duplicate_source", f"sources/{index}/id", f"duplicate source '{source_id}'")
                source_ids.add(source_id)

        assets = entry.get("assets")
        if isinstance(assets, dict):
            for role, source_id in assets.items():
                if isinstance(source_id, str) and source_id not in source_ids:
                    error("dangling_source", f"assets/{role}", f"unknown source '{source_id}'")

        details = entry.get("details")
        refs = details.get("value_refs") if isinstance(details, dict) else None
        if isinstance(refs, list):
            for index, ref in enumerate(refs):
                if not isinstance(ref, dict):
                    continue
                source_id = ref.get("source")
                if isinstance(source_id, str) and source_id not in source_ids:
                    error("dangling_source", f"details/value_refs/{index}/source",
                          f"unknown source '{source_id}'")
