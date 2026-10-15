#!/usr/bin/env python3
"""Mechanically extracts C++ static data-table arrays (and the struct shapes
that describe their rows) into C# source, using a real libclang AST walk
rather than text scraping.

Why libclang and not regex: these tables are struct-literal arrays (nested
Dice_t{...}, fixed-size sub-arrays, bitflags, strings, symbolic constants
like TV_FOOD) -- exactly the shapes a line-oriented regex can't reliably
split on commas/braces for. Parsing the real .cpp file with clang gives us
the genuine AST: InitListExpr nesting mirrors the struct/array shape exactly,
so walking it generically (by the field/array structure, not per-table
special-casing) can't silently misalign fields the way regex splitting can.
Literal text (numbers, strings, char literals) is carried through from the
source tokens verbatim wherever possible -- never recomputed -- so this tool
can't introduce a transcription bug the way hand-retyping (by a human or an
LLM) could.

Usage: python extract_data_tables.py <repo-root>
Reads the six data_*.cpp files under <repo-root>/src and writes:
  - dotnet/src/Umoria.Core/Model/Generated/<Struct>.cs   (one per row struct)
  - dotnet/src/Umoria.Core/Data/Generated/<File>Data.cs  (one per source file)
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

import clang.cindex as cindex

CLANG_ARGS = ["-x", "c++", "-std=c++14", "-Dssize_t=long"]

# Row-struct C++ names we regenerate as C# record structs, and what to call
# them in C#. Dice_t is deliberately excluded: dotnet/src/Umoria.Core/Model/
# Generated/Dice.cs already exists (Count, Sides) from the POC and is reused
# as-is wherever a field's type is Dice_t.
STRUCT_RENAME = {
    # Fully qualified: Umoria.Core.Dice (DiceRoller.cs's namespace) collides
    # with this type's bare name, so plain "Dice" is ambiguous (CS0118) from
    # any namespace where Umoria.Core is an enclosing scope.
    "Dice_t": "Umoria.Core.Model.Generated.Dice",
    "Creature_t": "Creature",
    "DungeonObject_t": "DungeonObject",
    "Race_t": "Race",
    "Class_t": "CharacterClass",  # `Class` is a C# keyword
    "Background_t": "Background",
    "Spell_t": "Spell",
    "Owner_t": "StoreOwner",
    "MonsterAttack_t": "MonsterAttack",
}

DATA_FILES = [
    "data_creatures.cpp",
    "data_treasure.cpp",
    "data_player.cpp",
    "data_recall.cpp",
    "data_store_owners.cpp",
    "data_stores.cpp",
]

# extern const globals whose definition (with its literal value) lives in a
# *different* translation unit than the data file referencing them -- e.g.
# config::spells::SPELL_TYPE_MAGE is declared `extern const uint8_t` in
# config.h and only defined, with its value, in config.cpp. clang can't
# resolve these across TUs on its own, so we parse config.cpp once up front
# and look these up by fully-qualified name.
EXTERN_CONST_SOURCES = ["config.cpp"]


def qualified_name(cursor: cindex.Cursor) -> str:
    parts = []
    c = cursor
    while c is not None and c.kind != cindex.CursorKind.TRANSLATION_UNIT:
        if c.kind in (
            cindex.CursorKind.NAMESPACE,
            cindex.CursorKind.VAR_DECL,
            cindex.CursorKind.ENUM_DECL,
        ):
            parts.append(c.spelling)
        c = c.semantic_parent
    return "::".join(reversed(parts))


def strip_int_suffix(text: str) -> str:
    return re.sub(r"\b(0[xX][0-9a-fA-F]+|\d+)[uUlL]+\b", r"\1", text)


def build_extern_const_table(src_dir: Path) -> dict[str, int]:
    """Parses config.cpp (etc.) for extern-const definitions, returning
    qualified_name -> integer value, for the handful of cross-TU constants
    the data tables reference symbolically (e.g. SPELL_TYPE_MAGE)."""
    table: dict[str, int] = {}
    index = cindex.Index.create()
    for filename in EXTERN_CONST_SOURCES:
        path = src_dir / filename
        tu = index.parse(str(path), args=CLANG_ARGS + ["-I", str(src_dir)])

        resolved_path = path.resolve()

        def visit(cursor: cindex.Cursor) -> None:
            if cursor.location.file is not None and Path(str(cursor.location.file)).resolve() != resolved_path:
                return  # skip anything pulled in from system/library headers
            if cursor.kind == cindex.CursorKind.VAR_DECL:
                children = list(cursor.get_children())
                literal = unwrap_casts(children[-1]) if children else None
                if literal is not None and is_simple_integer_expr(literal):
                    table[qualified_name(cursor)] = eval_integer_expr(literal)
            for child in cursor.get_children():
                visit(child)

        for child in tu.cursor.get_children():
            visit(child)
    return table


def unwrap_casts(cursor: cindex.Cursor) -> cindex.Cursor:
    while cursor.kind in (
        cindex.CursorKind.UNEXPOSED_EXPR,
        cindex.CursorKind.CSTYLE_CAST_EXPR,
        cindex.CursorKind.PAREN_EXPR,
    ):
        children = list(cursor.get_children())
        if not children:
            break
        cursor = children[0]
    return cursor


def is_simple_integer_expr(cursor: cindex.Cursor) -> bool:
    return cursor.kind in (
        cindex.CursorKind.INTEGER_LITERAL,
        cindex.CursorKind.UNARY_OPERATOR,
        cindex.CursorKind.CHARACTER_LITERAL,
    )


def eval_integer_expr(cursor: cindex.Cursor) -> int:
    text = strip_int_suffix(source_text(cursor)).strip()
    if re.fullmatch(r"'(\\.|[^'\\])'", text):
        inner = text[1:-1]
        return ord(decode_char_escape(inner))
    if not re.fullmatch(r"-?\s*(0[xX][0-9a-fA-F]+|\d+)", text.replace(" ", "")):
        raise SystemExit(f"unexpected literal form for constant folding: {text!r}")
    return int(text.replace(" ", ""), 0)


def decode_char_escape(inner: str) -> str:
    table = {"\\n": "\n", "\\t": "\t", "\\0": "\0", "\\\\": "\\", "\\'": "'", '\\"': '"'}
    return table.get(inner, inner)


def scalar_to_int(value: tuple[str, str]) -> int:
    kind, text = value
    if kind == "bool":
        return 1 if text == "true" else 0
    if kind == "char":
        inner = text[1:-1]
        return ord(decode_char_escape(inner))
    return int(text, 0)


def source_text(cursor: cindex.Cursor) -> str:
    extent = cursor.extent
    tokens = list(cursor.get_tokens())
    if not tokens:
        return ""
    return "".join(
        t.spelling + (" " if i < len(tokens) - 1 and needs_space(t, tokens[i + 1]) else "")
        for i, t in enumerate(tokens)
    )


def needs_space(a: cindex.Token, b: cindex.Token) -> bool:
    # Minimal spacing: keep punctuation tight, separate word-ish tokens.
    return a.spelling[-1:].isalnum() or a.spelling[-1:] == "_"


class Extractor:
    def __init__(self, src_dir: Path, extern_consts: dict[str, int]):
        self.src_dir = src_dir
        self.extern_consts = extern_consts
        self.structs_emitted: dict[str, list[tuple[str, cindex.Type]]] = {}

    # ---- symbol resolution -------------------------------------------------

    def resolve_decl_ref(self, cursor: cindex.Cursor) -> int:
        referenced = cursor.referenced
        if referenced is None:
            raise SystemExit(f"unresolved reference: {source_text(cursor)!r}")

        if referenced.kind == cindex.CursorKind.ENUM_CONSTANT_DECL:
            return referenced.enum_value

        defn = referenced.get_definition() or referenced
        children = list(defn.get_children())
        init = unwrap_casts(children[-1]) if children else None
        if init is not None and init.kind != cindex.CursorKind.TYPE_REF:
            return scalar_to_int(self.eval_scalar(init, defn.type))

        qname = qualified_name(referenced)
        if qname in self.extern_consts:
            return self.extern_consts[qname]
        bare = referenced.spelling
        if bare in self.extern_consts:
            return self.extern_consts[bare]
        raise SystemExit(f"could not resolve symbolic constant: {qname or bare!r}")

    # ---- scalar leaves ------------------------------------------------------

    def eval_scalar(self, cursor: cindex.Cursor, field_type: cindex.Type) -> object:
        """Returns either an int (for integer/bool/char fields) or a str
        (already-quoted C# string literal, or 'null') for pointer-to-char
        fields. field_type is the *canonical* field type."""
        canonical = field_type.get_canonical()

        if cursor.kind == cindex.CursorKind.UNARY_OPERATOR:
            tokens = list(cursor.get_tokens())
            op = tokens[0].spelling
            if op not in ("-", "+"):
                raise SystemExit(f"unhandled unary operator {op!r} in {source_text(cursor)!r}")
            inner = next(cursor.get_children())
            kind, text = self.eval_scalar(inner, field_type)
            return (kind, f"{op}{text}" if op == "-" else text)

        if canonical.kind == cindex.TypeKind.POINTER:
            if cursor.kind == cindex.CursorKind.STRING_LITERAL:
                return cursor.spelling  # includes the original escaping
            # CNIL (constexpr char* = nullptr) or a bare 0/nullptr literal.
            return None

        if cursor.kind == cindex.CursorKind.CHARACTER_LITERAL:
            text = strip_int_suffix(source_text(cursor))
            return ("char", text)  # emit verbatim as a C# char literal

        if cursor.kind in (cindex.CursorKind.INTEGER_LITERAL, cindex.CursorKind.FLOATING_LITERAL):
            text = strip_int_suffix(source_text(cursor))
            return ("num", text)

        if cursor.kind == cindex.CursorKind.CXX_BOOL_LITERAL_EXPR:
            return ("bool", source_text(cursor))

        if cursor.kind == cindex.CursorKind.DECL_REF_EXPR:
            value = self.resolve_decl_ref(cursor)
            return ("num", str(value))

        if cursor.kind in (
            cindex.CursorKind.PAREN_EXPR,
            cindex.CursorKind.UNEXPOSED_EXPR,
            cindex.CursorKind.CSTYLE_CAST_EXPR,
        ):
            return self.eval_scalar(next(cursor.get_children()), field_type)

        raise SystemExit(f"unhandled scalar init kind {cursor.kind} ({source_text(cursor)!r})")

    # ---- struct shape --------------------------------------------------------

    def struct_fields(self, record_type: cindex.Type) -> list[tuple[str, cindex.Type]]:
        decl = record_type.get_declaration()
        return [
            (f.spelling, f.type)
            for f in decl.get_children()
            if f.kind == cindex.CursorKind.FIELD_DECL
        ]

    def csharp_struct_name(self, record_type: cindex.Type) -> str:
        decl_name = record_type.get_declaration().spelling
        if decl_name in ("", None):
            # anonymous struct behind a typedef -- use the typedef's name instead.
            decl_name = record_type.spelling.replace("struct ", "").strip()
        return STRUCT_RENAME.get(decl_name, decl_name)

    def register_struct(self, record_type: cindex.Type) -> str:
        decl_name = record_type.get_declaration().spelling
        csharp_name = self.csharp_struct_name(record_type)
        if csharp_name == STRUCT_RENAME["Dice_t"]:
            return csharp_name
        if csharp_name not in self.structs_emitted:
            self.structs_emitted[csharp_name] = self.struct_fields(record_type)
        return csharp_name

    # ---- init-list walk -------------------------------------------------------

    def convert(self, cursor: cindex.Cursor, field_type: cindex.Type) -> str:
        canonical = field_type.get_canonical()

        if canonical.kind == cindex.TypeKind.CONSTANTARRAY:
            element_type = canonical.get_array_element_type()
            size = canonical.element_count
            if cursor.kind != cindex.CursorKind.INIT_LIST_EXPR:
                raise SystemExit(f"expected braced initializer for array field, got {cursor.kind}")
            elements = list(cursor.get_children())
            if len(elements) > size:
                raise SystemExit(f"too many initializers ({len(elements)}) for array[{size}]")
            parts = [self.convert(e, element_type) for e in elements]
            parts += [self.zero_value(element_type) for _ in range(size - len(elements))]
            element_csharp_type = field_csharp_type(element_type, self)
            return f"new {element_csharp_type}[] {{ " + ", ".join(parts) + " }"

        if canonical.kind == cindex.TypeKind.RECORD:
            csharp_name = self.register_struct(canonical)
            fields = self.struct_fields(canonical)
            if cursor.kind != cindex.CursorKind.INIT_LIST_EXPR:
                raise SystemExit(f"expected braced initializer for struct field, got {cursor.kind}")
            elements = list(cursor.get_children())
            if len(elements) > len(fields):
                raise SystemExit(f"too many initializers for struct {csharp_name}")
            args = []
            for i, (fname, ftype) in enumerate(fields):
                if i < len(elements):
                    args.append(self.convert(elements[i], ftype))
                else:
                    args.append(self.zero_value(ftype))
            return f"new {csharp_name}(" + ", ".join(args) + ")"

        return self.format_scalar(self.eval_scalar(cursor, canonical), canonical)

    def zero_value(self, field_type: cindex.Type) -> str:
        canonical = field_type.get_canonical()
        if canonical.kind == cindex.TypeKind.CONSTANTARRAY:
            element_type = canonical.get_array_element_type()
            size = canonical.element_count
            element_csharp_type = field_csharp_type(element_type, self)
            return f"new {element_csharp_type}[] {{ " + ", ".join(self.zero_value(element_type) for _ in range(size)) + " }"
        if canonical.kind == cindex.TypeKind.RECORD:
            csharp_name = self.register_struct(canonical)
            fields = self.struct_fields(canonical)
            return f"new {csharp_name}(" + ", ".join(self.zero_value(t) for _, t in fields) + ")"
        if canonical.kind == cindex.TypeKind.POINTER:
            return "null"
        return "0"

    def format_scalar(self, value, canonical: cindex.Type) -> str:
        csharp_type = map_builtin_type(canonical)
        if canonical.kind == cindex.TypeKind.POINTER:
            return "null" if value is None else value
        kind, text = value
        if kind == "bool":
            return text
        if kind == "char":
            return f"(byte){text}" if csharp_type == "byte" else text
        # numeric literal: carried through verbatim, only suffixed when the
        # literal's magnitude needs an explicit type to avoid a C# overflow
        # error (e.g. a uint field whose hex literal's top bit is set).
        if csharp_type == "uint":
            return f"{text}u" if not text.lstrip("-").rstrip().endswith(("u", "U")) else text
        if csharp_type == "ulong":
            return f"{text}ul"
        return text


def map_builtin_type(canonical: cindex.Type) -> str:
    kind = canonical.kind
    T = cindex.TypeKind
    if kind == T.POINTER:
        return "string"
    mapped = {
        T.BOOL: "bool",
        T.UCHAR: "byte",
        T.SCHAR: "sbyte",
        T.CHAR_S: "sbyte",
        T.USHORT: "ushort",
        T.SHORT: "short",
        T.UINT: "uint",
        T.INT: "int",
        T.ULONG: "ulong",
        T.LONG: "long",
        T.ULONGLONG: "ulong",
        T.LONGLONG: "long",
    }.get(kind)
    if mapped is None:
        raise SystemExit(f"no C# type mapping for clang type kind {kind} ({canonical.spelling})")
    return mapped


def pascal_case(name: str) -> str:
    return "".join(part.capitalize() for part in name.split("_"))


def field_csharp_type(field_type: cindex.Type, extractor: Extractor) -> str:
    canonical = field_type.get_canonical()
    if canonical.kind == cindex.TypeKind.CONSTANTARRAY:
        inner = field_csharp_type(canonical.get_array_element_type(), extractor)
        return f"{inner}[]"
    if canonical.kind == cindex.TypeKind.RECORD:
        return extractor.csharp_struct_name(canonical)
    return map_builtin_type(canonical)


def array_csharp_type(var_type: cindex.Type, extractor: Extractor) -> str:
    canonical = var_type.get_canonical()
    if canonical.kind != cindex.TypeKind.CONSTANTARRAY:
        raise SystemExit(f"expected top-level array type, got {canonical.kind}")
    element = canonical.get_array_element_type()
    return field_csharp_type(element, extractor) + "[]"


STRUCT_HEADER = """// <auto-generated by dotnet/tools/CppModelExtractor/extract_data_tables.py
// from src/{source}> -- do not hand-edit, regenerate instead.
namespace Umoria.Core.Model.Generated;

public readonly partial record struct {name}({fields});
"""


def write_struct_file(out_dir: Path, name: str, fields: list[tuple[str, cindex.Type]], extractor: Extractor, source: str) -> None:
    field_decls = ", ".join(f"{field_csharp_type(ftype, extractor)} {pascal_case(fname)}" for fname, ftype in fields)
    text = STRUCT_HEADER.format(source=source, name=name, fields=field_decls)
    (out_dir / f"{name}.cs").write_text(text, encoding="utf-8", newline="\n")


DATA_HEADER = """// <auto-generated by dotnet/tools/CppModelExtractor/extract_data_tables.py
// from src/{source}> -- do not hand-edit, regenerate instead.
using Umoria.Core.Model.Generated;

namespace Umoria.Core.Data.Generated;

public static class {class_name}
{{
"""


def write_data_file(out_dir: Path, class_name: str, tables: list[tuple[str, str, str]], source: str) -> None:
    lines = [DATA_HEADER.format(source=source, class_name=class_name)]
    for prop_name, csharp_type, literal in tables:
        lines.append(f"    public static readonly {csharp_type} {prop_name} =\n    {literal};\n\n")
    lines.append("}\n")
    (out_dir / f"{class_name}.cs").write_text("".join(lines), encoding="utf-8", newline="\n")


def process_file(path: Path, extractor: Extractor) -> list[tuple[str, str, str]]:
    index = cindex.Index.create()
    tu = index.parse(str(path), args=CLANG_ARGS + ["-I", str(extractor.src_dir)])

    resolved_path = path.resolve()
    tables: list[tuple[str, str, str]] = []
    for cursor in tu.cursor.get_children():
        if cursor.kind != cindex.CursorKind.VAR_DECL:
            continue
        if cursor.location.file is None or Path(str(cursor.location.file)).resolve() != resolved_path:
            continue
        if cursor.type.get_canonical().kind != cindex.TypeKind.CONSTANTARRAY:
            continue
        children = list(cursor.get_children())
        init = children[-1] if children else None
        if init is None or init.kind != cindex.CursorKind.INIT_LIST_EXPR:
            continue  # declaration only (`extern`), not a definition with data

        csharp_type = array_csharp_type(cursor.type, extractor)
        literal = extractor.convert(init, cursor.type)
        prop_name = pascal_case(cursor.spelling)
        tables.append((prop_name, csharp_type, literal))

    return tables


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit(f"usage: {sys.argv[0]} <repo-root>")

    repo_root = Path(sys.argv[1])
    src_dir = repo_root / "src"
    model_out = repo_root / "dotnet/src/Umoria.Core/Model/Generated"
    data_out = repo_root / "dotnet/src/Umoria.Core/Data/Generated"
    model_out.mkdir(parents=True, exist_ok=True)
    data_out.mkdir(parents=True, exist_ok=True)

    extern_consts = build_extern_const_table(src_dir)
    extractor = Extractor(src_dir, extern_consts)

    for filename in DATA_FILES:
        path = src_dir / filename
        tables = process_file(path, extractor)
        if not tables:
            raise SystemExit(f"no array definitions found in {filename} -- check CLANG_ARGS/include path")
        class_name = pascal_case(filename.removesuffix(".cpp")) + "Data"
        print(f"{filename}: {len(tables)} table(s) -> {class_name}.cs")
        for prop_name, csharp_type, _ in tables:
            print(f"    {prop_name}: {csharp_type}")
        write_data_file(data_out, class_name, tables, filename)

    for name, fields in extractor.structs_emitted.items():
        source = next(s for s, cs in STRUCT_RENAME.items() if cs == name)
        print(f"struct {name}: {len(fields)} field(s)")
        write_struct_file(model_out, name, fields, extractor, source_for_struct(source))

    print("done.")


def source_for_struct(cpp_name: str) -> str:
    return {
        "Creature_t": "monster.h",
        "DungeonObject_t": "dungeon.h",
        "Race_t": "character.h",
        "Class_t": "character.h",
        "Background_t": "character.h",
        "Spell_t": "spells.h",
        "Owner_t": "store.h",
        "MonsterAttack_t": "monster.h",
    }[cpp_name]


if __name__ == "__main__":
    main()
