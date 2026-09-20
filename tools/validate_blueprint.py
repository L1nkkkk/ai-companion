"""Validate this design package, not an implementation or a live platform."""

import argparse
import json
import re
import struct
import uuid
from pathlib import Path

try:
    from jsonschema import Draft202012Validator, FormatChecker
except ImportError as exc:
    raise SystemExit(
        "Install tools/requirements-validation.txt in a local virtual environment first."
    ) from exc

parser = argparse.ArgumentParser()
parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
parser.add_argument("--report", type=Path)
args = parser.parse_args()
root = args.root.resolve()
docs = root if (root / "ARCHITECTURE.md").exists() else root / "docs/blueprint"
contracts = root / "contracts"
errors = []
checks = {}


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def check(condition, message):
    if not condition:
        errors.append(message)


def walk(value):
    yield value
    if isinstance(value, dict):
        for child in value.values():
            yield from walk(child)
    elif isinstance(value, list):
        for child in value:
            yield from walk(child)


def resolve_local(document, ref):
    current = document
    if not ref.startswith("#/"):
        raise ValueError("Only internal refs are expected here: " + ref)
    for part in ref[2:].split("/"):
        current = current[part.replace("~1", "/").replace("~0", "~")]
    return current


schemas = {d: read(contracts / (d + "-event.schema.json")) for d in ("client", "server")}
format_checker = FormatChecker()
validators = {}
positive_count = 0
for direction, schema in schemas.items():
    Draft202012Validator.check_schema(schema)
    validators[direction] = Draft202012Validator(schema, format_checker=format_checker)
    examples = read(contracts / "examples" / (direction + "-valid.json"))
    event_types = set(schema["properties"]["type"]["enum"])
    check(
        {e["type"] for e in examples} == event_types,
        direction + " examples do not cover every event",
    )
    for example in examples:
        issues = list(validators[direction].iter_errors(example))
        check(
            not issues,
            direction
            + " positive sample rejected: "
            + example.get("type", "?")
            + " "
            + "; ".join(i.message for i in issues),
        )
        positive_count += 1
    for node in walk(schema):
        if isinstance(node, dict) and "$ref" in node:
            try:
                resolve_local(schema, node["$ref"])
            except (KeyError, ValueError) as exc:
                errors.append(str(exc))

negative = read(contracts / "examples/invalid-events.json")
for item in negative:
    check(
        not validators[item["direction"]].is_valid(item["event"]),
        "Negative sample accepted: " + item["reason"],
    )
checks["positive_control_examples"] = positive_count
checks["negative_control_examples"] = len(negative)
checks["client_event_types"] = len(schemas["client"]["properties"]["type"]["enum"])
checks["server_event_types"] = len(schemas["server"]["properties"]["type"]["enum"])

api = read(contracts / "openapi.json")
check(api["openapi"] == "3.1.0", "Unexpected OpenAPI version")
operations = []
operation_names = set()
for path, methods in api["paths"].items():
    for method, operation in methods.items():
        check(method in {"get", "post", "put", "patch", "delete"}, "Unexpected HTTP method")
        check(operation["operationId"] not in operation_names, "Duplicate operationId")
        operation_names.add(operation["operationId"])
        operations.append((method.upper(), path))
        path_params = set(re.findall(r"{([^}]+)}", path))
        declared = {
            p["name"]
            for p in operation.get("parameters", [])
            if p["in"] == "path" and p.get("required")
        }
        check(path_params == declared, "Missing path parameters: " + path)
        check(bool(operation.get("responses")), "Missing responses")
for model_name, schema in api["components"]["schemas"].items():
    Draft202012Validator.check_schema(schema)
for node in walk(api):
    if isinstance(node, dict) and "$ref" in node:
        try:
            resolve_local(api, node["$ref"])
        except (KeyError, ValueError) as exc:
            errors.append(str(exc))
rest_samples = read(contracts / "examples/rest-valid.json")
for name, sample in rest_samples.items():
    wrapper = {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$ref": "#/components/schemas/" + name,
        "components": api["components"],
    }
    issues = list(Draft202012Validator(wrapper, format_checker=format_checker).iter_errors(sample))
    check(not issues, "REST sample rejected: " + name + " " + "; ".join(i.message for i in issues))
checks["rest_operations"] = len(operations)
checks["rest_models"] = len(api["components"]["schemas"])
checks["rest_examples"] = len(rest_samples)

contract_text = (docs / "CONTRACTS.md").read_text(encoding="utf-8")
document_operations = set(re.findall(r"\| (GET|POST|PATCH|DELETE|PUT) (/[^ ]+) \|", contract_text))
check(
    set(operations) == document_operations,
    "REST document and OpenAPI operations differ: " + str(set(operations) ^ document_operations),
)
document_events = set(re.findall(r"^\| ([a-z]+(?:\.[a-z_]+)*) \|", contract_text, flags=re.M))
schema_events = {
    name for schema in schemas.values() for name in schema["properties"]["type"]["enum"]
}
check(
    document_events == schema_events,
    "Event table and schema differ: " + str(document_events ^ schema_events),
)

audio = read(contracts / "examples/audio-header.json")
raw = bytes.fromhex(audio["header_hex"])
check(len(raw) == 64, "Audio header is not 64 bytes")
parts = struct.unpack("<4sBBBBIIII16s16sII", raw)
check(parts[0] == b"AIC1" and parts[1:5] == (1, 1, 1, 0), "Audio magic, version or flags mismatch")
check(
    parts[5] == audio["session_epoch"] and parts[6] == audio["frame_seq"],
    "Audio epoch or sequence mismatch",
)
check(parts[7] == 16000 and parts[8] == 3200, "Audio rate or payload mismatch")
check(str(uuid.UUID(bytes=parts[9])) == audio["stream_id"], "Audio UUID byte order mismatch")
check(parts[10] == bytes(16) and parts[11:] == (0, 0), "Input audio turn or index mismatch")
check(parts[8] == parts[7] // 10 * 2, "Audio 100ms length mismatch")
checks["audio_header_bytes"] = len(raw)

task_data = read(docs / "planning/tasks.json")
tasks = task_data["tasks"]
task_map = {t["id"]: t for t in tasks}
check(len(task_map) == len(tasks), "Duplicate task IDs")
check(
    set(task_map) == {"T" + str(i).zfill(2) for i in range(len(tasks))},
    "Task IDs are not continuous",
)
acceptance_text = (docs / "ACCEPTANCE.md").read_text(encoding="utf-8")
acceptance_ids = set(re.findall(r"^\| (AC\d{2}) \|", acceptance_text, flags=re.M))
reqs = read(docs / "planning/requirements.json")["requirements"]
req_map = {r["id"]: r for r in reqs}
task_markdown = (docs / "TASKS.md").read_text(encoding="utf-8")
for task in tasks:
    check(task["owner_role"] in {"A" + str(i) for i in range(9)}, "Unknown task role")
    check(
        task["status"]
        in {"planned", "ready", "in_progress", "awaiting_external", "review", "done", "changed"},
        "Unknown task status",
    )
    if task["status"] in {"review", "done"}:
        check(
            bool(task.get("report")) and (root / task["report"]).is_file(),
            "Reviewed task needs an evidence report",
        )
    check(
        bool(task["allowed_paths"])
        and bool(task["deliverables"])
        and bool(task["dispatch_prompt"]),
        "Incomplete task card",
    )
    for dep in task["depends_on"]:
        check(
            dep in task_map and dep != task["id"],
            "Invalid dependency: " + task["id"] + " -> " + dep,
        )
    check(set(task["acceptance"]) <= acceptance_ids, "Unknown acceptance ID: " + task["id"])
    check(set(task["requirements"]) <= set(req_map), "Unknown requirement ID")
    check(
        "## " + task["id"] + " " + task["title"] in task_markdown, "Missing task card in Markdown"
    )

visiting, visited, order = set(), set(), []


def visit(task_id):
    if task_id in visiting:
        errors.append("Task cycle includes " + task_id)
        return
    if task_id in visited:
        return
    visiting.add(task_id)
    for dep in task_map[task_id]["depends_on"]:
        if dep in task_map:
            visit(dep)
    visiting.remove(task_id)
    visited.add(task_id)
    order.append(task_id)


for task_id in task_map:
    visit(task_id)
for req in reqs:
    real_tasks = [t["id"] for t in tasks if req["id"] in t["requirements"]]
    check(req["tasks"] == real_tasks, "Requirement task mapping drift: " + req["id"])
    check(
        bool(req["acceptance"]) and set(req["acceptance"]) <= acceptance_ids,
        "Requirement has no valid acceptance",
    )
check(
    set(task_map["T20"]["acceptance"]) == acceptance_ids,
    "R1 release task does not cover all mandatory acceptance",
)
checks["tasks"] = len(tasks)
checks["roles"] = len({t["owner_role"] for t in tasks})
checks["requirements"] = len(reqs)
checks["acceptance_items"] = len(acceptance_ids)
checks["topological_order"] = order

markdown_files = list(docs.rglob("*.md"))
if docs != root:
    markdown_files += list(contracts.rglob("*.md"))
else:
    markdown_files = list(root.rglob("*.md"))
local_link_count = 0
for file in markdown_files:
    text = file.read_text(encoding="utf-8-sig")
    check(text.count("~~~") % 2 == 0, "Unbalanced fenced block: " + str(file))
    for link in re.findall(r"\[[^\]]+\]\(([^)]+)\)", text):
        if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", link):
            continue
        path_part, _, fragment = link.partition("#")
        target = (file.parent / path_part).resolve() if path_part else file
        check(target.exists(), "Broken local link: " + str(file.name) + " -> " + link)
        if fragment and target.exists() and target.suffix == ".md":
            target_text = target.read_text(encoding="utf-8-sig")
            check('id="' + fragment + '"' in target_text, "Missing explicit anchor: " + link)
        local_link_count += 1
checks["markdown_files"] = len(markdown_files)
checks["local_links"] = local_link_count

report = {
    "status": "passed" if not errors else "failed",
    "scope": "Design documents, schema, examples and dependency consistency only",
    "not_validated": [
        "Application implementation",
        "Live provider performance",
        "Mobile background audio",
        "Streaming account permissions",
        "Production deployment",
    ],
    "checks": checks,
    "errors": errors,
}
if args.report:
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
print(json.dumps(report, ensure_ascii=False, indent=2))
raise SystemExit(1 if errors else 0)
