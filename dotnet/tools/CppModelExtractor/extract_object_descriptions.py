"""Extract src/identification.cpp's objectDescription() switch into the port.

M15: the '/' (identify a symbol) command's table -- 84 cases, one per
printing ASCII character -- is generated from the original's switch rather
than retyped, so the text can't drift from a slip. The rows are written
between `command switch {` and `};` of ObjectDescription() in
dotnet/src/Umoria.Core/Ui/GameUi.Look.cs (everything else in that file is
hand-ported).

Commented-out cases (`// case 'I':`) are skipped, as the compiler skips
them. A return that isn't a string literal must be listed in EXPRESSIONS
with its C# equivalent, or the extraction fails: an unknown one would
otherwise be dropped and its case fall through to the next -- which is how
'@' (`return py.misc.name;`) first came out wrong, caught by the M15 game
loop trace.

Usage (from the repository root):
    python dotnet/tools/CppModelExtractor/extract_object_descriptions.py
"""
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SOURCE = os.path.join(ROOT, "src", "identification.cpp")
TARGET = os.path.join(ROOT, "dotnet", "src", "Umoria.Core", "Ui", "GameUi.Look.cs")

EXPRESSIONS = {
    "py.misc.name": "W.Py.Misc.Name",
}


def extract(source_text):
    start = source_text.index("static const char *objectDescription(char command) {")
    end = source_text.index("\nvoid identifyGameObject()", start)

    pairs = []
    pending = []
    default = None

    for line in source_text[start:end].splitlines():
        s = line.strip()
        if s.startswith("//"):
            continue

        m = re.match(r"case '((?:\\.|[^'])+)':$", s)
        if m:
            pending.append(m.group(1))
            continue
        if s == "default:":
            pending.append(None)
            continue

        m = re.match(r'return "((?:\\.|[^"])*)";$', s)
        if m:
            value = '"' + m.group(1) + '"'
        else:
            m = re.match(r"return (.+);$", s)
            if not m:
                continue
            if m.group(1) not in EXPRESSIONS:
                sys.exit(f"unknown return expression in objectDescription(): {m.group(1)}")
            value = EXPRESSIONS[m.group(1)]

        for key in pending:
            if key is None:
                default = value
            else:
                pairs.append((key, value))
        pending = []

    if pending or default is None:
        sys.exit("objectDescription(): unterminated case list or no default")

    # C and C# share these character/string escapes (\', \\, \")
    rows = [f"            '{key}' => {value}," for key, value in pairs]
    rows.append(f"            _ => {default},")
    return rows


def main():
    with open(SOURCE, encoding="utf-8") as f:
        rows = extract(f.read())

    with open(TARGET, "rb") as f:
        text = f.read().decode("utf-8")

    head = "        command switch\n        {\n"
    start = text.index(head, text.index("ObjectDescription(char command)")) + len(head)
    end = text.index("        };\n", start)
    text = text[:start] + "\n".join(rows) + "\n" + text[end:]

    with open(TARGET, "wb") as f:
        f.write(text.encode("utf-8"))

    print(f"{len(rows) - 1} cases written to {os.path.relpath(TARGET, ROOT)}")


if __name__ == "__main__":
    main()
