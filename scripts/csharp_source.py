"""Mask C# comments and literal text while retaining executable interpolation code.

This lexical pass is deliberately conservative: it preserves the original length
and line breaks for diagnostic locations. It is not a C# semantic parser; package
and transitive project-reference checks complement this source-level guard.
"""


def mask_non_code(source: str) -> str:
    masked = list(source)
    end = len(source)

    def hide(start: int, stop: int) -> None:
        for offset in range(start, stop):
            if source[offset] not in "\r\n":
                masked[offset] = " "

    def scan_code(index: int, expression: bool = False) -> int:
        depth = 0
        while index < end:
            if source.startswith("//", index):
                stop = source.find("\n", index)
                stop = end if stop < 0 else stop
                hide(index, stop)
                index = stop
                continue
            if source.startswith("/*", index):
                stop = source.find("*/", index + 2)
                stop = end if stop < 0 else stop + 2
                hide(index, stop)
                index = stop
                continue

            start = index
            verbatim = False
            dollars = 0
            if source.startswith("@$", index) or source.startswith("$@", index):
                dollars, verbatim = 1, True
                index += 2
            elif source[index] == "$":
                while index < end and source[index] == "$":
                    dollars += 1
                    index += 1
            elif source[index] == "@":
                verbatim = True
                index += 1

            if index < end and source[index] == '"':
                quote_end = index
                while quote_end < end and source[quote_end] == '"':
                    quote_end += 1
                raw_quotes = quote_end - index if quote_end - index >= 3 else 0
                index = scan_string(start, index, dollars, verbatim, raw_quotes)
                continue
            if start != index:
                index = start

            if source[index] == "'":
                stop = index + 1
                while stop < end:
                    if source[stop] == "\\":
                        stop += 2
                    elif source[stop] == "'":
                        stop += 1
                        break
                    else:
                        stop += 1
                hide(index, min(stop, end))
                index = min(stop, end)
                continue
            if expression:
                if source[index] == "{":
                    depth += 1
                elif source[index] == "}":
                    if depth == 0:
                        return index
                    depth -= 1
            index += 1
        return end

    def scan_string(start: int, quote: int, dollars: int, verbatim: bool, raw_quotes: int) -> int:
        opening = raw_quotes if raw_quotes else 1
        index = quote + opening
        hide(start, index)
        while index < end:
            if raw_quotes:
                if source.startswith('"' * raw_quotes, index):
                    hide(index, index + raw_quotes)
                    return index + raw_quotes
            else:
                if source[index] == '"':
                    if verbatim and source.startswith('""', index):
                        hide(index, index + 2)
                        index += 2
                        continue
                    hide(index, index + 1)
                    return index + 1
                if not verbatim and source[index] == "\\":
                    hide(index, min(index + 2, end))
                    index += 2
                    continue

            if dollars and source[index] == "{":
                if raw_quotes:
                    # Raw interpolations use one '{' per '$' in the string prefix.
                    opening_braces = "{" * dollars
                    if not source.startswith(opening_braces, index):
                        hide(index, index + 1)
                        index += 1
                        continue
                elif source.startswith("{{", index):
                    hide(index, index + 2)
                    index += 2
                    continue

                brace_count = dollars if raw_quotes else 1
                hide(index, index + brace_count)
                index = scan_code(index + brace_count, expression=True)
                if index < end and source.startswith("}" * brace_count, index):
                    hide(index, index + brace_count)
                    index += brace_count
                continue
            hide(index, index + 1)
            index += 1
        return end

    scan_code(0)
    return "".join(masked)
